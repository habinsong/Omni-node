#ifndef _WIN32
#define _GNU_SOURCE
#endif
#include <ctype.h>
#include <errno.h>
#include <signal.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <winsock2.h>
#include <windows.h>
#pragma comment(lib, "Ws2_32.lib")
#else
#include <fcntl.h>
#include <poll.h>
#include <sys/file.h>
#include <sys/socket.h>
#include <sys/stat.h>
#include <sys/types.h>
#include <sys/un.h>
#include <unistd.h>
#endif

#ifndef _WIN32
#define LOCK_PATH_TEMPLATE "/tmp/omninode.%u.lock"
#define SOCKET_PATH_TEMPLATE "/tmp/omninode_core.%u.sock"
#endif
#define WINDOWS_DEFAULT_CORE_PORT 51808
#define MAX_CLIENTS 64
#define IO_BUFFER_SIZE 4096

static volatile sig_atomic_t g_should_stop = 0;
static char g_auth_token[129] = {0};
#ifdef _WIN32
static HANDLE g_lock_handle = NULL;
#else
static int g_lock_fd = -1;
static char g_lock_path[108] = {0};
static char g_socket_path[108] = {0};
#endif

static void on_signal(int signo) {
    (void)signo;
    g_should_stop = 1;
}

static bool is_tcp_endpoint(const char *value) {
    const char *tcp_prefix = "tcp://";
    return value != NULL && strncmp(value, tcp_prefix, strlen(tcp_prefix)) == 0;
}

#ifdef _WIN32
static bool write_all(SOCKET fd, const char *buffer, size_t length) {
    size_t written = 0;
    while (written < length) {
        int result = send(fd, buffer + written, (int)(length - written), 0);
        if (result < 0) {
            int error_code = WSAGetLastError();
            if (error_code == WSAEINTR || error_code == WSAEWOULDBLOCK) {
                continue;
            }
            return false;
        }

        if (result == 0) {
            return false;
        }

        written += (size_t)result;
    }

    return true;
}
#else
static bool write_all(int fd, const char *buffer, size_t length) {
    size_t written = 0;
    while (written < length) {
        ssize_t result = send(fd, buffer + written, length - written, 0);
        if (result < 0) {
            if (errno == EINTR) {
                continue;
            }
            if (errno == EAGAIN || errno == EWOULDBLOCK) {
                continue;
            }
            return false;
        }

        if (result == 0) {
            return false;
        }

        written += (size_t)result;
    }

    return true;
}
#endif

#ifndef _WIN32
static int set_nonblocking(int fd) {
    int flags = fcntl(fd, F_GETFL, 0);
    if (flags < 0) {
        return -1;
    }

    if (fcntl(fd, F_SETFL, flags | O_NONBLOCK) < 0) {
        return -1;
    }

    return 0;
}

static int acquire_single_instance_lock(void) {
    const uid_t uid = getuid();
    int flags = O_RDWR | O_CREAT | O_EXCL;
#ifdef O_CLOEXEC
    flags |= O_CLOEXEC;
#endif
#ifdef O_NOFOLLOW
    flags |= O_NOFOLLOW;
#endif

    snprintf(g_lock_path, sizeof(g_lock_path), LOCK_PATH_TEMPLATE, (unsigned int)uid);

    int fd = open(g_lock_path, flags, 0600);
    if (fd < 0 && errno == EEXIST) {
        int open_flags = O_RDWR;
#ifdef O_CLOEXEC
        open_flags |= O_CLOEXEC;
#endif
#ifdef O_NOFOLLOW
        open_flags |= O_NOFOLLOW;
#endif
        fd = open(g_lock_path, open_flags, 0600);
    }

    if (fd < 0) {
        perror("failed to open lock file");
        return -1;
    }

    struct stat st;
    if (fstat(fd, &st) != 0) {
        perror("fstat(lock)");
        close(fd);
        return -1;
    }

    if (!S_ISREG(st.st_mode)) {
        fprintf(stderr, "lock path is not a regular file: %s\n", g_lock_path);
        close(fd);
        return -1;
    }

    if (fchmod(fd, S_IRUSR | S_IWUSR) != 0) {
        perror("fchmod(lock)");
    }

    if (flock(fd, LOCK_EX | LOCK_NB) != 0) {
        if (errno == EWOULDBLOCK || errno == EAGAIN) {
            fprintf(stderr, "omninode_core is already running (lock: %s)\n", g_lock_path);
        } else {
            perror("flock(lock)");
        }
        close(fd);
        return -1;
    }

    if (ftruncate(fd, 0) == 0) {
        dprintf(fd, "%ld\n", (long)getpid());
    }

    g_lock_fd = fd;
    return 0;
}

