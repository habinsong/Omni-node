using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace OmniNode.Middleware;

public sealed record BrowserToolTab(
    string TargetId,
    string Url,
    string Title,
    bool Active,
    long UpdatedAtMs
);

public sealed record BrowserToolResult(
    bool Ok,
    string Action,
    string Profile,
    bool Disabled,
    string Adapter,
    bool Running,
    string? ActiveTargetId,
    string? ActiveUrl,
    IReadOnlyList<BrowserToolTab> Tabs,
    string? Error
);

public sealed class BrowserTool
{
    private const int DefaultTabLimit = 20;
    private const int MaxTabLimit = 100;
    private const string DefaultProfile = "default";
    private const int PlaywrightHelperRequestTimeoutMs = 15000;
    private const string PlaywrightHelperScript = """
const readline = require("readline");

let chromium;
try {
  chromium = require("playwright").chromium;
} catch (error) {
  console.error(`playwright module could not be loaded: ${error && error.message ? error.message : String(error)}`);
  process.exit(2);
}

const headlessValue = String(process.env.OMNINODE_BROWSER_HEADLESS || "true").toLowerCase();
const headless = !(headlessValue === "0" || headlessValue === "false" || headlessValue === "no" || headlessValue === "off");
let browser = null;
let context = null;
let activePage = null;
let nextPageId = 1;
const pageIds = new WeakMap();

function nowMs() {
  return Date.now();
}

function pageId(page) {
  if (!pageIds.has(page)) {
    pageIds.set(page, `tab-${String(nextPageId++).padStart(3, "0")}`);
  }

  return pageIds.get(page);
}

function isRunning() {
  return !!browser && browser.isConnected();
}

function openPages() {
  if (!context) {
    return [];
  }

  return context.pages().filter(page => !page.isClosed());
}

function titleFromUrl(url) {
  if (!url || url === "about:blank") {
    return "Blank";
  }

  try {
    const parsed = new URL(url);
    return parsed.host || parsed.href;
  } catch {
    return url;
  }
}

async function ensureBrowser() {
  if (isRunning() && context) {
    return;
  }

  browser = await chromium.launch({ headless });
  context = await browser.newContext();
  context.on("page", page => {
    pageId(page);
    activePage = page;
    page.on("close", () => {
      if (activePage === page) {
        activePage = null;
      }
    });
  });

  activePage = await context.newPage();
  pageId(activePage);
}

function resolvePage(targetId) {
  if (!targetId) {
    return null;
  }

  const normalizedTargetId = String(targetId).toLowerCase();
  return openPages().find(page => pageId(page).toLowerCase() === normalizedTargetId) || null;
}

async function pageInfo(page) {
  let url = "about:blank";
  let title = "";
  try {
    url = page.url() || "about:blank";
  } catch {
  }

  try {
    title = await page.title();
  } catch {
  }

  return {
    targetId: pageId(page),
    url,
    title: title || titleFromUrl(url),
    active: page === activePage,
    updatedAtMs: nowMs()
  };
}

async function snapshot(action, profile, ok, error, limit) {
  const pages = openPages();
  if (!activePage || activePage.isClosed()) {
    activePage = pages.length > 0 ? pages[pages.length - 1] : null;
  }

  const tabs = await Promise.all(pages.map(pageInfo));
  tabs.sort((a, b) => {
    if (a.active !== b.active) {
      return a.active ? -1 : 1;
    }

    return b.updatedAtMs - a.updatedAtMs;
  });

  const activeUrl = activePage && !activePage.isClosed() ? activePage.url() || "about:blank" : null;
  return {
    ok,
    action,
    profile,
    disabled: false,
    adapter: "playwright",
    running: isRunning(),
    activeTargetId: activePage && !activePage.isClosed() ? pageId(activePage) : null,
    activeUrl,
    tabs: tabs.slice(0, limit),
    error
  };
}

async function navigatePage(page, url) {
  await page.bringToFront().catch(() => {});
  activePage = page;
  await page.goto(url, { waitUntil: "domcontentloaded", timeout: 15000 });
}

async function handleRequest(request) {
  const action = String(request.action || "").toLowerCase();
  const profile = String(request.profile || "default");
  const limit = Math.max(1, Math.min(Number(request.limit) || 20, 100));
  const supportedActions = new Set(["status", "start", "stop", "tabs", "navigate", "open", "focus", "close"]);

  if (!supportedActions.has(action)) {
    return {
      ok: false,
      action,
      profile,
      disabled: false,
      adapter: "playwright",
      running: isRunning(),
      activeTargetId: activePage && !activePage.isClosed() ? pageId(activePage) : null,
      activeUrl: activePage && !activePage.isClosed() ? activePage.url() || "about:blank" : null,
      tabs: [],
      error: `unsupported browser action: ${action}`
    };
  }

  if (action === "status" || action === "tabs") {
    return snapshot(action, profile, true, null, limit);
  }

  if (action === "stop") {
    if (browser) {
      await browser.close().catch(() => {});
    }

    browser = null;
    context = null;
    activePage = null;
    return {
      ok: true,
      action,
      profile,
      disabled: false,
      adapter: "playwright",
      running: false,
      activeTargetId: null,
      activeUrl: null,
      tabs: [],
      error: null
    };
  }

  await ensureBrowser();

  if (action === "start") {
    return snapshot(action, profile, true, null, limit);
  }

  if (action === "open") {
    const page = await context.newPage();
    pageId(page);
    await navigatePage(page, request.url);
    return snapshot(action, profile, true, null, limit);
  }

  if (action === "navigate") {
    let page = resolvePage(request.targetId) || activePage || openPages()[0];
    if (!page || page.isClosed()) {
      page = await context.newPage();
      pageId(page);
    }

    await navigatePage(page, request.url);
    return snapshot(action, profile, true, null, limit);
  }

  if (action === "focus") {
    const page = resolvePage(request.targetId);
    if (!page) {
      return snapshot(action, profile, false, "target tab not found", limit);
    }

    activePage = page;
    await page.bringToFront().catch(() => {});
    return snapshot(action, profile, true, null, limit);
  }

  const page = resolvePage(request.targetId) || activePage || openPages()[openPages().length - 1];
  if (!page || page.isClosed()) {
    return snapshot(action, profile, false, "target tab not found", limit);
  }

  await page.close().catch(() => {});
  const remaining = openPages();
  activePage = remaining.length > 0 ? remaining[remaining.length - 1] : null;
  return snapshot(action, profile, true, null, limit);
}

const rl = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
rl.on("line", async line => {
  let request;
  try {
    request = JSON.parse(line);
  } catch (error) {
    process.stdout.write(JSON.stringify({
      ok: false,
      action: "unknown",
      profile: "default",
      disabled: false,
      adapter: "playwright",
      running: isRunning(),
      activeTargetId: null,
      activeUrl: null,
      tabs: [],
      error: `invalid request json: ${error && error.message ? error.message : String(error)}`
    }) + "\n");
    return;
  }

  try {
    const response = await handleRequest(request);
    const payload = JSON.stringify(response) + "\n";
    if (response.action === "stop") {
      process.stdout.write(payload, () => process.exit(0));
      return;
    }

    process.stdout.write(payload);
  } catch (error) {
    process.stdout.write(JSON.stringify({
      ok: false,
      action: String(request.action || "unknown").toLowerCase(),
      profile: String(request.profile || "default"),
      disabled: false,
      adapter: "playwright",
      running: isRunning(),
      activeTargetId: activePage && !activePage.isClosed() ? pageId(activePage) : null,
      activeUrl: activePage && !activePage.isClosed() ? activePage.url() || "about:blank" : null,
      tabs: [],
      error: error && error.message ? error.message : String(error)
    }) + "\n");
  }
});

process.on("SIGTERM", async () => {
  if (browser) {
    await browser.close().catch(() => {});
  }

  process.exit(0);
});
""";

