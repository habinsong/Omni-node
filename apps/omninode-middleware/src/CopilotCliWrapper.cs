using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using System.Globalization;

namespace OmniNode.Middleware;

public sealed class CopilotCliWrapper
{
    private const string DefaultCopilotModel = "gpt-5-mini";
    private const string LegacyDefaultCopilotModel = "claude-sonnet-4.6";
    private static readonly Regex CodeBlockRegex = new(
        "(?s)```(?:python|bash)?\\n(.*?)\\n```",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly Regex DeviceCodeRegex = new(
        "(?:one-time code\\s*[:]?\\s*|enter code\\s+)([A-Z0-9-]{7,20})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly Regex DeviceUrlRegex = new(
        "https://github\\.com/login/device\\S*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly Regex QuotedModelRegex = new(
        "\"([a-zA-Z0-9._/-]+)\"",
        RegexOptions.Compiled
    );
    private static readonly IReadOnlyList<string> FallbackModelIds = new[]
    {
        "claude-sonnet-4.6",
        "claude-sonnet-4.5",
        "claude-haiku-4.5",
        "claude-opus-4.6",
        "claude-opus-4.6-fast",
        "claude-opus-4.5",
        "claude-sonnet-4",
        "gemini-3-pro-preview",
        "gpt-5.3-codex",
        "gpt-5.2-codex",
        "gpt-5.2",
        "gpt-5.1-codex-max",
        "gpt-5.1-codex",
        "gpt-5.1",
        "gpt-5.1-codex-mini",
        "gpt-5-mini",
        "gpt-4.1"
    };
    private static readonly Dictionary<string, CopilotModelMeta> KnownModelMeta = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-sonnet-4.6"] = new("Anthropic", "1x"),
        ["claude-sonnet-4.5"] = new("Anthropic", "1x"),
        ["claude-haiku-4.5"] = new("Anthropic", "0.33x"),
        ["claude-opus-4.6"] = new("Anthropic", "3x"),
        ["claude-opus-4.6-fast"] = new("Anthropic", "30x"),
        ["claude-opus-4.5"] = new("Anthropic", "3x"),
        ["claude-sonnet-4"] = new("Anthropic", "1x"),
        ["gemini-3-pro-preview"] = new("Google", "1x"),
        ["gpt-5.3-codex"] = new("OpenAI", "1x"),
        ["gpt-5.2-codex"] = new("OpenAI", "1x"),
        ["gpt-5.2"] = new("OpenAI", "1x"),
        ["gpt-5.1-codex-max"] = new("OpenAI", "1x"),
        ["gpt-5.1-codex"] = new("OpenAI", "1x"),
        ["gpt-5.1"] = new("OpenAI", "1x"),
        ["gpt-5.1-codex-mini"] = new("OpenAI", "0.33x"),
        ["gpt-5-mini"] = new("OpenAI", "0x"),
        ["gpt-4.1"] = new("OpenAI", "0x")
    };

    private readonly string _ghBinaryPath;
    private readonly string _copilotBinaryPath;
    private readonly string _usageStatePath;
    private readonly int _requestTimeoutSec;
    private readonly object _modeLock = new();
    private readonly object _loginLock = new();
    private readonly object _modelLock = new();
    private readonly object _premiumUsageLock = new();
    private readonly Dictionary<string, CopilotUsage> _usageByModel = new(StringComparer.OrdinalIgnoreCase);
    private CopilotMode? _cachedMode;
    private LoginSession? _activeLoginSession;
    private string _selectedModel;
    private CopilotPremiumUsageSnapshot _premiumUsageCache = new();
    private DateTimeOffset _premiumUsageFetchedAt = DateTimeOffset.MinValue;

    public CopilotCliWrapper(
        string ghBinaryPath,
        string copilotBinaryPath,
        string? defaultModel = null,
        string? usageStatePath = null,
        int requestTimeoutSec = 45
    )
    {
        _ghBinaryPath = ghBinaryPath;
        _copilotBinaryPath = copilotBinaryPath;
        _selectedModel = NormalizeSelectedModel(defaultModel);
        _usageStatePath = string.IsNullOrWhiteSpace(usageStatePath)
            ? Path.Combine(Path.GetTempPath(), "omninode_copilot_usage.json")
            : usageStatePath.Trim();
        _requestTimeoutSec = Math.Max(10, requestTimeoutSec);
        LoadState();
    }

    public async Task<CopilotStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var mode = await ResolveModeAsync(cancellationToken);
        if (mode == CopilotMode.None)
        {
            return new CopilotStatus(false, false, "none", "copilot CLI not found");
        }

        if (mode == CopilotMode.GhCopilot)
        {
            var status = await RunProcessAsync(_ghBinaryPath, new[] { "auth", "status" }, cancellationToken);
            return new CopilotStatus(
                true,
                status.ExitCode == 0,
                "gh copilot",
                status.ExitCode == 0 ? "ready" : Trim(status.StdErr, 500)
            );
        }