static int setup_server_socket(const char *path) {
    int server_fd = socket(AF_UNIX, SOCK_STREAM, 0);
    struct sockaddr_un addr;

    if (server_fd < 0) {
        perror("socket");
        return -1;
    }

    if (set_nonblocking(server_fd) != 0) {
        perror("fcntl");
        close(server_fd);
        return -1;
    }

    memset(&addr, 0, sizeof(addr));
    addr.sun_family = AF_UNIX;
    strncpy(addr.sun_path, path, sizeof(addr.sun_path) - 1);

    mode_t previous_umask = umask(0077);
    unlink(path);
    if (bind(server_fd, (struct sockaddr *)&addr, sizeof(addr)) != 0) {
        umask(previous_umask);
        perror("bind");
        close(server_fd);
        return -1;
    }
    umask(previous_umask);

    if (chmod(path, 0600) != 0) {
        perror("chmod");
    }

    if (listen(server_fd, 64) != 0) {
        perror("listen");
        close(server_fd);
        unlink(path);
        return -1;
    }

    return server_fd;
}

static bool validate_peer_uid(int client_fd) {
#if defined(__linux__)
    struct ucred cred;
    socklen_t len = sizeof(cred);
    if (getsockopt(client_fd, SOL_SOCKET, SO_PEERCRED, &cred, &len) != 0) {
        perror("getsockopt(SO_PEERCRED)");
        return false;
    }

    return cred.uid == getuid();
#elif defined(__APPLE__)
    uid_t peer_uid = (uid_t)-1;
    gid_t peer_gid = (gid_t)-1;
    if (getpeereid(client_fd, &peer_uid, &peer_gid) != 0) {
        perror("getpeereid");
        return false;
    }

    (void)peer_gid;
    return peer_uid == getuid();
#else
    return true;
#endif
}
#endif

static bool init_auth_token(void) {
    const char *env = getenv("OMNINODE_CORE_AUTH_TOKEN");
    if (env == NULL || *env == '\0') {
        return false;
    }

    size_t len = strlen(env);
    if (len >= sizeof(g_auth_token)) {
        return false;
    }

    memcpy(g_auth_token, env, len);
    g_auth_token[len] = '\0';
    return true;
}

static char *next_token(char **cursor) {
    char *start = NULL;
    if (cursor == NULL || *cursor == NULL) {
        return NULL;
    }

    while (**cursor != '\0' && isspace((unsigned char)**cursor)) {
        (*cursor)++;
    }

    if (**cursor == '\0') {
        return NULL;
    }

    start = *cursor;
    while (**cursor != '\0' && !isspace((unsigned char)**cursor)) {
        (*cursor)++;
    }

    if (**cursor != '\0') {
        **cursor = '\0';
        (*cursor)++;
    }

    return start;
}

static bool resolve_posix_socket_path(char *out, size_t out_len) {
    const char *env = getenv("OMNINODE_CORE_SOCKET_PATH");
    if (env != NULL && *env != '\0' && !is_tcp_endpoint(env)) {
        size_t len = strlen(env);
        if (len >= out_len) {
            return false;
        }

        memcpy(out, env, len);
        out[len] = '\0';
        return true;
    }

    size_t len = snprintf(out, out_len, SOCKET_PATH_TEMPLATE, (unsigned int)getuid());
    if (len == 0 || len >= out_len) {
        return false;
    }

    return true;
}

#ifdef _WIN32
static int set_nonblocking(SOCKET fd) {
    u_long mode = 1;
    return ioctlsocket(fd, FIONBIO, &mode) == 0 ? 0 : -1;
}