    private readonly string _mode;
    private readonly bool _headless;
    private readonly string _projectRoot;
    private readonly object _lock = new();
    private readonly Dictionary<string, BrowserProfileState> _profiles = new(StringComparer.OrdinalIgnoreCase);

    public BrowserTool(AppConfig config)
    {
        _mode = ResolveMode(Environment.GetEnvironmentVariable("OMNINODE_BROWSER_TOOL_MODE"));
        _headless = ResolveBool(Environment.GetEnvironmentVariable("OMNINODE_BROWSER_HEADLESS"), fallback: true);
        _projectRoot = ResolveProjectRoot(config);
    }

    public BrowserToolResult Execute(
        string? action,
        string? targetUrl = null,
        string? profile = null,
        string? targetId = null,
        int? limit = null
    )
    {
        var normalizedAction = NormalizeToken(action);
        var resolvedProfile = ResolveProfile(profile);
        var resolvedTargetId = NormalizeToken(targetId);
        var resolvedLimit = ResolveLimit(limit);

        if (string.IsNullOrWhiteSpace(normalizedAction))
        {
            return ErrorResult("action is required", "unknown", resolvedProfile, null, resolvedLimit);
        }

        if (string.Equals(_mode, "off", StringComparison.Ordinal))
        {
            return new BrowserToolResult(
                Ok: false,
                Action: normalizedAction,
                Profile: resolvedProfile,
                Disabled: true,
                Adapter: "disabled",
                Running: false,
                ActiveTargetId: null,
                ActiveUrl: null,
                Tabs: Array.Empty<BrowserToolTab>(),
                Error: "browser tool disabled (OMNINODE_BROWSER_TOOL_MODE=off)"
            );
        }

        lock (_lock)
        {
            var state = GetOrCreateState(resolvedProfile);
            if (string.Equals(_mode, "playwright", StringComparison.Ordinal))
            {
                return ExecutePlaywrightAction(
                    state,
                    normalizedAction,
                    targetUrl,
                    resolvedProfile,
                    resolvedTargetId,
                    resolvedLimit
                );
            }

            if (string.Equals(_mode, "auto", StringComparison.Ordinal))
            {
                return ExecuteAutoAction(
                    state,
                    normalizedAction,
                    targetUrl,
                    resolvedProfile,
                    resolvedTargetId,
                    resolvedLimit
                );
            }

            return ExecuteStubAction(
                state,
                normalizedAction,
                targetUrl,
                resolvedProfile,
                resolvedTargetId,
                resolvedLimit
            );
        }
    }