        return await GetDirectCopilotStatusAsync(cancellationToken);
    }

    public async Task<string> SuggestCodeAsync(string prompt, CancellationToken cancellationToken)
    {
        var mode = await ResolveModeAsync(cancellationToken);
        if (mode == CopilotMode.None)
        {
            return string.Empty;
        }

        ProcessResult result;
        var model = NormalizeSelectedModel(GetSelectedModel());
        using var timeoutCts = CreateTimeoutToken(cancellationToken);
        if (mode == CopilotMode.DirectCopilot)
        {
            result = await RunProcessAsync(
                _copilotBinaryPath,
                new[] { "--model", model, "-s", "-p", prompt, "--allow-all-tools", "--stream", "off", "--no-color" },
                timeoutCts.Token
            );
        }
        else
        {
            var args = new List<string>
            {
                "copilot",
                "--",
                "--model", model,
                "-s",
                "-p", prompt,
                "--allow-all-tools",
                "--stream", "off",
                "--no-color"
            };
            result = await RunProcessAsync(
                _ghBinaryPath,
                args,
                timeoutCts.Token
            );
        }

        if (result.ExitCode != 0)
        {
            var merged = $"{result.StdErr}\n{result.StdOut}".Trim();
            if (ContainsAuthChallenge(merged))
            {
                return string.Empty;
            }

            Console.Error.WriteLine($"[copilot] exited with {result.ExitCode}: {Trim(result.StdErr, 800)}");
            return string.Empty;
        }

        RecordUsage(model);

        var stdout = result.StdOut.Trim();
        var matches = CodeBlockRegex.Matches(stdout);
        if (matches.Count == 0)
        {
            return stdout;
        }

        return matches[^1].Groups[1].Value.Trim();
    }

    public async Task<string> GenerateChatAsync(string prompt, string? modelOverride, CancellationToken cancellationToken)
    {
        var input = (prompt ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            return "empty input";
        }

        var mode = await ResolveModeAsync(cancellationToken);
        if (mode == CopilotMode.None)
        {
            return "copilot cli not found";
        }

        var model = NormalizeSelectedModel(string.IsNullOrWhiteSpace(modelOverride) ? GetSelectedModel() : modelOverride);
        if (mode == CopilotMode.DirectCopilot)
        {
            using var timeoutCts = CreateTimeoutToken(cancellationToken);
            var result = await RunProcessAsync(
                _copilotBinaryPath,
                new[] { "--model", model, "-p", input, "--silent", "--allow-all-tools", "--stream", "off", "--no-color" },
                timeoutCts.Token
            );

            if (result.ExitCode != 0)
            {
                var mergedError = $"{result.StdErr}\n{result.StdOut}";
                if (ContainsAuthChallenge(mergedError))
                {
                    return "copilot 인증이 만료되었습니다. 다시 로그인하세요.";
                }

                return $"copilot 요청 실패: {Trim(mergedError.Trim(), 700)}";
            }

            RecordUsage(model);
            var output = result.StdOut.Trim();
            return string.IsNullOrWhiteSpace(output)
                ? Trim(result.StdErr.Trim(), 700)
                : output;
        }

        using var ghTimeoutCts = CreateTimeoutToken(cancellationToken);
        var ghArgs = new List<string>
        {
            "copilot",
            "--",
            "--model", model,
            "-p", input,
            "--silent",
            "--allow-all-tools",
            "--stream", "off",
            "--no-color"
        };
        var ghResult = await RunProcessAsync(
            _ghBinaryPath,
            ghArgs,
            ghTimeoutCts.Token
        );

        if (ghResult.ExitCode != 0)
        {
            var mergedError = $"{ghResult.StdErr}\n{ghResult.StdOut}";
            if (ContainsAuthChallenge(mergedError))
            {
                return "copilot 인증이 만료되었습니다. 다시 로그인하세요.";
            }

            return $"copilot 요청 실패: {Trim(mergedError.Trim(), 700)}";
        }

        RecordUsage(model);
        return string.IsNullOrWhiteSpace(ghResult.StdOut)
            ? Trim(ghResult.StdErr.Trim(), 700)
            : ghResult.StdOut.Trim();
    }

    public async Task<IReadOnlyList<CopilotModelInfo>> GetModelsAsync(CancellationToken cancellationToken)
    {
        var ids = new HashSet<string>(FallbackModelIds, StringComparer.OrdinalIgnoreCase);
        var fromHelp = await GetModelIdsFromCliHelpAsync(cancellationToken);
        foreach (var id in fromHelp)
        {
            ids.Add(id);
        }

        Dictionary<string, CopilotUsage> usageSnapshot;
        lock (_modelLock)
        {
            usageSnapshot = _usageByModel.ToDictionary(
                x => x.Key,
                x => x.Value.Clone(),
                StringComparer.OrdinalIgnoreCase
            );
        }

        var result = new List<CopilotModelInfo>();
        foreach (var id in ids.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var meta = KnownModelMeta.TryGetValue(id, out var found)
                ? found
                : new CopilotModelMeta("Unknown", "-");
            usageSnapshot.TryGetValue(id, out var usage);

            result.Add(new CopilotModelInfo
            {
                Id = id,
                Provider = meta.Provider,
                PremiumMultiplier = meta.PremiumMultiplier,
                OutputTokensPerSecond = "-",
                RateLimit = "-",
                ContextWindow = "-",
                MaxCompletionTokens = "-",
                UsageRequests = usage?.Requests ?? 0
            });
        }

        return result;
    }

    public string GetSelectedModel()
    {
        lock (_modelLock)
        {
            return _selectedModel;
        }
    }

    public bool TrySetSelectedModel(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return false;
        }

        var trimmed = modelId.Trim();
        foreach (var ch in trimmed)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '/' || ch == '.')
            {
                continue;
            }

            return false;
        }

        var normalized = NormalizeSelectedModel(trimmed);
        lock (_modelLock)
        {
            _selectedModel = normalized;
            if (!_usageByModel.ContainsKey(normalized))
            {
                _usageByModel[normalized] = new CopilotUsage();
            }
        }

        SaveState();
        return true;
    }

    public IReadOnlyDictionary<string, CopilotUsage> GetUsageSnapshot()
    {
        lock (_modelLock)
        {
            return _usageByModel.ToDictionary(
                x => x.Key,
                x => x.Value.Clone(),
                StringComparer.OrdinalIgnoreCase
            );
        }
    }

    public async Task<CopilotPremiumUsageSnapshot> GetPremiumUsageSnapshotAsync(CancellationToken cancellationToken, bool forceRefresh = false)
    {
        lock (_premiumUsageLock)
        {
            if (!forceRefresh
                && _premiumUsageCache.Available
                && DateTimeOffset.UtcNow - _premiumUsageFetchedAt < TimeSpan.FromMinutes(3))
            {
                return _premiumUsageCache.Clone();
            }
        }

        var fetched = await FetchPremiumUsageSnapshotAsync(cancellationToken);
        lock (_premiumUsageLock)
        {
            _premiumUsageCache = fetched.Clone();
            _premiumUsageFetchedAt = DateTimeOffset.UtcNow;
            return _premiumUsageCache.Clone();
        }
    }

    public async Task<string> StartLoginAsync(CancellationToken cancellationToken)
    {
        var mode = await ResolveModeAsync(cancellationToken);
        if (mode == CopilotMode.None)
        {
            return "copilot cli not found";
        }

        string fileName;
        IReadOnlyList<string> args;
        string modeLabel;

        if (mode == CopilotMode.DirectCopilot)
        {
            fileName = _copilotBinaryPath;
            args = new[] { "login" };
            modeLabel = "copilot";
        }
        else
        {
            fileName = _ghBinaryPath;
            args = new[]
            {
                "auth", "login",
                "--hostname", "github.com",
                "--web",
                "--git-protocol", "https",
                "--skip-ssh-key",
                "--scopes", "read:org"
            };
            modeLabel = "gh copilot";
        }

        LoginSession session;
        lock (_loginLock)
        {
            if (_activeLoginSession != null && !_activeLoginSession.Process.HasExited)
            {
                var runningOutput = _activeLoginSession.GetOutputSnapshot();
                var runningCode = ExtractDeviceCode(runningOutput) ?? "-";
                var runningUrl = ExtractDeviceUrl(runningOutput) ?? "https://github.com/login/device";
                return $"copilot login is already running. code={runningCode} url={runningUrl}";
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            var process = new Process { StartInfo = startInfo };
            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                process.Dispose();
                return $"copilot login start failed: {Trim(ex.Message, 500)}";
            }

            session = new LoginSession(process, modeLabel);
            _activeLoginSession = session;
            session.StdOutPump = PumpLoginStreamAsync(process.StandardOutput, session);
            session.StdErrPump = PumpLoginStreamAsync(process.StandardError, session);
            _ = TrackLoginSessionAsync(session);
        }

        var bootMessage = await BuildLoginBootstrapMessageAsync(session, cancellationToken);
        return bootMessage;
    }

    private async Task<CopilotMode> ResolveModeAsync(CancellationToken cancellationToken)
    {
        lock (_modeLock)
        {
            if (_cachedMode.HasValue)
            {
                return _cachedMode.Value;
            }
        }

        var direct = await RunProcessAsync(_copilotBinaryPath, new[] { "--version" }, cancellationToken);
        if (direct.ExitCode == 0)
        {
            lock (_modeLock)
            {
                _cachedMode = CopilotMode.DirectCopilot;
            }
            return CopilotMode.DirectCopilot;
        }

        var gh = await RunProcessAsync(_ghBinaryPath, new[] { "copilot", "--help" }, cancellationToken);
        if (gh.ExitCode == 0)
        {
            lock (_modeLock)
            {
                _cachedMode = CopilotMode.GhCopilot;
            }
            return CopilotMode.GhCopilot;
        }

        lock (_modeLock)
        {
            _cachedMode = CopilotMode.None;
        }
        return CopilotMode.None;
    }

    private async Task<CopilotStatus> GetDirectCopilotStatusAsync(CancellationToken cancellationToken)
    {
        LoginSession? runningSession;
        lock (_loginLock)
        {
            runningSession = _activeLoginSession != null && !_activeLoginSession.Process.HasExited
                ? _activeLoginSession
                : null;
        }

        if (runningSession != null)
        {
            var runningOutput = runningSession.GetOutputSnapshot();
            var code = ExtractDeviceCode(runningOutput) ?? "-";
            var url = ExtractDeviceUrl(runningOutput) ?? "https://github.com/login/device";
            return new CopilotStatus(true, false, "copilot", $"login pending. code={code} url={url}");
        }

        var ghStatus = await RunProcessAsync(_ghBinaryPath, new[] { "auth", "status" }, cancellationToken);
        if (ghStatus.ExitCode == 0)
        {
            return new CopilotStatus(true, true, "copilot", "ready (non-consuming auth check)");
        }

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("COPILOT_GITHUB_TOKEN"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GH_TOKEN"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GITHUB_TOKEN")))
        {
            return new CopilotStatus(true, true, "copilot", "ready (token env detected)");
        }

        // 상태 조회는 비과금 원칙으로 동작한다. 실제 인증 유효성은 첫 요청 시 에러로 확인된다.
        return new CopilotStatus(true, true, "copilot", "ready (non-consuming check)");
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken
    )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.Environment["TERM"] = "dumb";
        startInfo.Environment["NO_COLOR"] = "1";
        startInfo.Environment["CLICOLOR"] = "0";
        startInfo.Environment["COLUMNS"] = "4000";
        startInfo.Environment["LINES"] = "4000";

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        try
        {
            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            return new ProcessResult(process.ExitCode, stdout, stderr);
        }
        catch (Exception ex)
        {
            return new ProcessResult(127, string.Empty, ex.Message);
        }
    }

    private async Task<string> BuildLoginBootstrapMessageAsync(LoginSession session, CancellationToken cancellationToken)
    {
        try
        {
            var waitTask = Task.WhenAny(
                session.HintReceived.Task,
                Task.Delay(TimeSpan.FromSeconds(4), cancellationToken),
                session.Process.WaitForExitAsync(cancellationToken)
            );
            await waitTask;
        }
        catch (OperationCanceledException)
        {
            return "copilot login started. keep this session open and complete browser authentication.";
        }

        var output = session.GetOutputSnapshot();
        var code = ExtractDeviceCode(output);
        var url = ExtractDeviceUrl(output);

        if (!string.IsNullOrWhiteSpace(code) || !string.IsNullOrWhiteSpace(url))
        {
            return $"copilot login started ({session.ModeLabel}). code={code ?? "-"} url={url ?? "-"}";
        }

        if (session.Process.HasExited)
        {
            return session.Process.ExitCode == 0
                ? "copilot login command completed."
                : $"copilot login failed: {Trim(output, 700)}";
        }

        return $"copilot login started ({session.ModeLabel}). browser/device flow output will continue in background.";
    }

    private async Task TrackLoginSessionAsync(LoginSession session)
    {
        try
        {
            await Task.WhenAll(
                session.StdOutPump ?? Task.CompletedTask,
                session.StdErrPump ?? Task.CompletedTask,
                session.Process.WaitForExitAsync()
            );
        }
        catch
        {
        }
        finally
        {
            var finalText = session.GetOutputSnapshot();
            if (session.Process.ExitCode == 0)
            {
                Console.WriteLine($"[copilot] login completed ({session.ModeLabel})");
            }
            else
            {
                Console.Error.WriteLine($"[copilot] login failed ({session.ModeLabel}): {Trim(finalText, 800)}");
            }

            lock (_loginLock)
            {
                if (ReferenceEquals(_activeLoginSession, session))
                {
                    _activeLoginSession = null;
                }
            }

            session.Process.Dispose();
        }
    }

    private static async Task PumpLoginStreamAsync(StreamReader reader, LoginSession session)
    {
        try
        {
            while (true)
            {
                var line = await reader.ReadLineAsync();
                if (line == null)
                {
                    break;
                }

                session.AppendOutput(line);
                if (line.Contains("one-time code", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("open this url", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("login/device", StringComparison.OrdinalIgnoreCase))
                {
                    session.HintReceived.TrySetResult(true);
                }
            }
        }
        catch
        {
        }
    }

    private static string? ExtractDeviceCode(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = DeviceCodeRegex.Match(text);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractDeviceUrl(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = DeviceUrlRegex.Match(text);
        return match.Success ? match.Value : null;
    }

    private static string Trim(string value, int max)
    {
        return value.Length <= max ? value : value[..max] + "...(truncated)";
    }

    private CancellationTokenSource CreateTimeoutToken(CancellationToken outerToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
        cts.CancelAfter(TimeSpan.FromSeconds(_requestTimeoutSec));
        return cts;
    }

    private void LoadState()
    {
        try
        {
            var fullPath = Path.GetFullPath(_usageStatePath);
            if (!File.Exists(fullPath))
            {
                return;
            }

            var json = File.ReadAllText(fullPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            var state = System.Text.Json.JsonSerializer.Deserialize(json, OmniJsonContext.Default.CopilotState);
            if (state == null)
            {
                return;
            }

            lock (_modelLock)
            {
                if (!string.IsNullOrWhiteSpace(state.SelectedModel))
                {
                    _selectedModel = NormalizeSelectedModel(state.SelectedModel);
                }

                _usageByModel.Clear();
                foreach (var entry in state.UsageByModel)
                {
                    _usageByModel[entry.Key] = entry.Value ?? new CopilotUsage();
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[copilot] state load failed: {Trim(ex.Message, 300)}");
        }
    }

    private void SaveState()
    {
        try
        {
            var fullPath = Path.GetFullPath(_usageStatePath);
            var dir = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(dir))
            {
                return;
            }

            Directory.CreateDirectory(dir);
            var state = new CopilotState();
            lock (_modelLock)
            {
                state.SelectedModel = _selectedModel;
                state.UsageByModel = _usageByModel.ToDictionary(
                    x => x.Key,
                    x => x.Value.Clone(),
                    StringComparer.OrdinalIgnoreCase
                );
            }

            var json = System.Text.Json.JsonSerializer.Serialize(state, OmniJsonContext.Default.CopilotState);
            AtomicFileStore.WriteAllText(fullPath, json, ownerOnly: true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[copilot] state save failed: {Trim(ex.Message, 300)}");
        }
    }

    private async Task<IReadOnlyList<string>> GetModelIdsFromCliHelpAsync(CancellationToken cancellationToken)
    {
        var mode = await ResolveModeAsync(cancellationToken);
        ProcessResult result;
        if (mode == CopilotMode.DirectCopilot)
        {
            result = await RunProcessAsync(_copilotBinaryPath, new[] { "--help" }, cancellationToken);
        }
        else if (mode == CopilotMode.GhCopilot)
        {
            result = await RunProcessAsync(_ghBinaryPath, new[] { "copilot", "--", "--help" }, cancellationToken);
        }
        else
        {
            return Array.Empty<string>();
        }

        var merged = $"{result.StdOut}\n{result.StdErr}";
        return ParseModelChoices(merged);
    }

    private static string NormalizeSelectedModel(string? modelId)
    {
        _ = modelId;
        // Omni-node에서는 Copilot 모델을 gpt-5-mini로 고정한다.
        return DefaultCopilotModel;
    }

    private static IReadOnlyList<string> ParseModelChoices(string helpText)
    {
        if (string.IsNullOrWhiteSpace(helpText))
        {
            return Array.Empty<string>();
        }

        var modelFlagIndex = helpText.IndexOf("--model <model>", StringComparison.OrdinalIgnoreCase);
        if (modelFlagIndex < 0)
        {
            return Array.Empty<string>();
        }

        var choicesIndex = helpText.IndexOf("choices:", modelFlagIndex, StringComparison.OrdinalIgnoreCase);
        if (choicesIndex < 0)
        {
            return Array.Empty<string>();
        }

        var startParen = helpText.IndexOf('(', choicesIndex);
        var endParen = helpText.IndexOf(')', choicesIndex);
        if (startParen < 0 || endParen < 0 || endParen <= startParen)
        {
            return Array.Empty<string>();
        }

        var choicesSegment = helpText[startParen..(endParen + 1)];
        var values = new List<string>();
        foreach (Match match in QuotedModelRegex.Matches(choicesSegment))
        {
            var value = match.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value);
            }
        }

        return values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void RecordUsage(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return;
        }

        lock (_modelLock)
        {
            if (!_usageByModel.TryGetValue(modelId, out var usage))
            {
                usage = new CopilotUsage();
                _usageByModel[modelId] = usage;
            }

            usage.Requests++;
        }

        SaveState();
    }

    private static bool ContainsAuthChallenge(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return DeviceCodeRegex.IsMatch(text)
               || text.Contains("login/device", StringComparison.OrdinalIgnoreCase)
               || text.Contains("waiting for authorization", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<CopilotPremiumUsageSnapshot> FetchPremiumUsageSnapshotAsync(CancellationToken cancellationToken)
    {
        var mode = await ResolveModeAsync(cancellationToken);
        if (mode == CopilotMode.None)
        {
            return BuildPremiumUsageError("copilot/gh cli가 설치되지 않아 Copilot 사용량을 조회할 수 없습니다.");
        }

        var loginResult = await RunProcessAsync(_ghBinaryPath, new[] { "api", "user", "-q", ".login" }, cancellationToken);
        if (loginResult.ExitCode != 0 || string.IsNullOrWhiteSpace(loginResult.StdOut))
        {
            return BuildPremiumUsageError(
                $"GitHub 사용자 조회 실패: {Trim((loginResult.StdErr + "\n" + loginResult.StdOut).Trim(), 400)}"
            );
        }

        var username = loginResult.StdOut.Trim();
        var path = $"/users/{username}/settings/billing/premium_request/usage?per_page=100";
        var usageResult = await RunProcessAsync(
            _ghBinaryPath,
            new[]
            {
                "api",
                "-H", "Accept: application/vnd.github+json",
                "-H", "X-GitHub-Api-Version: 2022-11-28",
                path
            },
            cancellationToken
        );

        var mergedError = (usageResult.StdErr + "\n" + usageResult.StdOut).Trim();
        if (usageResult.ExitCode != 0)
        {
            var requiresScope = mergedError.Contains("needs the \"user\" scope", StringComparison.OrdinalIgnoreCase)
                                || mergedError.Contains("request it, run:  gh auth refresh", StringComparison.OrdinalIgnoreCase);
            var help = requiresScope
                ? "GitHub token에 user scope가 없어 Copilot Premium 사용량 API를 호출할 수 없습니다. `gh auth refresh -h github.com -s user` 후 다시 시도하세요."
                : $"Copilot Premium 사용량 조회 실패: {Trim(mergedError, 500)}";
            var snapshot = BuildPremiumUsageError(help);
            snapshot.RequiresUserScope = requiresScope;
            snapshot.Username = username;
            snapshot.FeaturesUrl = "https://github.com/settings/copilot/features";
            snapshot.BillingUrl = "https://github.com/settings/billing/premium_requests_usage";
            snapshot.RefreshedLocal = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");
            return snapshot;
        }

        try
        {
            using var doc = JsonDocument.Parse(usageResult.StdOut);
            var root = doc.RootElement;
            var usageItems = ExtractUsageItems(root);

            var grouped = usageItems
                .Where(x => x.Quantity > 0d)
                .GroupBy(x => x.Model, StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    Model = group.Key,
                    Quantity = group.Sum(x => x.Quantity)
                })
                .OrderByDescending(x => x.Quantity)
                .ToArray();

            var totalUsedFromItems = grouped.Sum(x => x.Quantity);
            var totalUsedCandidate = TryFindNumberRecursive(root,
                                "total_used",
                                "totalUsed",
                                "total_quantity",
                                "totalQuantity",
                                "used_quantity",
                                "usedQuantity",
                                "netQuantity")
                            ?? 0d;
            var totalUsed = totalUsedCandidate > 0d ? totalUsedCandidate : totalUsedFromItems;
            var quota = TryFindNumberRecursive(root,
                            "total_monthly_quota",
                            "totalMonthlyQuota",
                            "monthly_quota",
                            "monthlyQuota",
                            "included_quantity",
                            "includedQuantity",
                            "premium_requests_quota",
                            "premiumRequestsQuota");
            var percentUsed = TryFindNumberRecursive(root,
                                  "percent_used",
                                  "percentUsed",
                                  "usage_percent",
                                  "usagePercent",
                                  "premium_request_usage_percent",
                                  "premiumRequestUsagePercent");
            var plan = TryFindStringRecursive(root, "plan", "plan_name", "planName", "subscription");
            if ((!quota.HasValue || quota.Value <= 0d) && !string.IsNullOrWhiteSpace(plan))
            {
                var inferredQuota = InferQuotaFromPlanName(plan);
                if (inferredQuota.HasValue)
                {
                    quota = inferredQuota.Value;
                }
            }

            // usage API 응답에 월 한도가 누락되는 경우가 있어 기본 포함량(300)으로 추정한다.
            if ((!quota.HasValue || quota.Value <= 0d) && totalUsedFromItems > 0d)
            {
                quota = 300d;
                if (string.IsNullOrWhiteSpace(plan) || plan == "-")
                {
                    plan = "inferred-pro-default";
                }
            }

            if ((!percentUsed.HasValue || percentUsed.Value <= 0d) && quota.HasValue && quota.Value > 0d)
            {
                percentUsed = (totalUsed / quota.Value) * 100d;
            }

            var items = new List<CopilotPremiumUsageItem>();
            foreach (var entry in grouped)
            {
                var ratio = totalUsedFromItems > 0d ? (entry.Quantity / totalUsedFromItems) * 100d : 0d;
                items.Add(new CopilotPremiumUsageItem
                {
                    Model = entry.Model,
                    Requests = entry.Quantity,
                    Percent = ratio
                });
            }

            return new CopilotPremiumUsageSnapshot
            {
                Available = true,
                RequiresUserScope = false,
                Message = "GitHub Billing API에서 Copilot Premium Requests 사용량을 불러왔습니다.",
                Username = username,
                PlanName = string.IsNullOrWhiteSpace(plan) ? "-" : plan,
                UsedRequests = totalUsed,
                MonthlyQuota = quota ?? 0d,
                PercentUsed = percentUsed ?? 0d,
                RefreshedLocal = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                FeaturesUrl = "https://github.com/settings/copilot/features",
                BillingUrl = "https://github.com/settings/billing/premium_requests_usage",
                Items = items
            };
        }
        catch (Exception ex)
        {
            var parseError = BuildPremiumUsageError($"Copilot Premium 사용량 응답 파싱 실패: {Trim(ex.Message, 400)}");
            parseError.Username = username;
            parseError.FeaturesUrl = "https://github.com/settings/copilot/features";
            parseError.BillingUrl = "https://github.com/settings/billing/premium_requests_usage";
            parseError.RefreshedLocal = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");
            return parseError;
        }
    }

    private static CopilotPremiumUsageSnapshot BuildPremiumUsageError(string message)
    {
        return new CopilotPremiumUsageSnapshot
        {
            Available = false,
            Message = message,
            Username = string.Empty,
            PlanName = "-",
            UsedRequests = 0d,
            MonthlyQuota = 0d,
            PercentUsed = 0d,
            RefreshedLocal = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            FeaturesUrl = "https://github.com/settings/copilot/features",
            BillingUrl = "https://github.com/settings/billing/premium_requests_usage",
            Items = new List<CopilotPremiumUsageItem>()
        };
    }

    private static IReadOnlyList<PremiumUsageRawItem> ExtractUsageItems(JsonElement root)
    {
        JsonElement itemsElement;
        if (TryGetPropertyCaseInsensitive(root, "usageItems", out itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
        {
            return ParseUsageItemsArray(itemsElement);
        }

        if (root.ValueKind == JsonValueKind.Array)
        {
            return ParseUsageItemsArray(root);
        }

        if (TryGetPropertyCaseInsensitive(root, "items", out itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
        {
            return ParseUsageItemsArray(itemsElement);
        }

        return Array.Empty<PremiumUsageRawItem>();
    }

    private static IReadOnlyList<PremiumUsageRawItem> ParseUsageItemsArray(JsonElement array)
    {
        var result = new List<PremiumUsageRawItem>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var model = TryGetStringProperty(item, "model")
                        ?? TryGetStringProperty(item, "sku")
                        ?? TryGetStringProperty(item, "product")
                        ?? TryGetStringProperty(item, "service")
                        ?? "unknown";
            var netQuantity = TryGetDoubleProperty(item, "netQuantity") ?? 0d;
            var grossQuantity = TryGetDoubleProperty(item, "grossQuantity") ?? 0d;
            var discountQuantity = TryGetDoubleProperty(item, "discountQuantity") ?? 0d;
            var rawQuantity = TryGetDoubleProperty(item, "quantity") ?? 0d;

            // Billing API에서 포함량 할인 적용 시 netQuantity가 0일 수 있으므로 gross/discount를 폴백으로 사용한다.
            var quantity = netQuantity > 0d
                ? netQuantity
                : (grossQuantity > 0d
                    ? grossQuantity
                    : (discountQuantity > 0d ? discountQuantity : rawQuantity));
            if (quantity <= 0d)
            {
                continue;
            }

            result.Add(new PremiumUsageRawItem(model, quantity));
        }

        return result;
    }

    private static double? TryFindNumberRecursive(JsonElement element, params string[] keys)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in keys)
            {
                if (TryGetPropertyCaseInsensitive(element, key, out var value))
                {
                    var parsed = TryParseJsonNumber(value);
                    if (parsed.HasValue)
                    {
                        return parsed.Value;
                    }
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                var nested = TryFindNumberRecursive(property.Value, keys);
                if (nested.HasValue)
                {
                    return nested.Value;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = TryFindNumberRecursive(item, keys);
                if (nested.HasValue)
                {
                    return nested.Value;
                }
            }
        }

        return null;
    }

    private static string? TryFindStringRecursive(JsonElement element, params string[] keys)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in keys)
            {
                if (TryGetPropertyCaseInsensitive(element, key, out var value))
                {
                    var parsed = TryParseJsonString(value);
                    if (!string.IsNullOrWhiteSpace(parsed))
                    {
                        return parsed;
                    }
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                var nested = TryFindStringRecursive(property.Value, keys);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = TryFindStringRecursive(item, keys);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals(name) || property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? TryGetStringProperty(JsonElement element, string key)
    {
        if (!TryGetPropertyCaseInsensitive(element, key, out var value))
        {
            return null;
        }

        return TryParseJsonString(value);
    }

    private static double? TryGetDoubleProperty(JsonElement element, string key)
    {
        if (!TryGetPropertyCaseInsensitive(element, key, out var value))
        {
            return null;
        }

        return TryParseJsonNumber(value);
    }

    private static string? TryParseJsonString(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.GetRawText();
        }

        return null;
    }

    private static double? TryParseJsonNumber(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetDouble(out var numeric))
            {
                return numeric;
            }

            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static double? InferQuotaFromPlanName(string planName)
    {
        var lowered = planName.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(lowered))
        {
            return null;
        }

        if (lowered.Contains("pro+", StringComparison.Ordinal) || lowered.Contains("pro plus", StringComparison.Ordinal))
        {
            return 1500d;
        }

        if (lowered.Contains("enterprise", StringComparison.Ordinal)
            || lowered.Contains("business", StringComparison.Ordinal)
            || lowered.Contains("pro", StringComparison.Ordinal))
        {
            return 300d;
        }

        return null;
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    private enum CopilotMode
    {
        None,
        DirectCopilot,
        GhCopilot
    }

    private sealed class LoginSession
    {
        private readonly StringBuilder _output = new();
        private readonly object _outputLock = new();

        public LoginSession(Process process, string modeLabel)
        {
            Process = process;
            ModeLabel = modeLabel;
        }

        public Process Process { get; }
        public string ModeLabel { get; }
        public TaskCompletionSource<bool> HintReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task? StdOutPump { get; set; }
        public Task? StdErrPump { get; set; }

        public void AppendOutput(string line)
        {
            lock (_outputLock)
            {
                _output.AppendLine(line);
            }
        }

        public string GetOutputSnapshot()
        {
            lock (_outputLock)
            {
                return _output.ToString();
            }
        }
    }

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
    private sealed record CopilotModelMeta(string Provider, string PremiumMultiplier);
    private sealed record PremiumUsageRawItem(string Model, double Quantity);
}

public sealed record CopilotStatus(
    bool Installed,
    bool Authenticated,
    string Mode,
    string Detail
);

public sealed class CopilotUsage
{
    public long Requests { get; set; }

    public CopilotUsage Clone()
    {
        return new CopilotUsage
        {
            Requests = Requests
        };
    }
}

public sealed class CopilotModelInfo
{
    public string Id { get; set; } = string.Empty;
    public string Provider { get; set; } = "Unknown";
    public string PremiumMultiplier { get; set; } = "-";
    public string OutputTokensPerSecond { get; set; } = "-";
    public string RateLimit { get; set; } = "-";
    public string ContextWindow { get; set; } = "-";
    public string MaxCompletionTokens { get; set; } = "-";
    public long UsageRequests { get; set; }
}

public sealed class CopilotPremiumUsageSnapshot
{
    public bool Available { get; set; }
    public bool RequiresUserScope { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string PlanName { get; set; } = "-";
    public double UsedRequests { get; set; }
    public double MonthlyQuota { get; set; }
    public double PercentUsed { get; set; }
    public string RefreshedLocal { get; set; } = string.Empty;
    public string FeaturesUrl { get; set; } = "https://github.com/settings/copilot/features";
    public string BillingUrl { get; set; } = "https://github.com/settings/billing/premium_requests_usage";
    public List<CopilotPremiumUsageItem> Items { get; set; } = new();

    public CopilotPremiumUsageSnapshot Clone()
    {
        return new CopilotPremiumUsageSnapshot
        {
            Available = Available,
            RequiresUserScope = RequiresUserScope,
            Message = Message,
            Username = Username,
            PlanName = PlanName,
            UsedRequests = UsedRequests,
            MonthlyQuota = MonthlyQuota,
            PercentUsed = PercentUsed,
            RefreshedLocal = RefreshedLocal,
            FeaturesUrl = FeaturesUrl,
            BillingUrl = BillingUrl,
            Items = Items.Select(x => x.Clone()).ToList()
        };
    }
}

public sealed class CopilotPremiumUsageItem
{
    public string Model { get; set; } = string.Empty;
    public double Requests { get; set; }
    public double Percent { get; set; }

    public CopilotPremiumUsageItem Clone()
    {
        return new CopilotPremiumUsageItem
        {
            Model = Model,
            Requests = Requests,
            Percent = Percent
        };
    }
}

public sealed class CopilotState
{
    public string SelectedModel { get; set; } = string.Empty;
    public Dictionary<string, CopilotUsage> UsageByModel { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