static int acquire_single_instance_lock(void) {
    g_lock_handle = CreateMutexA(NULL, TRUE, "Local\\OmniNodeCore");
    if (g_lock_handle == NULL) {
        fprintf(stderr, "failed to create mutex: %lu\n", GetLastError());
        return -1;
    }

    if (GetLastError() == ERROR_ALREADY_EXISTS) {
        fprintf(stderr, "omninode_core is already running (mutex: Local\\OmniNodeCore)\n");
        CloseHandle(g_lock_handle);
        g_lock_handle = NULL;
        return -1;
    }

    return 0;
}

static unsigned short parse_port_value(const char *value, unsigned short fallback) {
    char *endptr = NULL;
    long parsed = 0;
    if (value == NULL || *value == '\0') {
        return fallback;
    }

    parsed = strtol(value, &endptr, 10);
    if (endptr == value || parsed <= 0 || parsed > 65535) {
        return fallback;
    }

    return (unsigned short)parsed;
}

static unsigned short resolve_windows_core_port(void) {
    const char *port_env = getenv("OMNINODE_CORE_TCP_PORT");
    const char *endpoint_env = getenv("OMNINODE_CORE_SOCKET_PATH");
    const char *tcp_prefix = "tcp://127.0.0.1:";

    if (port_env != NULL && *port_env != '\0') {
        return parse_port_value(port_env, WINDOWS_DEFAULT_CORE_PORT);
    }

    if (endpoint_env != NULL && strncmp(endpoint_env, tcp_prefix, strlen(tcp_prefix)) == 0) {
        return parse_port_value(endpoint_env + strlen(tcp_prefix), WINDOWS_DEFAULT_CORE_PORT);
    }

    return WINDOWS_DEFAULT_CORE_PORT;
}

static SOCKET setup_server_socket(unsigned short port) {
    SOCKET server_fd = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    struct sockaddr_in addr;
    BOOL reuse = TRUE;

    if (server_fd == INVALID_SOCKET) {
        fprintf(stderr, "socket failed: %d\n", WSAGetLastError());
        return INVALID_SOCKET;
    }

    setsockopt(server_fd, SOL_SOCKET, SO_REUSEADDR, (const char *)&reuse, sizeof(reuse));

    if (set_nonblocking(server_fd) != 0) {
        fprintf(stderr, "ioctlsocket failed: %d\n", WSAGetLastError());
        closesocket(server_fd);
        return INVALID_SOCKET;
    }

    memset(&addr, 0, sizeof(addr));
    addr.sin_family = AF_INET;
    addr.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
    addr.sin_port = htons(port);

    if (bind(server_fd, (struct sockaddr *)&addr, sizeof(addr)) != 0) {
        fprintf(stderr, "bind failed: %d\n", WSAGetLastError());
        closesocket(server_fd);
        return INVALID_SOCKET;
    }

    if (listen(server_fd, 64) != 0) {
        fprintf(stderr, "listen failed: %d\n", WSAGetLastError());
        closesocket(server_fd);
        return INVALID_SOCKET;
    }

    return server_fd;
}
#endif

static long get_mem_free_mb(void) {
#ifdef _WIN32
    MEMORYSTATUSEX status;
    memset(&status, 0, sizeof(status));
    status.dwLength = sizeof(status);
    if (!GlobalMemoryStatusEx(&status)) {
        return -1;
    }

    return (long)(status.ullAvailPhys / (1024ULL * 1024ULL));
#else
#if defined(_SC_AVPHYS_PAGES)
    long pages = sysconf(_SC_AVPHYS_PAGES);
#elif defined(_SC_PHYS_PAGES)
    long pages = sysconf(_SC_PHYS_PAGES);
#else
    long pages = -1;
#endif
    long page_size = sysconf(_SC_PAGESIZE);

    if (pages <= 0 || page_size <= 0) {
        return -1;
    }

    return (long)(((double)pages * (double)page_size) / (1024.0 * 1024.0));
#endif
}

static double get_cpu_load_1m(void) {
#ifdef _WIN32
    return -1.0;
#else
    double loadavg[3] = {0.0, 0.0, 0.0};
    if (getloadavg(loadavg, 1) == 1) {
        return loadavg[0];
    }
    return -1.0;
#endif
}

static void build_error_response(char *out, size_t out_len, const char *message) {
    snprintf(out, out_len, "status=error message=%s", message);
}