    private BrowserToolResult ExecuteAutoAction(
        BrowserProfileState state,
        string action,
        string? targetUrl,
        string profile,
        string? targetId,
        int limit
    )
    {
        if ((action == "status" || action == "tabs") && !IsPlaywrightHelperAlive(state))
        {
            return ExecuteStubAction(state, action, targetUrl, profile, targetId, limit);
        }

        var result = ExecutePlaywrightAction(state, action, targetUrl, profile, targetId, limit);
        if (result.Ok || !IsPlaywrightUnavailable(result.Error))
        {
            return result;
        }

        DisposePlaywrightHelper(state);
        return ExecuteStubAction(state, action, targetUrl, profile, targetId, limit);
    }

    private BrowserToolResult ExecuteStubAction(
        BrowserProfileState state,
        string action,
        string? targetUrl,
        string profile,
        string? targetId,
        int limit
    )
    {
        switch (action)
        {
            case "status":
            case "tabs":
                return BuildSnapshot(state, action, profile, ok: true, error: null, limit);
            case "start":
                state.Running = true;
                EnsureDefaultTab(state);
                state.UpdatedAtMs = UtcNowMs();
                return BuildSnapshot(state, action, profile, ok: true, error: null, limit);
            case "stop":
                state.Running = false;
                state.UpdatedAtMs = UtcNowMs();
                return BuildSnapshot(state, action, profile, ok: true, error: null, limit);
            case "navigate":
            {
                var normalizedUrl = NormalizeUrl(targetUrl);
                if (normalizedUrl is null)
                {
                    return ErrorResult(
                        "url is required and must use http/https or about:blank",
                        action,
                        profile,
                        state,
                        limit
                    );
                }

                state.Running = true;
                var targetTab = ResolveNavigationTab(state, targetId);
                targetTab.Url = normalizedUrl;
                targetTab.Title = BuildTitle(normalizedUrl);
                targetTab.UpdatedAtMs = UtcNowMs();
                state.ActiveTargetId = targetTab.TargetId;
                state.UpdatedAtMs = targetTab.UpdatedAtMs;
                return BuildSnapshot(state, action, profile, ok: true, error: null, limit);
            }
            case "open":
            {
                var normalizedUrl = NormalizeUrl(targetUrl);
                if (normalizedUrl is null)
                {
                    return ErrorResult(
                        "url is required and must use http/https or about:blank",
                        action,
                        profile,
                        state,
                        limit
                    );
                }

                state.Running = true;
                var opened = CreateTab(state, normalizedUrl);
                state.ActiveTargetId = opened.TargetId;
                state.UpdatedAtMs = opened.UpdatedAtMs;
                return BuildSnapshot(state, action, profile, ok: true, error: null, limit);
            }
            case "focus":
            {
                if (string.IsNullOrWhiteSpace(targetId))
                {
                    return ErrorResult("targetId is required for focus", action, profile, state, limit);
                }

                var target = state.Tabs.FirstOrDefault(x =>
                    x.TargetId.Equals(targetId, StringComparison.OrdinalIgnoreCase));
                if (target is null)
                {
                    return ErrorResult("target tab not found", action, profile, state, limit);
                }

                state.ActiveTargetId = target.TargetId;
                target.UpdatedAtMs = UtcNowMs();
                state.UpdatedAtMs = target.UpdatedAtMs;
                return BuildSnapshot(state, action, profile, ok: true, error: null, limit);
            }
            case "close":
            {
                var target = ResolveClosableTab(state, targetId);
                if (target is null)
                {
                    return ErrorResult("target tab not found", action, profile, state, limit);
                }

                _ = state.Tabs.Remove(target);
                if (state.Tabs.Count == 0)
                {
                    state.ActiveTargetId = null;
                }
                else if (string.Equals(state.ActiveTargetId, target.TargetId, StringComparison.OrdinalIgnoreCase))
                {
                    state.ActiveTargetId = state.Tabs[^1].TargetId;
                }

                state.UpdatedAtMs = UtcNowMs();
                return BuildSnapshot(state, action, profile, ok: true, error: null, limit);
            }
            default:
                return ErrorResult(
                    $"unsupported browser action: {action}",
                    action,
                    profile,
                    state,
                    limit
                );
        }
    }

