import assert from "node:assert/strict";
import { spawn, spawnSync } from "node:child_process";
import { randomBytes } from "node:crypto";
import { mkdirSync, mkdtempSync, rmSync } from "node:fs";
import net from "node:net";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const coreBinaryPath = path.join(repoRoot, "apps", "omninode-core", "omninode_core");

function findFreePort() {
  return new Promise((resolve, reject) => {
    const server = net.createServer();
    server.on("error", reject);
    server.listen(0, "127.0.0.1", () => {
      const address = server.address();
      const port = typeof address === "object" && address ? address.port : 0;
      server.close(() => resolve(port));
    });
  });
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function findNonLoopbackIPv4() {
  for (const interfaces of Object.values(os.networkInterfaces())) {
    for (const item of interfaces ?? []) {
      if (item.family === "IPv4" && !item.internal && item.address) {
        return item.address;
      }
    }
  }

  return "";
}

function repoCorePids() {
  const result = spawnSync("pgrep", ["-f", coreBinaryPath], {
    cwd: repoRoot,
    encoding: "utf8"
  });
  if (result.status !== 0 || !result.stdout.trim()) {
    return new Set();
  }

  return new Set(
    result.stdout
      .split(/\s+/)
      .map((value) => value.trim())
      .filter(Boolean)
  );
}

function killPid(pid) {
  try {
    process.kill(Number(pid), "SIGTERM");
  } catch {
    return;
  }
}

async function stopProcess(processHandle) {
  if (!processHandle || processHandle.exitCode !== null) {
    return;
  }

  processHandle.kill("SIGTERM");
  for (let i = 0; i < 20; i += 1) {
    if (processHandle.exitCode !== null) {
      return;
    }
    await sleep(100);
  }

  processHandle.kill("SIGKILL");
}

async function waitForHttpOk(url, logs) {
  let lastError = "";
  for (let i = 0; i < 200; i += 1) {
    try {
      const response = await fetch(url);
      if (response.ok) {
        return response;
      }
      lastError = `${response.status} ${response.statusText}`;
    } catch (error) {
      lastError = error.message;
    }

    await sleep(250);
  }

  throw new Error(`gateway did not become healthy: ${lastError}\n${logs()}`);
}

function rawWebSocketHandshake({ host = "127.0.0.1", port, origin }) {
  return new Promise((resolve, reject) => {
    const socket = net.createConnection({ host, port });
    let response = "";
    const timeout = setTimeout(() => {
      socket.destroy();
      reject(new Error("websocket handshake timed out"));
    }, 5000);

    socket.on("connect", () => {
      const lines = [
        "GET /ws/ HTTP/1.1",
        `Host: ${host}:${port}`,
        "Upgrade: websocket",
        "Connection: Upgrade",
        "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==",
        "Sec-WebSocket-Version: 13"
      ];
      if (origin) {
        lines.push(`Origin: ${origin}`);
      }
      socket.write(`${lines.join("\r\n")}\r\n\r\n`);
    });

    socket.on("data", (chunk) => {
      response += chunk.toString("utf8");
      if (!response.includes("\r\n\r\n")) {
        return;
      }

      clearTimeout(timeout);
      socket.destroy();
      const statusLine = response.split("\r\n", 1)[0] || "";
      const match = statusLine.match(/^HTTP\/1\.[01]\s+(\d+)/);
      resolve({
        status: match ? Number(match[1]) : 0,
        statusLine
      });
    });

    socket.on("error", (error) => {
      clearTimeout(timeout);
      reject(error);
    });
  });
}

function encodeClientFrame(opcode, payloadText = "") {
  const payload = Buffer.from(payloadText, "utf8");
  const extendedLengthBytes = payload.length <= 125 ? 0 : payload.length <= 65535 ? 2 : 8;
  const headerLength = 2 + extendedLengthBytes;
  const frame = Buffer.alloc(headerLength + 4 + payload.length);
  frame[0] = 0x80 | opcode;
  if (payload.length <= 125) {
    frame[1] = 0x80 | payload.length;
  } else if (payload.length <= 65535) {
    frame[1] = 0x80 | 126;
    frame.writeUInt16BE(payload.length, 2);
  } else {
    frame[1] = 0x80 | 127;
    frame.writeUInt32BE(0, 2);
    frame.writeUInt32BE(payload.length, 6);
  }

  const mask = randomBytes(4);
  mask.copy(frame, headerLength);
  for (let i = 0; i < payload.length; i += 1) {
    frame[headerLength + 4 + i] = payload[i] ^ mask[i % 4];
  }
  return frame;
}

function extractServerFrames(buffer) {
  const frames = [];
  let offset = 0;
  while (buffer.length - offset >= 2) {
    const first = buffer[offset];
    const second = buffer[offset + 1];
    const opcode = first & 0x0f;
    const masked = (second & 0x80) !== 0;
    let length = second & 0x7f;
    let headerLength = 2;

    if (length === 126) {
      if (buffer.length - offset < 4) {
        break;
      }
      length = buffer.readUInt16BE(offset + 2);
      headerLength = 4;
    } else if (length === 127) {
      if (buffer.length - offset < 10) {
        break;
      }
      const high = buffer.readUInt32BE(offset + 2);
      const low = buffer.readUInt32BE(offset + 6);
      length = high * 2 ** 32 + low;
      headerLength = 10;
    }

    const maskLength = masked ? 4 : 0;
    const frameLength = headerLength + maskLength + length;
    if (buffer.length - offset < frameLength) {
      break;
    }

    let payload = buffer.subarray(offset + headerLength + maskLength, offset + frameLength);
    if (masked) {
      const mask = buffer.subarray(offset + headerLength, offset + headerLength + 4);
      payload = Buffer.from(payload.map((value, index) => value ^ mask[index % 4]));
    }

    frames.push({ opcode, payload });
    offset += frameLength;
  }

  return {
    frames,
    remaining: buffer.subarray(offset)
  };
}

function openRawWebSocketSession({ host = "127.0.0.1", port, origin }) {
  return new Promise((resolve, reject) => {
    const socket = net.createConnection({ host, port });
    let handshakeBuffer = Buffer.alloc(0);
    let frameBuffer = Buffer.alloc(0);
    let handshakeDone = false;
    let settled = false;
    const messages = [];
    const waiters = [];
    const timeout = setTimeout(() => {
      socket.destroy();
      reject(new Error("websocket session handshake timed out"));
    }, 5000);

    const rejectWaiters = (error) => {
      while (waiters.length > 0) {
        const waiter = waiters.shift();
        clearTimeout(waiter.timer);
        waiter.reject(error);
      }
    };

    const dispatchMessage = (message) => {
      messages.push(message);
      for (let i = 0; i < waiters.length; i += 1) {
        const waiter = waiters[i];
        if (!waiter.predicate(message)) {
          continue;
        }

        waiters.splice(i, 1);
        clearTimeout(waiter.timer);
        waiter.resolve(message);
        return;
      }
    };

    const handleFrameBytes = (chunk) => {
      frameBuffer = Buffer.concat([frameBuffer, chunk]);
      const extracted = extractServerFrames(frameBuffer);
      frameBuffer = extracted.remaining;
      for (const frame of extracted.frames) {
        if (frame.opcode === 0x1) {
          const text = frame.payload.toString("utf8");
          try {
            dispatchMessage(JSON.parse(text));
          } catch {
            dispatchMessage({ type: "__raw_text__", text });
          }
        }
      }
    };

    const session = {
      sendJson(message) {
        socket.write(encodeClientFrame(0x1, JSON.stringify(message)));
      },
      waitForJson(predicate, description, timeoutMs = 5000) {
        for (const message of messages) {
          if (predicate(message)) {
            return Promise.resolve(message);
          }
        }

        return new Promise((resolve, rejectWaiter) => {
          const waiter = {
            predicate,
            resolve,
            reject: rejectWaiter,
            timer: setTimeout(() => {
              const index = waiters.indexOf(waiter);
              if (index >= 0) {
                waiters.splice(index, 1);
              }
              rejectWaiter(new Error(`${description} timed out; received=${JSON.stringify(messages)}`));
            }, timeoutMs)
          };
          waiters.push(waiter);
        });
      },
      close() {
        socket.write(encodeClientFrame(0x8));
        socket.end();
      }
    };

    socket.on("connect", () => {
      const lines = [
        "GET /ws/ HTTP/1.1",
        `Host: ${host}:${port}`,
        "Upgrade: websocket",
        "Connection: Upgrade",
        `Sec-WebSocket-Key: ${randomBytes(16).toString("base64")}`,
        "Sec-WebSocket-Version: 13"
      ];
      if (origin) {
        lines.push(`Origin: ${origin}`);
      }
      socket.write(`${lines.join("\r\n")}\r\n\r\n`);
    });

    socket.on("data", (chunk) => {
      if (handshakeDone) {
        handleFrameBytes(chunk);
        return;
      }

      handshakeBuffer = Buffer.concat([handshakeBuffer, chunk]);
      const headerEnd = handshakeBuffer.indexOf("\r\n\r\n");
      if (headerEnd < 0) {
        return;
      }

      const headerText = handshakeBuffer.subarray(0, headerEnd).toString("utf8");
      const statusLine = headerText.split("\r\n", 1)[0] || "";
      const match = statusLine.match(/^HTTP\/1\.[01]\s+(\d+)/);
      const status = match ? Number(match[1]) : 0;
      if (status !== 101) {
        clearTimeout(timeout);
        socket.destroy();
        reject(new Error(`websocket session expected 101 but received ${statusLine}`));
        return;
      }

      handshakeDone = true;
      settled = true;
      clearTimeout(timeout);
      resolve(session);
      const remaining = handshakeBuffer.subarray(headerEnd + 4);
      if (remaining.length > 0) {
        handleFrameBytes(remaining);
      }
    });

    socket.on("error", (error) => {
      clearTimeout(timeout);
      if (!settled) {
        reject(error);
        return;
      }

      rejectWaiters(error);
    });

    socket.on("close", () => {
      rejectWaiters(new Error("websocket session closed"));
    });
  });
}

function waitForPong(url) {
  return new Promise((resolve, reject) => {
    const socket = new WebSocket(url);
    const timeout = setTimeout(() => {
      socket.close();
      reject(new Error("websocket pong timed out"));
    }, 5000);

    socket.addEventListener("open", () => {
      socket.send(JSON.stringify({ type: "ping" }));
    });

    socket.addEventListener("message", (event) => {
      const text = typeof event.data === "string" ? event.data : event.data.toString();
      let message;
      try {
        message = JSON.parse(text);
      } catch {
        return;
      }

      if (message.type === "pong") {
        clearTimeout(timeout);
        socket.close();
        resolve(message);
      }
    });

    socket.addEventListener("error", () => {
      clearTimeout(timeout);
      reject(new Error("websocket connection failed"));
    });
  });
}

async function main() {
  const port = await findFreePort();
  const externalHost = findNonLoopbackIPv4();
  const runtimeRoot = mkdtempSync(path.join(os.tmpdir(), "omninode-gateway-runtime-"));
  const homeDir = path.join(runtimeRoot, "home");
  const workspaceRoot = path.join(runtimeRoot, "workspace", "coding");
  mkdirSync(homeDir, { recursive: true });
  mkdirSync(workspaceRoot, { recursive: true });

  const beforeCorePids = repoCorePids();
  const logs = [];
  const middleware = spawn(
    "dotnet",
    ["run", "--project", "apps/omninode-middleware/OmniNode.Middleware.csproj"],
    {
      cwd: repoRoot,
      env: {
        ...process.env,
        HOME: homeDir,
        OMNINODE_WS_PORT: String(port),
        OMNINODE_WORKSPACE_ROOT: workspaceRoot,
        OMNINODE_CORE_SOCKET_PATH: path.join(runtimeRoot, "core.sock"),
        OMNINODE_DASHBOARD_ACCESS_STATE_PATH: path.join(runtimeRoot, "dashboard_access.json"),
        OMNINODE_ENABLE_LOCAL_OTP_FALLBACK: "1",
        OMNINODE_GATEWAY_STARTUP_PROBE: "0",
        OMNINODE_EXTERNAL_DASHBOARD: externalHost ? "1" : "0",
        OMNINODE_SKIP_CORE_BOOTSTRAP: "1",
        OMNINODE_SKIP_MEMORY_INDEX_BOOTSTRAP: "1",
        DOTNET_CLI_TELEMETRY_OPTOUT: "1",
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE: "1",
        DOTNET_CLI_HOME: process.env.HOME || homeDir
      },
      stdio: ["ignore", "pipe", "pipe"]
    }
  );

  middleware.stdout.on("data", (chunk) => logs.push(chunk.toString("utf8")));
  middleware.stderr.on("data", (chunk) => logs.push(chunk.toString("utf8")));

  try {
    const baseUrl = `http://127.0.0.1:${port}`;
    await waitForHttpOk(`${baseUrl}/healthz`, () => logs.join(""));

    const noOrigin = await rawWebSocketHandshake({ port });
    assert.equal(noOrigin.status, 101, "local websocket without Origin should be accepted");

    const badOrigin = await rawWebSocketHandshake({
      port,
      origin: `http://evil.example:${port}`
    });
    assert.equal(badOrigin.status, 403, "websocket with mismatched Origin should be rejected");

    const localSession = await openRawWebSocketSession({ port });
    const authRequired = await localSession.waitForJson(
      (message) => message.type === "auth_required",
      "local auth_required"
    );
    assert.equal(authRequired.remoteDashboardClient, false, "local websocket should start in OTP-pending mode");
    localSession.sendJson({ type: "llm_chat_single", input: "should require auth" });
    const unauthorized = await localSession.waitForJson(
      (message) => message.type === "error" && message.message === "unauthorized",
      "local unauthorized protected message"
    );
    assert.equal(unauthorized.message, "unauthorized", "local protected messages should require auth");
    localSession.close();

    const remoteChecks = [];
    if (externalHost) {
      const remoteNoOrigin = await rawWebSocketHandshake({ host: externalHost, port });
      assert.equal(remoteNoOrigin.status, 403, "remote websocket without Origin should be rejected");

      const remoteOrigin = `http://${externalHost}:${port}`;
      const remoteSession = await openRawWebSocketSession({ host: externalHost, port, origin: remoteOrigin });
      const remoteAuth = await remoteSession.waitForJson(
        (message) => message.type === "auth_result",
        "remote auth_result"
      );
      assert.equal(remoteAuth.ok, true, "remote dashboard client should be auto-approved");
      assert.equal(remoteAuth.remoteDashboardClient, true, "remote dashboard client should be marked remote");
      assert.equal(remoteAuth.remoteLimited, true, "remote dashboard client should enter limited mode");

      const remoteSettings = await remoteSession.waitForJson(
        (message) => message.type === "settings_state",
        "remote settings_state"
      );
      assert.equal(remoteSettings.remoteDashboardClient, true, "remote settings should be marked remote");

      remoteSession.sendJson({ type: "list_conversations", scope: "chat", mode: "single" });
      const conversations = await remoteSession.waitForJson(
        (message) => message.type === "conversations",
        "remote read-only conversations"
      );
      assert.equal(conversations.scope, "chat", "remote read-only conversation list should preserve scope");
      assert.equal(conversations.mode, "single", "remote read-only conversation list should preserve mode");
      assert.ok(Array.isArray(conversations.items), "remote read-only conversation list should return items");

      remoteSession.sendJson({ type: "llm_chat_single", input: "remote execution should be blocked" });
      const blocked = await remoteSession.waitForJson(
        (message) => message.type === "error" && message.message === "forbidden_remote_limited_action",
        "remote limited execution block"
      );
      assert.equal(
        blocked.message,
        "forbidden_remote_limited_action",
        "remote limited mode should block execution messages before dispatch"
      );
      remoteSession.close();

      remoteChecks.push(
        "websocket_no_origin_remote_reject",
        "remote_limited_auto_auth",
        "remote_limited_read_only_allow",
        "remote_limited_execution_block"
      );
    } else {
      remoteChecks.push("remote_limited_runtime_skipped_no_non_loopback_ipv4");
    }

    await waitForPong(`ws://127.0.0.1:${port}/ws/`);
    const ready = await fetch(`${baseUrl}/readyz`);
    assert.equal(ready.status, 200, "readyz should pass after websocket ping/pong");

    const firstIndex = await fetch(`${baseUrl}/`);
    assert.equal(firstIndex.status, 200, "dashboard index should load");
    const etag = firstIndex.headers.get("etag");
    assert.ok(etag, "dashboard index should expose ETag");
    assert.ok(firstIndex.headers.get("last-modified"), "dashboard index should expose Last-Modified");

    const cachedIndex = await fetch(`${baseUrl}/`, {
      headers: {
        "If-None-Match": etag
      }
    });
    assert.equal(cachedIndex.status, 304, "dashboard index should support conditional 304");

    console.log(JSON.stringify({
      ok: true,
      port,
      checks: [
        "healthz",
        "websocket_no_origin_local_accept",
        "websocket_bad_origin_reject",
        "websocket_local_unauthorized_protected_reject",
        ...remoteChecks,
        "readyz_after_ping",
        "static_index_etag_304"
      ]
    }, null, 2));
  } finally {
    await stopProcess(middleware);
    const afterCorePids = repoCorePids();
    for (const pid of afterCorePids) {
      if (!beforeCorePids.has(pid)) {
        killPid(pid);
      }
    }
    rmSync(runtimeRoot, { recursive: true, force: true });
  }
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