static bool terminate_process_by_pid(long long pid, char *error_msg, size_t error_msg_len) {
#ifdef _WIN32
    HANDLE process = OpenProcess(PROCESS_TERMINATE, FALSE, (DWORD)pid);
    if (process == NULL) {
        snprintf(error_msg, error_msg_len, "open process failed: %lu", GetLastError());
        return false;
    }

    if (!TerminateProcess(process, 1)) {
        snprintf(error_msg, error_msg_len, "terminate failed: %lu", GetLastError());
        CloseHandle(process);
        return false;
    }

    CloseHandle(process);
    return true;
#else
    if (kill((pid_t)pid, SIGTERM) != 0) {
        snprintf(error_msg, error_msg_len, "kill failed: %s", strerror(errno));
        return false;
    }

    return true;
#endif
}

static bool parse_command_request(const char *request, char *action, size_t action_len, long long *pid) {
    char line[IO_BUFFER_SIZE];
    size_t line_len = 0;
    char *endptr = NULL;
    char *cursor = NULL;
    char *command = NULL;
    char *auth = NULL;
    char *pid_text = NULL;

    if (action_len == 0) {
        return false;
    }

    if (request == NULL) {
        return false;
    }

    line_len = strcspn(request, "\r\n");
    if (line_len == 0 || line_len >= sizeof(line)) {
        return false;
    }

    memcpy(line, request, line_len);
    line[line_len] = '\0';

    cursor = line;
    while (*cursor != '\0' && isspace((unsigned char)*cursor)) {
        cursor++;
    }

    char *tail = cursor + strlen(cursor);
    while (tail > cursor && isspace((unsigned char)tail[-1])) {
        tail--;
    }
    *tail = '\0';

    command = next_token(&cursor);
    if (command == NULL) {
        return false;
    }

    if (strcmp(command, "get_metrics") == 0 || strcmp(command, "metrics") == 0) {
        auth = next_token(&cursor);
        if (auth == NULL || strcmp(auth, g_auth_token) != 0 || next_token(&cursor) != NULL) {
            return false;
        }
        snprintf(action, action_len, "get_metrics");
        if (pid != NULL) {
            *pid = 0;
        }
        return true;
    }

    if (strcmp(command, "kill") == 0) {
        auth = next_token(&cursor);
        pid_text = next_token(&cursor);
        if (auth == NULL || pid_text == NULL || strcmp(auth, g_auth_token) != 0 || next_token(&cursor) != NULL) {
            return false;
        }

        errno = 0;
        long long parsed_pid = strtoll(pid_text, &endptr, 10);

        if (errno != 0 || endptr == pid_text || (endptr != NULL && *endptr != '\0') || parsed_pid <= 1) {
            return false;
        }

        snprintf(action, action_len, "kill");
        if (pid != NULL) {
            *pid = parsed_pid;
        }
        return true;
    }

    return false;
}

static void handle_request(const char *request, char *response, size_t response_len) {
    char action[64] = {0};
    long long pid = 0;

    if (!parse_command_request(request, action, sizeof(action), &pid)) {
        build_error_response(response, response_len, "invalid command");
        return;
    }

    if (strcmp(action, "get_metrics") == 0) {
        double cpu_load = get_cpu_load_1m();
        long mem_free_mb = get_mem_free_mb();
        snprintf(
            response,
            response_len,
            "status=ok cpu_usage=%.2f mem_free_mb=%ld",
            cpu_load,
            mem_free_mb
        );
        return;
    }

    if (strcmp(action, "kill") == 0) {
        char error_msg[128];
        if (!terminate_process_by_pid(pid, error_msg, sizeof(error_msg))) {
            build_error_response(response, response_len, error_msg);
            return;
        }

        snprintf(response, response_len, "status=ok killed_pid=%lld", pid);
        return;
    }

    build_error_response(response, response_len, "unknown command");
}

#ifndef _WIN32
static void close_client(struct pollfd *entry) {
    if (entry->fd >= 0) {
        close(entry->fd);
        entry->fd = -1;
        entry->events = 0;
        entry->revents = 0;
    }
}

static bool register_client(struct pollfd *clients, int client_fd) {
    for (int i = 1; i < MAX_CLIENTS + 1; ++i) {
        if (clients[i].fd < 0) {
            clients[i].fd = client_fd;
            clients[i].events = POLLIN;
            clients[i].revents = 0;
            return true;
        }
    }

    return false;
}