    private BrowserToolResult ErrorResult(
        string error,
        string action,
        string profile,
        BrowserProfileState? state,
        int limit
    )
    {
        if (state is null)
        {
            return new BrowserToolResult(
                Ok: false,
                Action: action,
                Profile: profile,
                Disabled: false,
                Adapter: StubAdapter,
                Running: false,
                ActiveTargetId: null,
                ActiveUrl: null,
                Tabs: Array.Empty<BrowserToolTab>(),
                Error: error
            );
        }

        return BuildSnapshot(state, action, profile, ok: false, error, limit);
    }

    private BrowserToolResult BuildSnapshot(
        BrowserProfileState state,
        string action,
        string profile,
        bool ok,
        string? error,
        int limit
    )
    {
        var mappedTabs = state.Tabs
            .OrderByDescending(x => x.TargetId.Equals(state.ActiveTargetId, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(x => x.UpdatedAtMs)
            .Take(limit)
            .Select(x => new BrowserToolTab(
                TargetId: x.TargetId,
                Url: x.Url,
                Title: x.Title,
                Active: x.TargetId.Equals(state.ActiveTargetId, StringComparison.OrdinalIgnoreCase),
                UpdatedAtMs: x.UpdatedAtMs
            ))
            .ToArray();

        var activeUrl = state.Tabs
            .FirstOrDefault(x => x.TargetId.Equals(state.ActiveTargetId, StringComparison.OrdinalIgnoreCase))
            ?.Url;

        return new BrowserToolResult(
            Ok: ok,
            Action: action,
            Profile: profile,
            Disabled: false,
            Adapter: StubAdapter,
            Running: state.Running,
            ActiveTargetId: state.ActiveTargetId,
            ActiveUrl: activeUrl,
            Tabs: mappedTabs,
            Error: error
        );
    }

    private BrowserProfileState GetOrCreateState(string profile)
    {
        if (_profiles.TryGetValue(profile, out var existing))
        {
            return existing;
        }

        var created = new BrowserProfileState
        {
            Running = false,
            UpdatedAtMs = UtcNowMs(),
            NextTabNumber = 1
        };
        _profiles[profile] = created;
        return created;
    }

    private static BrowserTabState ResolveNavigationTab(BrowserProfileState state, string? targetId)
    {
        if (!string.IsNullOrWhiteSpace(targetId))
        {
            var requested = state.Tabs.FirstOrDefault(x => x.TargetId.Equals(targetId, StringComparison.OrdinalIgnoreCase));
            if (requested is not null)
            {
                return requested;
            }
        }

        if (!string.IsNullOrWhiteSpace(state.ActiveTargetId))
        {
            var active = state.Tabs.FirstOrDefault(x => x.TargetId.Equals(state.ActiveTargetId, StringComparison.OrdinalIgnoreCase));
            if (active is not null)
            {
                return active;
            }
        }

        EnsureDefaultTab(state);
        return state.Tabs[^1];
    }

    private static BrowserTabState? ResolveClosableTab(BrowserProfileState state, string? targetId)
    {
        if (!string.IsNullOrWhiteSpace(targetId))
        {
            return state.Tabs.FirstOrDefault(x => x.TargetId.Equals(targetId, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(state.ActiveTargetId))
        {
            var active = state.Tabs.FirstOrDefault(x => x.TargetId.Equals(state.ActiveTargetId, StringComparison.OrdinalIgnoreCase));
            if (active is not null)
            {
                return active;
            }
        }

        return state.Tabs.Count > 0 ? state.Tabs[^1] : null;
    }

    private static void EnsureDefaultTab(BrowserProfileState state)
    {
        if (state.Tabs.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(state.ActiveTargetId))
            {
                state.ActiveTargetId = state.Tabs[^1].TargetId;
            }

            return;
        }

        var created = CreateTab(state, "about:blank");
        state.ActiveTargetId = created.TargetId;
    }

    private static BrowserTabState CreateTab(BrowserProfileState state, string url)
    {
        var now = UtcNowMs();
        var tab = new BrowserTabState
        {
            TargetId = $"tab-{state.NextTabNumber:D3}",
            Url = url,
            Title = BuildTitle(url),
            UpdatedAtMs = now
        };
        state.NextTabNumber++;
        state.Tabs.Add(tab);
        return tab;
    }

    private static string BuildTitle(string url)
    {
        if (string.Equals(url, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return "Blank";
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        var host = uri.Host.Trim();
        if (!string.IsNullOrWhiteSpace(host))
        {
            return host;
        }

        return uri.AbsoluteUri;
    }

    private static string? NormalizeUrl(string? url)
    {
        var candidate = (url ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        if (string.Equals(candidate, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return "about:blank";
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var parsed))
        {
            return null;
        }

        var scheme = parsed.Scheme;
        if (!scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
            && !scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return parsed.AbsoluteUri;
    }

    private static int ResolveLimit(int? limit)
    {
        if (!limit.HasValue || limit.Value <= 0)
        {
            return DefaultTabLimit;
        }

        return Math.Clamp(limit.Value, 1, MaxTabLimit);
    }

    private static string ResolveProfile(string? profile)
    {
        var normalized = NormalizeToken(profile);
        return string.IsNullOrWhiteSpace(normalized) ? DefaultProfile : normalized;
    }

    private static string NormalizeToken(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();
    }

    private static string ResolveMode(string? mode)
    {
        var normalized = NormalizeToken(mode);
        return normalized switch
        {
            "auto" => "auto",
            "off" => "off",
            "playwright" => "playwright",
            "stub" => "stub",
            _ => "auto"
        };
    }

    private string StubAdapter => string.Equals(_mode, "auto", StringComparison.Ordinal) ? "stub" : _mode;

    private BrowserToolResult ExecutePlaywrightAction(
        BrowserProfileState state,
        string action,
        string? targetUrl,
        string profile,
        string? targetId,
        int limit
    )
    {
        try
        {
            return ExecutePlaywrightHelperAction(state, action, targetUrl, profile, targetId, limit);
        }
        catch (Exception ex)
        {
            return BuildPlaywrightHelperErrorResult(state, action, profile, TrimError(ex.Message));
        }
    }

    private BrowserToolResult ExecutePlaywrightHelperAction(
        BrowserProfileState state,
        string action,
        string? targetUrl,
        string profile,
        string? targetId,
        int limit
    )
    {
        if (action is not ("status" or "start" or "stop" or "tabs" or "navigate" or "open" or "focus" or "close"))
        {
            return BuildPlaywrightHelperErrorResult(
                state,
                action,
                profile,
                $"unsupported browser action: {action}"
            );
        }

        var normalizedUrl = action is "open" or "navigate"
            ? NormalizeUrl(targetUrl)
            : targetUrl;
        if (action is "open" or "navigate" && normalizedUrl is null)
        {
            return new BrowserToolResult(
                false,
                action,
                profile,
                false,
                _mode,
                IsPlaywrightHelperAlive(state),
                null,
                null,
                Array.Empty<BrowserToolTab>(),
                "url is required and must use http/https or about:blank"
            );
        }

        if (action == "focus" && string.IsNullOrWhiteSpace(targetId))
        {
            return BuildPlaywrightHelperErrorResult(state, action, profile, "targetId is required for focus");
        }

        if (action == "stop" && !IsPlaywrightHelperAlive(state))
        {
            state.Running = false;
            state.ActiveTargetId = null;
            return new BrowserToolResult(
                true,
                action,
                profile,
                false,
                _mode,
                false,
                null,
                null,
                Array.Empty<BrowserToolTab>(),
                null
            );
        }

        if (action == "status" || action == "tabs")
        {
            if (!IsPlaywrightHelperAlive(state))
            {
                return new BrowserToolResult(
                    true,
                    action,
                    profile,
                    false,
                    _mode,
                    false,
                    null,
                    null,
                    Array.Empty<BrowserToolTab>(),
                    null
                );
            }
        }
        else
        {
            EnsurePlaywrightHelperStarted(state);
        }

        if (!IsPlaywrightHelperAlive(state))
        {
            return new BrowserToolResult(
                action == "stop",
                action,
                profile,
                false,
                _mode,
                false,
                null,
                null,
                Array.Empty<BrowserToolTab>(),
                action == "stop" ? null : "playwright helper is not running"
            );
        }

        var response = SendPlaywrightHelperRequest(
            state,
            BuildPlaywrightHelperRequestJson(action, profile, normalizedUrl, targetId, limit)
        );
        state.UpdatedAtMs = UtcNowMs();
        var result = ParsePlaywrightHelperResult(response, action, profile, limit);
        if (action == "stop")
        {
            DisposePlaywrightHelper(state);
        }

        return result;
    }

    private static string BuildPlaywrightHelperRequestJson(
        string action,
        string profile,
        string? url,
        string? targetId,
        int limit
    )
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("action", action);
            writer.WriteString("profile", profile);
            if (url is null)
            {
                writer.WriteNull("url");
            }
            else
            {
                writer.WriteString("url", url);
            }

            if (targetId is null)
            {
                writer.WriteNull("targetId");
            }
            else
            {
                writer.WriteString("targetId", targetId);
            }

            writer.WriteNumber("limit", limit);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private void EnsurePlaywrightHelperStarted(BrowserProfileState state)
    {
        if (IsPlaywrightHelperAlive(state))
        {
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "node",
            WorkingDirectory = Directory.Exists(_projectRoot) ? _projectRoot : Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add(PlaywrightHelperScript);
        startInfo.Environment["OMNINODE_BROWSER_HEADLESS"] = _headless ? "true" : "false";

        var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException("node executable was not found.", ex);
        }

        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                state.LastHelperError = TrimError(args.Data);
            }
        };
        process.BeginErrorReadLine();
        state.HelperProcess = process;
        state.LastHelperError = null;
        state.Running = true;
    }

    private static bool IsPlaywrightHelperAlive(BrowserProfileState state)
    {
        try
        {
            return state.HelperProcess != null && !state.HelperProcess.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static void DisposePlaywrightHelper(BrowserProfileState state)
    {
        if (state.HelperProcess == null)
        {
            return;
        }

        try
        {
            if (!state.HelperProcess.HasExited)
            {
                state.HelperProcess.WaitForExit(1500);
            }
        }
        catch
        {
        }

        try
        {
            if (!state.HelperProcess.HasExited)
            {
                state.HelperProcess.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }

        state.HelperProcess.Dispose();
        state.HelperProcess = null;
        state.LastHelperError = null;
        state.Running = false;
        state.ActiveTargetId = null;
    }

    private static string SendPlaywrightHelperRequest(BrowserProfileState state, string json)
    {
        var process = state.HelperProcess ?? throw new InvalidOperationException("playwright helper is not running");
        if (process.HasExited)
        {
            throw new InvalidOperationException(state.LastHelperError ?? $"playwright helper exited with code {process.ExitCode}");
        }

        process.StandardInput.WriteLine(json);
        process.StandardInput.Flush();
        var readTask = process.StandardOutput.ReadLineAsync();
        if (!readTask.Wait(PlaywrightHelperRequestTimeoutMs))
        {
            throw new TimeoutException(state.LastHelperError ?? "playwright helper request timed out");
        }

        var line = readTask.Result;
        if (string.IsNullOrWhiteSpace(line))
        {
            throw new InvalidOperationException(state.LastHelperError ?? "playwright helper returned an empty response");
        }

        return line;
    }

    private BrowserToolResult BuildPlaywrightHelperErrorResult(
        BrowserProfileState state,
        string action,
        string profile,
        string error
    )
    {
        var running = IsPlaywrightHelperAlive(state);
        state.Running = running;
        if (!running)
        {
            state.ActiveTargetId = null;
        }

        return new BrowserToolResult(
            Ok: false,
            Action: action,
            Profile: profile,
            Disabled: false,
            Adapter: "playwright",
            Running: running,
            ActiveTargetId: running ? state.ActiveTargetId : null,
            ActiveUrl: null,
            Tabs: Array.Empty<BrowserToolTab>(),
            Error: error
        );
    }

    private static bool IsPlaywrightUnavailable(string? error)
    {
        var normalized = (error ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return normalized.Contains("node executable was not found", StringComparison.Ordinal)
            || normalized.Contains("playwright module could not be loaded", StringComparison.Ordinal)
            || normalized.Contains("cannot find module 'playwright'", StringComparison.Ordinal)
            || normalized.Contains("playwright helper exited", StringComparison.Ordinal)
            || normalized.Contains("playwright helper returned an empty response", StringComparison.Ordinal)
            || normalized.Contains("playwright helper request timed out", StringComparison.Ordinal)
            || normalized.Contains("playwright helper is not running", StringComparison.Ordinal);
    }

    private BrowserToolResult ParsePlaywrightHelperResult(string json, string fallbackAction, string profile, int limit)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var ok = root.TryGetProperty("ok", out var okElement) && okElement.ValueKind == JsonValueKind.True;
        var action = root.TryGetProperty("action", out var actionElement)
            ? actionElement.GetString() ?? fallbackAction
            : fallbackAction;
        var adapter = root.TryGetProperty("adapter", out var adapterElement)
            ? adapterElement.GetString() ?? "playwright"
            : "playwright";
        var running = root.TryGetProperty("running", out var runningElement) && runningElement.ValueKind == JsonValueKind.True;
        var activeTargetId = root.TryGetProperty("activeTargetId", out var activeIdElement)
            ? activeIdElement.GetString()
            : null;
        var activeUrl = root.TryGetProperty("activeUrl", out var activeUrlElement)
            ? activeUrlElement.GetString()
            : null;
        var error = root.TryGetProperty("error", out var errorElement)
            ? errorElement.GetString()
            : null;
        var tabs = new List<BrowserToolTab>();
        if (root.TryGetProperty("tabs", out var tabsElement) && tabsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in tabsElement.EnumerateArray().Take(limit))
            {
                var targetId = item.TryGetProperty("targetId", out var targetIdElement)
                    ? targetIdElement.GetString() ?? string.Empty
                    : string.Empty;
                var url = item.TryGetProperty("url", out var urlElement)
                    ? urlElement.GetString() ?? string.Empty
                    : string.Empty;
                var title = item.TryGetProperty("title", out var titleElement)
                    ? titleElement.GetString() ?? string.Empty
                    : string.Empty;
                var active = item.TryGetProperty("active", out var activeElement) && activeElement.ValueKind == JsonValueKind.True;
                var updatedAtMs = item.TryGetProperty("updatedAtMs", out var updatedElement) && updatedElement.TryGetInt64(out var parsedUpdated)
                    ? parsedUpdated
                    : UtcNowMs();
                tabs.Add(new BrowserToolTab(targetId, url, title, active, updatedAtMs));
            }
        }

        return new BrowserToolResult(
            ok,
            action,
            profile,
            false,
            adapter,
            running,
            activeTargetId,
            activeUrl,
            tabs,
            error
        );
    }

    private static bool ResolveBool(string? value, bool fallback)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => fallback
        };
    }

    private static string ResolveProjectRoot(AppConfig config)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(config.DashboardIndexPath))
        {
            var current = new FileInfo(Path.GetFullPath(config.DashboardIndexPath)).Directory;
            while (current != null)
            {
                candidates.Add(current.FullName);
                current = current.Parent;
            }
        }

        candidates.Add(Environment.CurrentDirectory);
        foreach (var candidate in candidates)
        {
            try
            {
                if (Directory.Exists(Path.Combine(candidate, "apps"))
                    && File.Exists(Path.Combine(candidate, "package.json")))
                {
                    return candidate;
                }
            }
            catch
            {
            }
        }

        return Environment.CurrentDirectory;
    }

    private static string TrimError(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length <= 600)
        {
            return normalized;
        }

        return normalized[..600] + "...";
    }

    private static long UtcNowMs()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private sealed class BrowserProfileState
    {
        public bool Running { get; set; }
        public string? ActiveTargetId { get; set; }
        public long UpdatedAtMs { get; set; }
        public int NextTabNumber { get; set; }
        public Process? HelperProcess { get; set; }
        public string? LastHelperError { get; set; }
        public List<BrowserTabState> Tabs { get; } = new();
    }

    private sealed class BrowserTabState
    {
        public string TargetId { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public long UpdatedAtMs { get; set; }
    }
}
