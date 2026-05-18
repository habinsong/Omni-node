using System.Net;
using System.Net.Sockets;
using System.Text;

namespace OmniNode.Middleware;

public sealed class UdsCoreClient
{
    private readonly string _socketPath;
    private readonly string _authToken;

    public UdsCoreClient(string socketPath, string authToken)
    {
        if (string.IsNullOrWhiteSpace(authToken))
        {
            throw new ArgumentException("core auth token is required.", nameof(authToken));
        }

        _socketPath = socketPath;
        _authToken = authToken.Trim();
    }

    public static bool IsTcpEndpoint(string endpoint)
    {
        return Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            && uri.Scheme.Equals("tcp", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryGetTcpPort(string endpoint, out int port)
    {
        port = 0;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals("tcp", StringComparison.OrdinalIgnoreCase)
            || uri.Port <= 0)
        {
            return false;
        }

        port = uri.Port;
        return true;
    }

    public Task<string> GetMetricsAsync(CancellationToken cancellationToken)
    {
        return RequestAsync($"get_metrics {_authToken}", cancellationToken);
    }

    public Task<string> KillAsync(int pid, CancellationToken cancellationToken)
    {
        return RequestAsync($"kill {_authToken} {pid}", cancellationToken);
    }

    public async Task<string> RequestAsync(string jsonRequest, CancellationToken cancellationToken)
    {
        using var socket = CreateSocket(_socketPath, out var endpoint);

        await socket.ConnectAsync(endpoint, cancellationToken);

        var requestBytes = Encoding.UTF8.GetBytes(jsonRequest + "\n");
        await socket.SendAsync(requestBytes, SocketFlags.None, cancellationToken);

        var buffer = new byte[8192];
        var builder = new StringBuilder();

        while (true)
        {
            var read = await socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken);
            if (read <= 0)
            {
                break;
            }

            builder.Append(Encoding.UTF8.GetString(buffer, 0, read));
            if (builder.ToString().Contains('\n'))
            {
                break;
            }
        }

        return builder.ToString().Trim();
    }

    private static Socket CreateSocket(string endpointText, out EndPoint endpoint)
    {
        if (IsTcpEndpoint(endpointText))
        {
            var uri = new Uri(endpointText);
            var host = string.IsNullOrWhiteSpace(uri.Host) ? IPAddress.Loopback.ToString() : uri.Host;
            var address = IPAddress.TryParse(host, out var parsedAddress) ? parsedAddress : IPAddress.Loopback;
            endpoint = new IPEndPoint(address, uri.Port);
            return new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        }

        endpoint = new UnixDomainSocketEndPoint(endpointText);
        return new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
    }
}