int main(void) {
    int server_fd = -1;
    struct pollfd poll_fds[MAX_CLIENTS + 1];
    const uid_t uid = getuid();

    if (acquire_single_instance_lock() != 0) {
        return 1;
    }

    if (!init_auth_token()) {
        fprintf(stderr, "missing or invalid OMNINODE_CORE_AUTH_TOKEN\n");
        if (g_lock_fd >= 0) {
            close(g_lock_fd);
        }
        return 1;
    }

    signal(SIGINT, on_signal);
    signal(SIGTERM, on_signal);

    if (!resolve_posix_socket_path(g_socket_path, sizeof(g_socket_path))) {
        fprintf(stderr, "invalid OMNINODE_CORE_SOCKET_PATH\n");
        if (g_lock_fd >= 0) {
            close(g_lock_fd);
        }
        return 1;
    }
    server_fd = setup_server_socket(g_socket_path);
    if (server_fd < 0) {
        if (g_lock_fd >= 0) {
            close(g_lock_fd);
        }
        return 1;
    }

    for (int i = 0; i < MAX_CLIENTS + 1; ++i) {
        poll_fds[i].fd = -1;
        poll_fds[i].events = 0;
        poll_fds[i].revents = 0;
    }

    poll_fds[0].fd = server_fd;
    poll_fds[0].events = POLLIN;

    fprintf(stderr, "omninode_core started (uid=%u, uds=%s, lock=%s)\n", (unsigned int)uid, g_socket_path, g_lock_path);

    while (!g_should_stop) {
        int ready = poll(poll_fds, MAX_CLIENTS + 1, 500);
        if (ready < 0) {
            if (errno == EINTR) {
                continue;
            }
            perror("poll");
            break;
        }

        if (ready == 0) {
            continue;
        }

        if ((poll_fds[0].revents & POLLIN) != 0) {
            while (true) {
                int client_fd = accept(server_fd, NULL, NULL);
                if (client_fd < 0) {
                    if (errno == EAGAIN || errno == EWOULDBLOCK) {
                        break;
                    }
                    perror("accept");
                    break;
                }

                if (set_nonblocking(client_fd) != 0) {
                    perror("fcntl(client)");
                    close(client_fd);
                    continue;
                }

                if (!validate_peer_uid(client_fd)) {
                    fprintf(stderr, "rejected client: uid mismatch\n");
                    close(client_fd);
                    continue;
                }

                if (!register_client(poll_fds, client_fd)) {
                    const char *busy_msg = "{\"status\":\"error\",\"message\":\"server busy\"}\n";
                    write_all(client_fd, busy_msg, strlen(busy_msg));
                    close(client_fd);
                }
            }
        }

        for (int i = 1; i < MAX_CLIENTS + 1; ++i) {
            struct pollfd *entry = &poll_fds[i];
            char input[IO_BUFFER_SIZE];
            char output[IO_BUFFER_SIZE];

            if (entry->fd < 0) {
                continue;
            }

            if ((entry->revents & (POLLERR | POLLHUP | POLLNVAL)) != 0) {
                close_client(entry);
                continue;
            }

            if ((entry->revents & POLLIN) == 0) {
                continue;
            }

            ssize_t n = recv(entry->fd, input, sizeof(input) - 1, 0);
            if (n <= 0) {
                close_client(entry);
                continue;
            }

            input[n] = '\0';
            memset(output, 0, sizeof(output));
            handle_request(input, output, sizeof(output));
            write_all(entry->fd, output, strlen(output));
            write_all(entry->fd, "\n", 1);
            close_client(entry);
        }
    }

    for (int i = 1; i < MAX_CLIENTS + 1; ++i) {
        close_client(&poll_fds[i]);
    }

    close(server_fd);
    unlink(g_socket_path);

    if (g_lock_fd >= 0) {
        close(g_lock_fd);
    }

    fprintf(stderr, "omninode_core stopped\n");
    return 0;
}
#else
static bool register_windows_client(SOCKET *clients, SOCKET client_fd) {
    for (int i = 0; i < MAX_CLIENTS; ++i) {
        if (clients[i] == INVALID_SOCKET) {
            clients[i] = client_fd;
            return true;
        }
    }

    return false;
}

