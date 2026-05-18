import assert from "node:assert/strict";
import { spawn, spawnSync } from "node:child_process";
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

function rawWebSocketHandshake({ port, origin }) {
  return new Promise((resolve, reject) => {
    const socket = net.createConnection({ host: "127.0.0.1", port });
    let response = "";
    const timeout = setTimeout(() => {
      socket.destroy();
      reject(new Error("websocket handshake timed out"));
    }, 5000);

    socket.on("connect", () => {
      const lines = [
        "GET /ws/ HTTP/1.1",
        `Host: 127.0.0.1:${port}`,
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
        OMNINODE_EXTERNAL_DASHBOARD: "0",
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
