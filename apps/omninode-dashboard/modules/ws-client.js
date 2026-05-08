export const AUTH_TOKEN_KEY = "omninode_auth_token";
export const AUTH_EXPIRES_KEY = "omninode_auth_expires_utc";

function resolveStorage(storage) {
  if (storage) {
    return storage;
  }

  if (typeof window !== "undefined" && window.localStorage) {
    return window.localStorage;
  }

  return null;
}

export function getSavedAuthToken(storage) {
  try {
    const target = resolveStorage(storage);
    return target ? (target.getItem(AUTH_TOKEN_KEY) || "") : "";
  } catch (_err) {
    return "";
  }
}

export function getSavedAuthExpiry(storage) {
  try {
    const target = resolveStorage(storage);
    return target ? (target.getItem(AUTH_EXPIRES_KEY) || "") : "";
  } catch (_err) {
    return "";
  }
}

export function persistAuthSession(token, expiresAtUtc, storage) {
  try {
    const target = resolveStorage(storage);
    if (!target) {
      return;
    }

    if (token) {
      target.setItem(AUTH_TOKEN_KEY, token);
    }
    if (expiresAtUtc) {
      target.setItem(AUTH_EXPIRES_KEY, expiresAtUtc);
    }
  } catch (_err) {
  }
}

export function clearPersistedAuthSession(storage) {
  try {
    const target = resolveStorage(storage);
    if (!target) {
      return;
    }

    target.removeItem(AUTH_TOKEN_KEY);
    target.removeItem(AUTH_EXPIRES_KEY);
  } catch (_err) {
  }
}

export function buildDashboardWsUrl(locationLike) {
  const target = locationLike || (typeof window !== "undefined" ? window.location : null);
  if (!target) {
    return "ws://127.0.0.1/ws/";
  }

  const proto = target.protocol === "https:" ? "wss" : "ws";
  return `${proto}://${target.host}/ws/`;
}

export function isRemoteDashboardHost(locationLike) {
  const target = locationLike || (typeof window !== "undefined" ? window.location : null);
  const hostname = (target?.hostname || "").trim().toLowerCase();
  if (!hostname) {
    return false;
  }

  return hostname !== "localhost"
    && hostname !== "::1"
    && hostname !== "[::1]"
    && !hostname.startsWith("127.");
}

export function flushQueuedPayloads(ws, outboundQueue) {
  if (!ws || ws.readyState !== WebSocket.OPEN) {
    return false;
  }

  if (!Array.isArray(outboundQueue) || outboundQueue.length === 0) {
    return true;
  }

  const queued = outboundQueue.splice(0, outboundQueue.length);
  queued.forEach((payload) => {
    try {
      ws.send(JSON.stringify(payload));
    } catch (_err) {
    }
  });

  return true;
}

export function sendWsPayload({
  ws,
  payload,
  outboundQueue,
  queueIfClosed = false,
  silent = false,
  hasOpenedSocket = false,
  log
}) {
  if (!ws || ws.readyState !== WebSocket.OPEN) {
    if (queueIfClosed && Array.isArray(outboundQueue)) {
      outboundQueue.push(payload);
    }
    if (!silent && hasOpenedSocket && typeof log === "function") {
      log("WS 연결이 필요합니다.", "error");
    }
    return false;
  }

  ws.send(JSON.stringify(payload));
  return true;
}