static void close_windows_client(SOCKET *client_fd) {
    if (*client_fd != INVALID_SOCKET) {
        closesocket(*client_fd);
        *client_fd = INVALID_SOCKET;
    }
}

int main(void) {
    WSADATA wsa_data;
    SOCKET server_fd = INVALID_SOCKET;
    SOCKET clients[MAX_CLIENTS];
    unsigned short port = resolve_windows_core_port();

    if (WSAStartup(MAKEWORD(2, 2), &wsa_data) != 0) {
        fprintf(stderr, "WSAStartup failed\n");
        return 1;
    }

    if (acquire_single_instance_lock() != 0) {
        WSACleanup();
        return 1;
    }

    if (!init_auth_token()) {
        fprintf(stderr, "missing or invalid OMNINODE_CORE_AUTH_TOKEN\n");
        WSACleanup();
        return 1;
    }

    signal(SIGINT, on_signal);
    signal(SIGTERM, on_signal);

    server_fd = setup_server_socket(port);
    if (server_fd == INVALID_SOCKET) {
        if (g_lock_handle != NULL) {
            CloseHandle(g_lock_handle);
            g_lock_handle = NULL;
        }
        WSACleanup();
        return 1;
    }

    for (int i = 0; i < MAX_CLIENTS; ++i) {
        clients[i] = INVALID_SOCKET;
    }

    fprintf(stderr, "omninode_core started (tcp=127.0.0.1:%u, lock=Local\\OmniNodeCore)\n", (unsigned int)port);

    while (!g_should_stop) {
        fd_set read_fds;
        struct timeval timeout;
        int ready = 0;

        FD_ZERO(&read_fds);
        FD_SET(server_fd, &read_fds);
        for (int i = 0; i < MAX_CLIENTS; ++i) {
            if (clients[i] != INVALID_SOCKET) {
                FD_SET(clients[i], &read_fds);
            }
        }

        timeout.tv_sec = 0;
        timeout.tv_usec = 500000;
        ready = select(0, &read_fds, NULL, NULL, &timeout);
        if (ready == SOCKET_ERROR) {
            fprintf(stderr, "select failed: %d\n", WSAGetLastError());
            break;
        }

        if (ready == 0) {
            continue;
        }

        if (FD_ISSET(server_fd, &read_fds)) {
            while (true) {
                SOCKET client_fd = accept(server_fd, NULL, NULL);
                if (client_fd == INVALID_SOCKET) {
                    int error_code = WSAGetLastError();
                    if (error_code == WSAEWOULDBLOCK) {
                        break;
                    }
                    fprintf(stderr, "accept failed: %d\n", error_code);
                    break;
                }

                if (set_nonblocking(client_fd) != 0) {
                    fprintf(stderr, "ioctlsocket(client) failed: %d\n", WSAGetLastError());
                    closesocket(client_fd);
                    continue;
                }

                if (!register_windows_client(clients, client_fd)) {
                    const char *busy_msg = "{\"status\":\"error\",\"message\":\"server busy\"}\n";
                    send(client_fd, busy_msg, (int)strlen(busy_msg), 0);
                    closesocket(client_fd);
                }
            }
        }

        for (int i = 0; i < MAX_CLIENTS; ++i) {
            char input[IO_BUFFER_SIZE];
            char output[IO_BUFFER_SIZE];
            int n = 0;

            if (clients[i] == INVALID_SOCKET || !FD_ISSET(clients[i], &read_fds)) {
                continue;
            }

            n = recv(clients[i], input, sizeof(input) - 1, 0);
            if (n <= 0) {
                close_windows_client(&clients[i]);
                continue;
            }

            input[n] = '\0';
            memset(output, 0, sizeof(output));
            handle_request(input, output, sizeof(output));
            send(clients[i], output, (int)strlen(output), 0);
            send(clients[i], "\n", 1, 0);
            close_windows_client(&clients[i]);
        }
    }

    for (int i = 0; i < MAX_CLIENTS; ++i) {
        close_windows_client(&clients[i]);
    }

    closesocket(server_fd);
    if (g_lock_handle != NULL) {
        CloseHandle(g_lock_handle);
        g_lock_handle = NULL;
    }
    WSACleanup();

    fprintf(stderr, "omninode_core stopped\n");
    return 0;
}
#endif
