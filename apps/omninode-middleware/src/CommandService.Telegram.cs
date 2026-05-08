using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    private Task<string?> TryHandleTelegramProfileCommandAsync(string text, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult<string?>(null);
        }

        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return Task.FromResult<string?>(null);
        }

        var command = tokens[0].ToLowerInvariant();
        if (command != "/talk" && command != "/code")
        {
            return Task.FromResult<string?>(null);
        }

        if (command == "/code" && tokens.Length >= 2)
        {
            var second = tokens[1].Trim().ToLowerInvariant();
            if (second != "low" && second != "high" && second != "help")
            {
                return Task.FromResult<string?>(null);
            }
        }

        if (tokens.Length >= 2 && tokens[1].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<string?>("""
                [빠른 프로필]
                - /talk [low|high] : 대화 위주로 맞춤
                - /code [low|high] : 코딩 위주로 맞춤

                예시:
                - /talk low
                - /code high
                - 그냥 "코딩용으로 바꿔" 라고 말해도 됩니다.
                """);
        }

        var requestedThinking = tokens.Length >= 2 ? NormalizeThinkingLevel(tokens[1], "auto") : "auto";
        lock (_telegramLlmLock)
        {
            if (command == "/talk")
            {
                ApplyTelegramTalkDefaults(requestedThinking);
                return Task.FromResult<string?>(
                    $"텔레그램 프로필을 대화용으로 바꿨습니다. 모드={FormatModeDisplayName(_telegramLlmPreferences.Mode)}, thinking={_telegramLlmPreferences.TalkThinkingLevel}"
                );
            }

            ApplyTelegramCodeDefaults(requestedThinking);
            return Task.FromResult<string?>(
                $"텔레그램 프로필을 코딩용으로 바꿨습니다. 모드={FormatModeDisplayName(_telegramLlmPreferences.Mode)}, thinking={_telegramLlmPreferences.CodeThinkingLevel}"
            );
        }
    }

    private void ApplyTelegramTalkDefaults(string requestedThinking)
    {
        var fastModel = string.IsNullOrWhiteSpace(_config.GroqModel) ? DefaultGroqPrimaryModel : _config.GroqModel;
        _telegramLlmPreferences.Profile = "talk";
        _telegramLlmPreferences.Mode = "orchestration";
        _telegramLlmPreferences.SingleProvider = "groq";
        _telegramLlmPreferences.SingleModel = fastModel;
        _telegramLlmPreferences.AutoGroqComplexUpgrade = true;
        _telegramLlmPreferences.OrchestrationProvider = "gemini";
        _telegramLlmPreferences.OrchestrationModel = _config.GeminiModel;
        _telegramLlmPreferences.MultiGroqModel = fastModel;
        _telegramLlmPreferences.MultiGeminiModel = _config.GeminiModel;
        _telegramLlmPreferences.MultiCopilotModel = DefaultCopilotModel;
        _telegramLlmPreferences.MultiCerebrasModel = _config.CerebrasModel;
        _telegramLlmPreferences.MultiCodexModel = _config.CodexModel;
        _telegramLlmPreferences.MultiSummaryProvider = "gemini";
        _telegramLlmPreferences.TalkThinkingLevel = NormalizeThinkingLevel(requestedThinking, "low");
    }

    private void ApplyTelegramCodeDefaults(string requestedThinking)
    {
        var fastModel = string.IsNullOrWhiteSpace(_config.GroqModel) ? DefaultGroqPrimaryModel : _config.GroqModel;
        _telegramLlmPreferences.Profile = "code";
        _telegramLlmPreferences.Mode = "orchestration";
        _telegramLlmPreferences.SingleProvider = "copilot";
        _telegramLlmPreferences.SingleModel = DefaultCopilotModel;
        _telegramLlmPreferences.AutoGroqComplexUpgrade = false;
        _telegramLlmPreferences.OrchestrationProvider = "gemini";
        _telegramLlmPreferences.OrchestrationModel = _config.GeminiModel;
        _telegramLlmPreferences.MultiGroqModel = fastModel;
        _telegramLlmPreferences.MultiGeminiModel = _config.GeminiModel;
        _telegramLlmPreferences.MultiCopilotModel = DefaultCopilotModel;
        _telegramLlmPreferences.MultiCerebrasModel = _config.CerebrasModel;
        _telegramLlmPreferences.MultiCodexModel = _config.CodexModel;
        _telegramLlmPreferences.MultiSummaryProvider = "gemini";
        _telegramLlmPreferences.CodeThinkingLevel = NormalizeThinkingLevel(requestedThinking, "high");
    }

    private static string NormalizeThinkingLevel(string? value, string fallback)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized == "low" || normalized == "high")
        {
            return normalized;
        }

        return fallback;
    }

    private async Task<string?> TryHandleTelegramLlmControlCommandAsync(string text, CancellationToken cancellationToken)
    {
        if (!text.StartsWith("/llm", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 1 || tokens[1].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            return BuildUnifiedLlmHelpText("telegram");
        }

        if (tokens[1].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            TelegramLlmPreferences snapshot;
            lock (_telegramLlmLock)
            {
                snapshot = _telegramLlmPreferences.Clone();
            }

            var quota = GetTelegramUpgradeQuotaSnapshot();
            var copilotStatus = await _copilotWrapper.GetStatusAsync(cancellationToken);
            var toolSnapshot = _toolRegistry.GetAvailabilitySnapshot();
            var enabledTools = toolSnapshot
                .Where(item => item.Enabled)
                .Select(item => item.ToolId)
                .ToArray();
            var pendingTools = toolSnapshot
                .Where(item => !item.Enabled)
                .Select(item => $"{item.ToolId}({item.Reason})")
                .ToArray();

            var enabledText = enabledTools.Length == 0 ? "(none)" : string.Join(", ", enabledTools);
            var pendingText = pendingTools.Length == 0 ? "(none)" : string.Join(", ", pendingTools);

            return $"""
                    {BuildChannelModelStatus("telegram")}

                    [부가 상태]
                    프로필: {snapshot.Profile}
                    thinking.talk: {snapshot.TalkThinkingLevel}
                    thinking.code: {snapshot.CodeThinkingLevel}
                    qwen 업그레이드 사용량: {quota.Used}/{quota.Cap} (day={quota.DayKey})
                    Copilot 상태: {copilotStatus.Mode} / {(copilotStatus.Authenticated ? "authenticated" : "unauthenticated")}
                    사용 가능 도구: {enabledText}
                    대기 중 도구: {pendingText}
                    """;
        }

        if (tokens[1].Equals("mode", StringComparison.OrdinalIgnoreCase))
        {
            if (tokens.Length < 3)
            {
                return "사용법: /llm mode <single|orchestration|multi>";
            }
            return SetChannelMode("telegram", tokens[2].ToLowerInvariant());
        }

        if (tokens[1].Equals("models", StringComparison.OrdinalIgnoreCase))
        {
            var target = tokens.Length >= 3 ? tokens[2].Trim().ToLowerInvariant() : "all";
            return await BuildTelegramModelsReportAsync(target, cancellationToken);
        }

        if (tokens[1].Equals("usage", StringComparison.OrdinalIgnoreCase)
            || tokens[1].Equals("limits", StringComparison.OrdinalIgnoreCase)
            || tokens[1].Equals("quota", StringComparison.OrdinalIgnoreCase))
        {
            return await BuildTelegramUsageReportAsync(cancellationToken);
        }

        if (tokens[1].Equals("set", StringComparison.OrdinalIgnoreCase))
        {
            if (tokens.Length < 4)
            {
                return "사용법: /llm set <groq|copilot|codex> <model-id>";
            }

            var provider = tokens[2].Trim().ToLowerInvariant();
            var modelId = string.Join(' ', tokens.Skip(3)).Trim();
            if (provider == "groq")
            {
                return await SetGroqModelForTelegramAsync(modelId, cancellationToken);
            }

            if (provider == "copilot")
            {
                return await SetCopilotModelForTelegramAsync(modelId, cancellationToken);
            }

            if (provider == "codex")
            {
                var providerSet = SetChannelProvider("telegram", "single", "codex");
                if (providerSet.StartsWith("지원", StringComparison.OrdinalIgnoreCase)
                    || providerSet.StartsWith("invalid", StringComparison.OrdinalIgnoreCase))
                {
                    return providerSet;
                }

                return SetChannelModel("telegram", "single", modelId);
            }

            return "사용법: /llm set <groq|copilot|codex> <model-id>";
        }

        if (tokens[1].Equals("single", StringComparison.OrdinalIgnoreCase))
        {
            if (tokens.Length < 4)
            {
                return "사용법: /llm single provider <groq|gemini|copilot|cerebras|codex> | /llm single model <model-id>";
            }

            if (tokens[2].Equals("provider", StringComparison.OrdinalIgnoreCase))
            {
                return SetChannelProvider("telegram", "single", tokens[3].Trim().ToLowerInvariant());
            }

            if (tokens[2].Equals("model", StringComparison.OrdinalIgnoreCase))
            {
                var model = string.Join(' ', tokens.Skip(3)).Trim();
                if (string.IsNullOrWhiteSpace(model))
                {
                    return "usage: /llm single model <model-id>";
                }

                return SetChannelModel("telegram", "single", model);
            }

            return "사용법: /llm single provider <groq|gemini|copilot|cerebras|codex> | /llm single model <model-id>";
        }

        if (tokens[1].Equals("orchestration", StringComparison.OrdinalIgnoreCase))
        {
            if (tokens.Length < 4)
            {
                return "사용법: /llm orchestration provider <auto|groq|gemini|copilot|cerebras|codex> | /llm orchestration model <model-id>";
            }

            if (tokens[2].Equals("provider", StringComparison.OrdinalIgnoreCase))
            {
                return SetChannelProvider("telegram", "orchestration", tokens[3].Trim().ToLowerInvariant());
            }

            if (tokens[2].Equals("model", StringComparison.OrdinalIgnoreCase))
            {
                var model = string.Join(' ', tokens.Skip(3)).Trim();
                if (string.IsNullOrWhiteSpace(model))
                {
                    return "usage: /llm orchestration model <model-id>";
                }

                return SetChannelModel("telegram", "orchestration", model);
            }

            return "사용법: /llm orchestration provider <auto|groq|gemini|copilot|cerebras|codex> | /llm orchestration model <model-id>";
        }

        if (tokens[1].Equals("multi", StringComparison.OrdinalIgnoreCase))
        {
            if (tokens.Length < 4)
            {
                return "사용법: /llm multi <groq|gemini|copilot|cerebras|codex> <model-id> | /llm multi summary <auto|groq|gemini|copilot|cerebras|codex>";
            }

            var key = tokens[2].ToLowerInvariant();
            var value = string.Join(' ', tokens.Skip(3)).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return "사용법: /llm multi <groq|gemini|copilot|cerebras|codex> <model-id> | /llm multi summary <auto|groq|gemini|copilot|cerebras|codex>";
            }

            lock (_telegramLlmLock)
            {
                if (key == "groq")
                {
                    return SetChannelModel("telegram", "multi.groq", value);
                }

                if (key == "gemini")
                {
                    return SetChannelModel("telegram", "multi.gemini", value);
                }

                if (key == "copilot")
                {
                    return SetChannelModel("telegram", "multi.copilot", value);
                }

                if (key == "cerebras")
                {
                    return SetChannelModel("telegram", "multi.cerebras", value);
                }

                if (key == "codex")
                {
                    return SetChannelModel("telegram", "multi.codex", value);
                }

                if (key == "summary")
                {
                    return SetChannelProvider("telegram", "summary", value.Trim().ToLowerInvariant());
                }
            }

            return "사용법: /llm multi <groq|gemini|copilot|cerebras|codex> <model-id> | /llm multi summary <auto|groq|gemini|copilot|cerebras|codex>";
        }

        return "알 수 없는 /llm 명령입니다. /llm help 또는 자연어 요청을 사용하세요.";
    }

    private Task<string?> TryHandleTelegramQuickModelCommandAsync(string text, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        if (!text.StartsWith("/model", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<string?>(null);
        }

        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 2)
        {
            return Task.FromResult<string?>("사용법: /model <groq|gemini|copilot|cerebras|codex>");
        }

        var key = tokens[1].Trim().ToLowerInvariant();
        lock (_telegramLlmLock)
        {
            _telegramLlmPreferences.Profile = "default";
            _telegramLlmPreferences.Mode = "single";
            if (key == "groq")
            {
                _telegramLlmPreferences.SingleProvider = "groq";
                _telegramLlmPreferences.SingleModel = string.IsNullOrWhiteSpace(_config.GroqModel) ? DefaultGroqPrimaryModel : _config.GroqModel;
                _telegramLlmPreferences.AutoGroqComplexUpgrade = true;
                return Task.FromResult<string?>($"단일 제공자를 Groq로 바꿨습니다. 현재 모델: {_telegramLlmPreferences.SingleModel}");
            }

            if (key == "gemini")
            {
                _telegramLlmPreferences.SingleProvider = "gemini";
                _telegramLlmPreferences.SingleModel = _config.GeminiModel;
                _telegramLlmPreferences.AutoGroqComplexUpgrade = false;
                return Task.FromResult<string?>($"단일 제공자를 Gemini로 바꿨습니다. 현재 모델: {_telegramLlmPreferences.SingleModel}");
            }

            if (key == "copilot")
            {
                _telegramLlmPreferences.SingleProvider = "copilot";
                _telegramLlmPreferences.SingleModel = DefaultCopilotModel;
                _telegramLlmPreferences.AutoGroqComplexUpgrade = false;
                return Task.FromResult<string?>($"단일 제공자를 Copilot로 바꿨습니다. 현재 모델: {_telegramLlmPreferences.SingleModel}");
            }

            if (key == "cerebras")
            {
                _telegramLlmPreferences.SingleProvider = "cerebras";
                _telegramLlmPreferences.SingleModel = _config.CerebrasModel;
                _telegramLlmPreferences.AutoGroqComplexUpgrade = false;
                return Task.FromResult<string?>($"단일 제공자를 Cerebras로 바꿨습니다. 현재 모델: {_telegramLlmPreferences.SingleModel}");
            }

            if (key == "codex")
            {
                _telegramLlmPreferences.SingleProvider = "codex";
                _telegramLlmPreferences.SingleModel = _config.CodexModel;
                _telegramLlmPreferences.AutoGroqComplexUpgrade = false;
                return Task.FromResult<string?>($"단일 제공자를 Codex로 바꿨습니다. 현재 모델: {_telegramLlmPreferences.SingleModel}");
            }
        }

        return Task.FromResult<string?>("사용법: /model <groq|gemini|copilot|cerebras|codex>");
    }

    private async Task<string?> TryHandleTelegramNaturalControlCommandAsync(
        string text,
        IReadOnlyList<InputAttachment>? attachments,
        IReadOnlyList<string>? webUrls,
        bool webSearchEnabled,
        CancellationToken cancellationToken
    )
    {
        var normalized = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var lowered = normalized.ToLowerInvariant();
        var pseudoCommand = TryBuildTelegramNaturalPseudoCommand(normalized, lowered);
        if (!string.IsNullOrWhiteSpace(pseudoCommand))
        {
            var pseudoResult = await ExecuteTelegramPseudoCommandAsync(
                pseudoCommand,
                attachments,
                webUrls,
                webSearchEnabled,
                cancellationToken
            );
            if (!string.IsNullOrWhiteSpace(pseudoResult))
            {
                return pseudoResult;
            }
        }

        if (ContainsAny(lowered, "모델 목록", "모델 보여", "모델 리스트"))
        {
            var target = ContainsAny(lowered, "groq", "그록")
                ? "groq"
                : ContainsAny(lowered, "gemini", "제미니")
                    ? "gemini"
                : ContainsAny(lowered, "copilot", "코파일럿")
                    ? "copilot"
                    : ContainsAny(lowered, "cerebras", "세레브라스", "세레브라")
                        ? "cerebras"
                        : ContainsAny(lowered, "codex", "코덱스")
                            ? "codex"
                        : "all";
            return await BuildTelegramModelsReportAsync(target, cancellationToken);
        }

        if (ContainsAny(lowered, "사용량", "과금", "quota", "한도", "토큰 잔여", "요청 잔여"))
        {
            return await BuildTelegramUsageReportAsync(cancellationToken);
        }

        var helpTopic = ExtractHelpTopicFromNaturalText(lowered);
        if (helpTopic != null)
        {
            return BuildTelegramHelpText(helpTopic);
        }

        var setProviderModel = Regex.Match(normalized, @"(?i)(groq|그록|gemini|제미니|copilot|코파일럿|cerebras|세레브라스|세레브라|codex|코덱스)\s*모델\s*([a-zA-Z0-9._/\-]+)\s*(?:로|으로)?\s*(?:바꿔|변경|설정)");
        if (setProviderModel.Success)
        {
            var provider = ExtractProviderAliasFromNaturalText(setProviderModel.Groups[1].Value, allowAuto: false);
            var modelId = setProviderModel.Groups[2].Value.Trim();
            if (!string.IsNullOrWhiteSpace(provider) && !string.IsNullOrWhiteSpace(modelId))
            {
                if (provider == "groq")
                {
                    return await SetGroqModelForTelegramAsync(modelId, cancellationToken);
                }

                if (provider == "copilot")
                {
                    return await SetCopilotModelForTelegramAsync(modelId, cancellationToken);
                }

                var providerMessage = SetChannelProvider("telegram", "single", provider);
                var modelMessage = SetChannelModel("telegram", "single", modelId);
                return providerMessage.Contains("실패", StringComparison.OrdinalIgnoreCase)
                    ? providerMessage
                    : modelMessage;
            }
        }

        var setGroq = Regex.Match(normalized, @"(?i)groq\s*모델\s*([a-zA-Z0-9._/\-]+)\s*(?:로|으로)?\s*(?:바꿔|변경|설정)");
        if (setGroq.Success)
        {
            return await SetGroqModelForTelegramAsync(setGroq.Groups[1].Value, cancellationToken);
        }

        var setCopilot = Regex.Match(normalized, @"(?i)(?:copilot|코파일럿)\s*모델\s*([a-zA-Z0-9._/\-]+)\s*(?:로|으로)?\s*(?:바꿔|변경|설정)");
        if (setCopilot.Success)
        {
            return await SetCopilotModelForTelegramAsync(setCopilot.Groups[1].Value, cancellationToken);
        }

        return null;
    }

    private async Task<string?> ExecuteTelegramPseudoCommandAsync(
        string pseudoCommand,
        IReadOnlyList<InputAttachment>? attachments,
        IReadOnlyList<string>? webUrls,
        bool webSearchEnabled,
        CancellationToken cancellationToken
    )
    {
        var command = (pseudoCommand ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        if (command.StartsWith("/help", StringComparison.OrdinalIgnoreCase) || command.Equals("/start", StringComparison.OrdinalIgnoreCase))
        {
            return BuildTelegramHelpText(ParseHelpTopicFromInput(command));
        }

        if (command.StartsWith("/talk", StringComparison.OrdinalIgnoreCase) || command.StartsWith("/code", StringComparison.OrdinalIgnoreCase))
        {
            return await TryHandleTelegramProfileCommandAsync(command, cancellationToken);
        }

        if (command.StartsWith("/model", StringComparison.OrdinalIgnoreCase))
        {
            return await TryHandleTelegramQuickModelCommandAsync(command, cancellationToken);
        }

        if (command.StartsWith("/llm", StringComparison.OrdinalIgnoreCase))
        {
            return await TryHandleTelegramLlmControlCommandAsync(command, cancellationToken);
        }

        if (command.StartsWith("/skill", StringComparison.OrdinalIgnoreCase)
            || command.StartsWith("/skills", StringComparison.OrdinalIgnoreCase))
        {
            return await TryHandleTelegramSkillCommandAsync(command, cancellationToken);
        }

        if (command.StartsWith("/coding", StringComparison.OrdinalIgnoreCase))
        {
            return await TryHandleTelegramCodingCommandAsync(command, attachments, webUrls, webSearchEnabled, cancellationToken);
        }

        if (command.StartsWith("/refactor", StringComparison.OrdinalIgnoreCase))
        {
            return await TryHandleTelegramRefactorCommandAsync(command, cancellationToken);
        }

        if (command.StartsWith("/memory", StringComparison.OrdinalIgnoreCase))
        {
            return await TryHandleTelegramMemoryCommandAsync(command, cancellationToken);
        }

        if (command.StartsWith("/doctor", StringComparison.OrdinalIgnoreCase))
        {
            return await TryHandleTelegramDoctorCommandAsync(command, cancellationToken);
        }

        if (command.StartsWith("/plan", StringComparison.OrdinalIgnoreCase))
        {
            return await TryHandleTelegramPlanCommandAsync(command, cancellationToken);
        }

        if (command.StartsWith("/task", StringComparison.OrdinalIgnoreCase))
        {
            return await TryHandleTelegramTaskCommandAsync(command, cancellationToken);
        }

        if (command.StartsWith("/notebook", StringComparison.OrdinalIgnoreCase)
            || command.StartsWith("/handoff", StringComparison.OrdinalIgnoreCase))
        {
            return await TryHandleTelegramNotebookCommandAsync(command, cancellationToken);
        }

        if (command.StartsWith("/routine", StringComparison.OrdinalIgnoreCase)
            || command.StartsWith("/routines", StringComparison.OrdinalIgnoreCase))
        {
            return await TryHandleRoutineCommandAsync(command, "telegram", cancellationToken);
        }

        if (command.Equals("/metrics", StringComparison.OrdinalIgnoreCase))
        {
            var metrics = await _coreClient.GetMetricsAsync(cancellationToken);
            RecordEvent($"telegram:natural:{command}");
            _auditLogger.Log("telegram", "metrics", "ok", "natural_control");
            return metrics;
        }

        if (TryParseKillCommand(command, out var pid))
        {
            var guard = await ValidateKillTargetAsync(pid, "telegram", cancellationToken);
            if (!guard.Allowed)
            {
                _auditLogger.Log("telegram", "kill", "deny", $"pid={pid} reason={guard.Reason} natural_control");
                return $"kill denied: {guard.Reason}";
            }

            var result = await _coreClient.KillAsync(pid, cancellationToken);
            RecordEvent($"telegram:natural:{command}");
            _auditLogger.Log("telegram", "kill", "ok", $"pid={pid} natural_control");
            return result;
        }

        return null;
    }

    private static string? TryBuildTelegramNaturalPseudoCommand(string normalized, string lowered)
    {
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var commandLike = Regex.Match(normalized, @"(?i)^(help|start|talk|code|model|llm|skill|skills|coding|refactor|memory|doctor|plan|task|notebook|handoff|routine|routines|metrics|kill)\b(.*)$");
        if (commandLike.Success)
        {
            var head = commandLike.Groups[1].Value.ToLowerInvariant();
            var tail = commandLike.Groups[2].Value;
            return "/" + head + tail;
        }

        if (ContainsAny(lowered, "대화 프리셋", "대화 프로필", "talk 모드", "대화 탭 환경"))
        {
            var thinking = ExtractThinkingLevelFromNaturalText(lowered);
            return thinking == null ? "/talk" : $"/talk {thinking}";
        }

        if (ContainsAny(lowered, "코딩 프리셋", "코딩 프로필", "code 모드", "코딩 탭 환경"))
        {
            var thinking = ExtractThinkingLevelFromNaturalText(lowered);
            return thinking == null ? "/code" : $"/code {thinking}";
        }

        if (ContainsAny(lowered, "llm 단일 모드", "단일 모드로", "single 모드", "single mode"))
        {
            return "/llm mode single";
        }

        if (ContainsAny(lowered, "llm 오케스트레이션 모드", "오케스트레이션 모드로", "orchestration mode", "orchestration 모드"))
        {
            return "/llm mode orchestration";
        }

        if (ContainsAny(lowered, "llm 다중 모드", "다중 모드로", "멀티 모드로", "multi mode", "multi 모드"))
        {
            return "/llm mode multi";
        }

        if (ContainsAny(lowered, "최근 코딩 결과", "마지막 코딩 결과", "코딩 결과 보여", "coding result"))
        {
            return "/coding last";
        }

        if (ContainsAny(lowered, "코딩 상태", "코딩 설정", "coding status"))
        {
            return "/coding status";
        }

        if (ContainsAny(lowered, "코딩 파일 목록", "최근 코딩 파일", "coding files"))
        {
            return "/coding files";
        }

        var codingFilePreview = Regex.Match(normalized, @"(?is)(?:코딩 파일|coding file)\s*(?:보여|열어|preview)?\s*[:：]?\s*(.+)$");
        if (codingFilePreview.Success)
        {
            var query = codingFilePreview.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                return $"/coding file {query}";
            }
        }

        var codingSingleRun = Regex.Match(normalized, @"(?is)(?:단일|single)\s*코딩(?:으로)?\s*[:：]?\s*(.+)$");
        if (codingSingleRun.Success)
        {
            var request = codingSingleRun.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(request))
            {
                return $"/coding single run {request}";
            }
        }

        var codingOrchRun = Regex.Match(normalized, @"(?is)(?:오케스트레이션|orchestration)\s*코딩(?:으로)?\s*[:：]?\s*(.*)$");
        if (codingOrchRun.Success)
        {
            var request = codingOrchRun.Groups[1].Value.Trim();
            return string.IsNullOrWhiteSpace(request)
                ? "/coding orchestration run"
                : $"/coding orchestration run {request}";
        }

        var codingMultiRun = Regex.Match(normalized, @"(?is)(?:다중|multi)\s*코딩(?:으로)?\s*[:：]?\s*(.+)$");
        if (codingMultiRun.Success)
        {
            var request = codingMultiRun.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(request))
            {
                return $"/coding multi run {request}";
            }
        }

        var codingRun = Regex.Match(normalized, @"(?is)(?:코딩|coding)\s*(?:실행|run)\s*[:：]?\s*(.*)$");
        if (codingRun.Success)
        {
            var request = codingRun.Groups[1].Value.Trim();
            return string.IsNullOrWhiteSpace(request)
                ? "/coding run"
                : $"/coding run {request}";
        }

        var codingLanguage = Regex.Match(normalized, @"(?is)(?:(단일|single|오케스트레이션|orchestration|다중|multi)\s*)?(?:코딩|coding)\s*(?:언어|language)\s*(?:를)?\s*([a-zA-Z0-9#+._-]+)\s*(?:로|으로)?\s*(?:바꿔|변경|설정)");
        if (codingLanguage.Success)
        {
            var mode = codingLanguage.Groups[1].Value.Trim().ToLowerInvariant() switch
            {
                "단일" => "single",
                "single" => "single",
                "오케스트레이션" => "orchestration",
                "orchestration" => "orchestration",
                "다중" => "multi",
                "multi" => "multi",
                _ => string.Empty
            };
            var language = codingLanguage.Groups[2].Value.Trim();
            return string.IsNullOrWhiteSpace(mode)
                ? $"/coding language {language}"
                : $"/coding language {mode} {language}";
        }

        var codingMode = Regex.Match(normalized, @"(?is)(?:코딩|coding)\s*모드.*?(단일|single|오케스트레이션|orchestration|다중|multi).*(?:바꿔|변경|설정)");
        if (codingMode.Success)
        {
            var mode = codingMode.Groups[1].Value.Trim().ToLowerInvariant() switch
            {
                "단일" => "single",
                "single" => "single",
                "오케스트레이션" => "orchestration",
                "orchestration" => "orchestration",
                "다중" => "multi",
                "multi" => "multi",
                _ => string.Empty
            };
            if (!string.IsNullOrWhiteSpace(mode))
            {
                return $"/coding mode {mode}";
            }
        }

        var codingProvider = Regex.Match(
            normalized,
            @"(?is)(단일|single|오케스트레이션|orchestration|다중|multi)\s*코딩\s*(?:요약\s*)?(?:제공자|provider)\s*(?:를|을)?\s*(auto|자동|groq|그록|gemini|제미니|copilot|코파일럿|cerebras|세레브라스|세레브라|codex|코덱스)\s*(?:로|으로)?\s*(?:바꿔|변경|설정)"
        );
        if (codingProvider.Success)
        {
            var mode = codingProvider.Groups[1].Value.Trim().ToLowerInvariant() switch
            {
                "단일" => "single",
                "single" => "single",
                "오케스트레이션" => "orchestration",
                "orchestration" => "orchestration",
                "다중" => "multi",
                "multi" => "multi",
                _ => string.Empty
            };
            var provider = ExtractProviderAliasFromNaturalText(codingProvider.Groups[2].Value, allowAuto: true);
            if (!string.IsNullOrWhiteSpace(mode) && !string.IsNullOrWhiteSpace(provider))
            {
                return $"/coding {mode} provider {provider}";
            }
        }

        var codingModel = Regex.Match(
            normalized,
            @"(?is)(단일|single|오케스트레이션|orchestration|다중|multi)\s*코딩\s*(?:모델|model)\s*(?:을|를)?\s*([a-zA-Z0-9._/\-]+)\s*(?:로|으로)?\s*(?:바꿔|변경|설정)"
        );
        if (codingModel.Success)
        {
            var mode = codingModel.Groups[1].Value.Trim().ToLowerInvariant() switch
            {
                "단일" => "single",
                "single" => "single",
                "오케스트레이션" => "orchestration",
                "orchestration" => "orchestration",
                "다중" => "multi",
                "multi" => "multi",
                _ => string.Empty
            };
            var modelId = codingModel.Groups[2].Value.Trim();
            if (!string.IsNullOrWhiteSpace(mode) && !string.IsNullOrWhiteSpace(modelId))
            {
                return $"/coding {mode} model {modelId}";
            }
        }

        var codingWorker = Regex.Match(
            normalized,
            @"(?is)(오케스트레이션|orchestration|다중|multi)\s*코딩\s*(?:워커|worker)\s*(groq|그록|gemini|제미니|copilot|코파일럿|cerebras|세레브라스|세레브라|codex|코덱스)\s*(?:모델|model)?\s*(?:을|를)?\s*([a-zA-Z0-9._/\-]+|none|없음|선택안함|선택 안함)\s*(?:로|으로)?\s*(?:바꿔|변경|설정)"
        );
        if (codingWorker.Success)
        {
            var mode = codingWorker.Groups[1].Value.Trim().ToLowerInvariant() switch
            {
                "오케스트레이션" => "orchestration",
                "orchestration" => "orchestration",
                "다중" => "multi",
                "multi" => "multi",
                _ => string.Empty
            };
            var provider = ExtractProviderAliasFromNaturalText(codingWorker.Groups[2].Value, allowAuto: false);
            var workerModel = codingWorker.Groups[3].Value.Trim();
            if (workerModel.Equals("없음", StringComparison.OrdinalIgnoreCase)
                || workerModel.Equals("선택안함", StringComparison.OrdinalIgnoreCase)
                || workerModel.Equals("선택 안함", StringComparison.OrdinalIgnoreCase))
            {
                workerModel = "none";
            }

            if (!string.IsNullOrWhiteSpace(mode)
                && !string.IsNullOrWhiteSpace(provider)
                && !string.IsNullOrWhiteSpace(workerModel))
            {
                return $"/coding {mode} worker {provider} {workerModel}";
            }
        }

        if (ContainsAny(lowered, "safe refactor 상태", "refactor 상태", "리팩터 상태"))
        {
            return "/refactor status";
        }

        if (ContainsAny(lowered, "safe refactor 적용", "refactor 적용", "리팩터 적용"))
        {
            return "/refactor apply";
        }

        var refactorRead = Regex.Match(normalized, @"(?is)(?:safe refactor|refactor|리팩터)\s*(?:읽기|read)\s*([^\s]+)(?:\s+([0-9]+))?(?:\s+([0-9]+))?$");
        if (refactorRead.Success)
        {
            var path = refactorRead.Groups[1].Value.Trim();
            var start = refactorRead.Groups[2].Value.Trim();
            var end = refactorRead.Groups[3].Value.Trim();
            if (!string.IsNullOrWhiteSpace(path))
            {
                var tail = string.IsNullOrWhiteSpace(start)
                    ? path
                    : string.IsNullOrWhiteSpace(end)
                        ? $"{path} {start}"
                        : $"{path} {start} {end}";
                return $"/refactor read {tail}";
            }
        }

        if (ContainsAny(lowered, "메모리 초기화", "메모리 비우기", "메모리 삭제", "메모리 지워"))
        {
            return "/memory clear";
        }

        if (ContainsAny(lowered, "메모리 노트", "메모리 저장", "메모리 생성", "메모리 만들어"))
        {
            return ContainsAny(lowered, "compact", "압축", "짧게")
                ? "/memory create compact"
                : "/memory create";
        }

        if (ContainsAny(lowered, "doctor 실행", "doctor 결과", "doctor 보여", "환경 진단", "상태 점검", "시스템 점검", "진단 실행", "최근 진단", "마지막 진단"))
        {
            var parts = new List<string> { "/doctor" };
            if (ContainsAny(lowered, "last", "latest", "최근", "마지막"))
            {
                parts.Add("last");
            }

            if (ContainsAny(lowered, "json"))
            {
                parts.Add("json");
            }

            return string.Join(' ', parts);
        }

        if (ContainsAny(lowered, "단일 제공자", "single provider", "single 제공자", "단일 provider"))
        {
            var provider = ExtractProviderAliasFromNaturalText(lowered, allowAuto: false);
            if (!string.IsNullOrWhiteSpace(provider))
            {
                return $"/llm single provider {provider}";
            }
        }

        if (ContainsAny(lowered, "오케스트레이션 제공자", "orchestration provider", "집계 제공자", "집계 provider"))
        {
            var provider = ExtractProviderAliasFromNaturalText(lowered, allowAuto: true);
            if (!string.IsNullOrWhiteSpace(provider))
            {
                return $"/llm orchestration provider {provider}";
            }
        }

        if (ContainsAny(lowered, "다중 요약 제공자", "multi summary provider", "요약 제공자", "summary provider"))
        {
            var provider = ExtractProviderAliasFromNaturalText(lowered, allowAuto: true);
            if (!string.IsNullOrWhiteSpace(provider))
            {
                return $"/llm multi summary {provider}";
            }
        }

        var singleProviderSwitch = Regex.Match(lowered, @"(?:단일|single).*(groq|그록|gemini|제미니|copilot|코파일럿|cerebras|세레브라스|세레브라|codex|코덱스).*(바꿔|변경|설정)");
        if (singleProviderSwitch.Success)
        {
            var provider = ExtractProviderAliasFromNaturalText(singleProviderSwitch.Groups[1].Value, allowAuto: false);
            if (!string.IsNullOrWhiteSpace(provider))
            {
                return $"/llm single provider {provider}";
            }
        }

        var orchestrationProviderSwitch = Regex.Match(lowered, @"(?:오케스트레이션|orchestration|집계).*(auto|자동|groq|그록|gemini|제미니|copilot|코파일럿|cerebras|세레브라스|세레브라|codex|코덱스).*(바꿔|변경|설정)");
        if (orchestrationProviderSwitch.Success)
        {
            var provider = ExtractProviderAliasFromNaturalText(orchestrationProviderSwitch.Groups[1].Value, allowAuto: true);
            if (!string.IsNullOrWhiteSpace(provider))
            {
                return $"/llm orchestration provider {provider}";
            }
        }

        var quickProviderSwitch = Regex.Match(lowered, @"(groq|그록|gemini|제미니|copilot|코파일럿|cerebras|세레브라스|세레브라|codex|코덱스).*(바꿔|변경|설정)");
        if (quickProviderSwitch.Success
            && !ContainsAny(lowered, "단일", "single", "오케스트레이션", "요약", "summary", "다중", "multi"))
        {
            var provider = ExtractProviderAliasFromNaturalText(quickProviderSwitch.Groups[1].Value, allowAuto: false);
            if (!string.IsNullOrWhiteSpace(provider))
            {
                return $"/model {provider}";
            }
        }

        var singleModel = Regex.Match(normalized, @"(?i)(?:단일|single)\s*(?:모델|model)\s*([a-zA-Z0-9._/\-]+)");
        if (singleModel.Success)
        {
            return $"/llm single model {singleModel.Groups[1].Value}";
        }

        var orchestrationModel = Regex.Match(normalized, @"(?i)(?:오케스트레이션|orchestration)\s*(?:모델|model)\s*([a-zA-Z0-9._/\-]+)");
        if (orchestrationModel.Success)
        {
            return $"/llm orchestration model {orchestrationModel.Groups[1].Value}";
        }

        var multiGroqModel = Regex.Match(normalized, @"(?i)(?:다중|multi)\s*groq\s*(?:모델|model)\s*([a-zA-Z0-9._/\-]+)");
        if (multiGroqModel.Success)
        {
            return $"/llm multi groq {multiGroqModel.Groups[1].Value}";
        }

        var multiGeminiModel = Regex.Match(normalized, @"(?i)(?:다중|multi)\s*(?:gemini|제미니)\s*(?:모델|model)\s*([a-zA-Z0-9._/\-]+)");
        if (multiGeminiModel.Success)
        {
            return $"/llm multi gemini {multiGeminiModel.Groups[1].Value}";
        }

        var multiCopilotModel = Regex.Match(normalized, @"(?i)(?:다중|multi)\s*(?:copilot|코파일럿)\s*(?:모델|model)\s*([a-zA-Z0-9._/\-]+)");
        if (multiCopilotModel.Success)
        {
            return $"/llm multi copilot {multiCopilotModel.Groups[1].Value}";
        }

        var multiCerebrasModel = Regex.Match(normalized, @"(?i)(?:다중|multi)\s*(?:cerebras|세레브라스|세레브라)\s*(?:모델|model)\s*([a-zA-Z0-9._/\-]+)");
        if (multiCerebrasModel.Success)
        {
            return $"/llm multi cerebras {multiCerebrasModel.Groups[1].Value}";
        }

        var multiCodexModel = Regex.Match(normalized, @"(?i)(?:다중|multi)\s*(?:codex|코덱스)\s*(?:모델|model)\s*([a-zA-Z0-9._/\-]+)");
        if (multiCodexModel.Success)
        {
            return $"/llm multi codex {multiCodexModel.Groups[1].Value}";
        }

        if (ContainsAny(lowered, "계획 목록", "plan 목록", "플랜 목록"))
        {
            return "/plan list";
        }

        var planGet = Regex.Match(normalized, @"(?i)(?:계획|plan)\s*(?:상세|보기|get)\s*([a-z0-9_\-]+)");
        if (planGet.Success)
        {
            return $"/plan get {planGet.Groups[1].Value}";
        }

        var planReview = Regex.Match(normalized, @"(?i)(?:계획|plan)\s*(?:리뷰|검토|review)\s*([a-z0-9_\-]+)");
        if (planReview.Success)
        {
            return $"/plan review {planReview.Groups[1].Value}";
        }

        var planApprove = Regex.Match(normalized, @"(?i)(?:계획|plan)\s*(?:승인|approve)\s*([a-z0-9_\-]+)");
        if (planApprove.Success)
        {
            return $"/plan approve {planApprove.Groups[1].Value}";
        }

        var planRun = Regex.Match(normalized, @"(?i)(?:계획|plan)\s*(?:실행|run)\s*([a-z0-9_\-]+)");
        if (planRun.Success)
        {
            return $"/plan run {planRun.Groups[1].Value}";
        }

        var planCreate = Regex.Match(normalized, @"(?i)(?:계획|plan)\s*(?:생성|만들|추가)\s*[:：]?\s*(.+)$");
        if (planCreate.Success)
        {
            var request = planCreate.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(request))
            {
                return $"/plan create {request}";
            }
        }

        if (ContainsAny(lowered, "작업 목록", "task 목록", "태스크 목록"))
        {
            return "/task list";
        }

        var taskCreate = Regex.Match(normalized, @"(?i)(?:작업|task)\s*(?:생성|create)\s*([a-z0-9_\-]+)");
        if (taskCreate.Success)
        {
            return $"/task create {taskCreate.Groups[1].Value}";
        }

        var taskStatus = Regex.Match(normalized, @"(?i)(?:작업|task)\s*(?:상태|status|get)\s*([a-z0-9_\-]+)");
        if (taskStatus.Success)
        {
            return $"/task status {taskStatus.Groups[1].Value}";
        }

        var taskRun = Regex.Match(normalized, @"(?i)(?:작업|task)\s*(?:실행|run)\s*([a-z0-9_\-]+)");
        if (taskRun.Success)
        {
            return $"/task run {taskRun.Groups[1].Value}";
        }

        var taskCancel = Regex.Match(normalized, @"(?i)(?:작업|task)\s*(?:취소|중지|cancel)\s*([a-z0-9_\-]+)\s+([a-z0-9_\-]+)");
        if (taskCancel.Success)
        {
            return $"/task cancel {taskCancel.Groups[1].Value} {taskCancel.Groups[2].Value}";
        }

        var taskOutput = Regex.Match(normalized, @"(?i)(?:작업|task)\s*(?:결과|output)\s*([a-z0-9_\-]+)\s+([a-z0-9_\-]+)");
        if (taskOutput.Success)
        {
            return $"/task output {taskOutput.Groups[1].Value} {taskOutput.Groups[2].Value}";
        }

        if (ContainsAny(lowered, "노트북 보여", "노트북 열어", "notebook show"))
        {
            return "/notebook show";
        }

        var notebookAppend = Regex.Match(normalized, @"(?i)(?:노트북|notebook)\s*(?:append|추가|기록)\s*(learning|decision|verification|학습|결정|검증)\s*[:：]?\s*(.+)$");
        if (notebookAppend.Success)
        {
            var kind = notebookAppend.Groups[1].Value.ToLowerInvariant() switch
            {
                "학습" => "learning",
                "결정" => "decision",
                "검증" => "verification",
                _ => notebookAppend.Groups[1].Value.ToLowerInvariant()
            };
            var content = notebookAppend.Groups[2].Value.Trim();
            if (!string.IsNullOrWhiteSpace(content))
            {
                return $"/notebook append {kind} {content}";
            }
        }

        if (ContainsAny(lowered, "handoff", "인수인계"))
        {
            return "/handoff";
        }

        if (ContainsAny(lowered, "루틴 목록", "루틴 리스트", "routines 목록"))
        {
            return "/routine list";
        }

        var routineRuns = Regex.Match(normalized, @"(?is)루틴\s*(?:실행\s*이력|이력|runs)\s*([a-z0-9\-]+)");
        if (routineRuns.Success)
        {
            return $"/routine runs {routineRuns.Groups[1].Value}";
        }

        var routineDetail = Regex.Match(normalized, @"(?is)루틴\s*(?:상세|detail)\s*([a-z0-9\-]+)\s+([0-9]{6,})");
        if (routineDetail.Success)
        {
            return $"/routine detail {routineDetail.Groups[1].Value} {routineDetail.Groups[2].Value}";
        }

        var routineResend = Regex.Match(normalized, @"(?is)루틴\s*(?:재전송|텔레그램\s*재전송|resend)\s*([a-z0-9\-]+)\s+([0-9]{6,})");
        if (routineResend.Success)
        {
            return $"/routine resend {routineResend.Groups[1].Value} {routineResend.Groups[2].Value}";
        }

        var routineRun = Regex.Match(normalized, @"(?i)루틴\s*(?:즉시\s*)?(?:실행|run)\s*([a-z0-9\-]+)");
        if (routineRun.Success)
        {
            return $"/routine run {routineRun.Groups[1].Value}";
        }

        var routineOn = Regex.Match(normalized, @"(?i)루틴\s*(?:켜|활성화|on)\s*([a-z0-9\-]+)");
        if (routineOn.Success)
        {
            return $"/routine on {routineOn.Groups[1].Value}";
        }

        var routineOff = Regex.Match(normalized, @"(?i)루틴\s*(?:꺼|비활성화|off|중지)\s*([a-z0-9\-]+)");
        if (routineOff.Success)
        {
            return $"/routine off {routineOff.Groups[1].Value}";
        }

        var routineDelete = Regex.Match(normalized, @"(?i)루틴\s*(?:삭제|제거|지워)\s*([a-z0-9\-]+)");
        if (routineDelete.Success)
        {
            return $"/routine delete {routineDelete.Groups[1].Value}";
        }

        var routineCreate = Regex.Match(normalized, @"(?i)루틴\s*(?:생성|등록|추가)\s*[:：]?\s*(.+)$");
        if (routineCreate.Success)
        {
            var request = routineCreate.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(request))
            {
                return $"/routine create {request}";
            }
        }

        var routineUpdate = Regex.Match(normalized, @"(?is)루틴\s*(?:수정|업데이트|update)\s*([a-z0-9\-]+)\s*[:：]?\s*(.+)$");
        if (routineUpdate.Success)
        {
            var routineId = routineUpdate.Groups[1].Value.Trim();
            var request = routineUpdate.Groups[2].Value.Trim();
            if (!string.IsNullOrWhiteSpace(routineId) && !string.IsNullOrWhiteSpace(request))
            {
                return $"/routine update {routineId} {request}";
            }
        }

        if (ContainsAny(lowered, "메트릭 보여", "메트릭 조회", "시스템 메트릭", "metrics 보여"))
        {
            return "/metrics";
        }

        return null;
    }

    private async Task<string?> TryHandleTelegramMemoryCommandAsync(string text, CancellationToken cancellationToken)
    {
        if (!text.StartsWith("/memory", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length >= 2 && tokens[1].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            return BuildTelegramHelpText("memory");
        }

        if (tokens.Length >= 2 && tokens[1].Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            var result = ClearMemory("telegram", "telegram");
            return $"메모리를 비웠습니다. {result}";
        }

        if (tokens.Length >= 2 && tokens[1].Equals("create", StringComparison.OrdinalIgnoreCase))
        {
            var telegramThread = EnsureTelegramLinkedConversation();
            var compactConversation = tokens.Length >= 3 && tokens[2].Equals("compact", StringComparison.OrdinalIgnoreCase);
            var created = await CreateMemoryNoteAsync(
                telegramThread.Id,
                "telegram",
                compactConversation,
                cancellationToken
            );
            return created.Ok
                ? $"메모리 노트를 만들었습니다. {created.Message}"
                : $"메모리 노트 생성 실패: {created.Message}";
        }

        return BuildTelegramHelpText("memory");
    }

    private static string? ExtractProviderAliasFromNaturalText(string text, bool allowAuto)
    {
        var lowered = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(lowered))
        {
            return null;
        }

        if (ContainsAny(lowered, "groq", "그록"))
        {
            return "groq";
        }

        if (ContainsAny(lowered, "gemini", "제미니"))
        {
            return "gemini";
        }

        if (ContainsAny(lowered, "copilot", "코파일럿"))
        {
            return "copilot";
        }

        if (ContainsAny(lowered, "cerebras", "세레브라스", "세레브라"))
        {
            return "cerebras";
        }

        if (ContainsAny(lowered, "codex", "코덱스", "openai", "오픈ai", "오픈 ai"))
        {
            return "codex";
        }

        if (allowAuto && ContainsAny(lowered, "auto", "자동"))
        {
            return "auto";
        }

        return null;
    }

    private static string? ExtractThinkingLevelFromNaturalText(string lowered)
    {
        if (ContainsAny(lowered, "high", "정밀", "깊게", "신중", "정확도"))
        {
            return "high";
        }

        if (ContainsAny(lowered, "low", "빠르게", "간단", "짧게"))
        {
            return "low";
        }

        return null;
    }

    private static string? ExtractHelpTopicFromNaturalText(string lowered)
    {
        if (!ContainsAny(lowered, "도움말", "help", "명령어"))
        {
            return null;
        }

        if (ContainsAny(lowered, "llm", "모델"))
        {
            return "llm";
        }

        if (ContainsAny(lowered, "루틴", "routine"))
        {
            return "routine";
        }

        if (ContainsAny(lowered, "doctor", "진단", "점검"))
        {
            return "doctor";
        }

        if (ContainsAny(lowered, "plan", "계획", "기획"))
        {
            return "plan";
        }

        if (ContainsAny(lowered, "coding", "코딩"))
        {
            return "coding";
        }

        if (ContainsAny(lowered, "refactor", "safe refactor", "리팩터"))
        {
            return "refactor";
        }

        if (ContainsAny(lowered, "task", "작업", "태스크"))
        {
            return "task";
        }

        if (ContainsAny(lowered, "notebook", "노트북", "handoff", "인수인계"))
        {
            return "notebook";
        }

        if (ContainsAny(lowered, "memory", "메모리"))
        {
            return "memory";
        }

        if (ContainsAny(lowered, "자연어", "대화", "natural"))
        {
            return "natural";
        }

        return string.Empty;
    }

    private async Task<string> SetGroqModelForTelegramAsync(string modelId, CancellationToken cancellationToken)
    {
        var requested = (modelId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(requested))
        {
            return "model-id를 입력하세요. 예: /llm set groq meta-llama/llama-4-scout-17b-16e-instruct";
        }

        var models = await _groqModelCatalog.GetModelsAsync(cancellationToken);
        if (!models.Any(x => x.Id.Equals(requested, StringComparison.OrdinalIgnoreCase)))
        {
            return $"알 수 없는 Groq 모델: {requested}";
        }

        _llmRouter.TrySetSelectedGroqModel(requested);
        lock (_telegramLlmLock)
        {
            _telegramLlmPreferences.SingleProvider = "groq";
            _telegramLlmPreferences.SingleModel = requested;
            _telegramLlmPreferences.AutoGroqComplexUpgrade = requested.Equals(DefaultGroqFastModel, StringComparison.OrdinalIgnoreCase);
        }

        return $"Groq 모델을 {requested}로 바꿨습니다.";
    }

    private Task<string> SetCopilotModelForTelegramAsync(string modelId, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var requested = (modelId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(requested))
        {
            return Task.FromResult("model-id를 입력하세요. 예: /llm set copilot gpt-5-mini");
        }

        if (!_copilotWrapper.TrySetSelectedModel(DefaultCopilotModel))
        {
            return Task.FromResult($"Copilot 모델 설정 실패: {DefaultCopilotModel}");
        }

        lock (_telegramLlmLock)
        {
            _telegramLlmPreferences.SingleProvider = "copilot";
            _telegramLlmPreferences.SingleModel = DefaultCopilotModel;
        }

        if (!requested.Equals(DefaultCopilotModel, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult($"Copilot 모델은 {DefaultCopilotModel}로 고정됩니다. 요청한 `{requested}` 대신 {DefaultCopilotModel}를 사용합니다.");
        }

        return Task.FromResult($"Copilot 모델을 {DefaultCopilotModel}로 설정했습니다.");
    }

    private async Task<string> BuildTelegramModelsReportAsync(string target, CancellationToken cancellationToken)
    {
        var selected = (target ?? "all").Trim().ToLowerInvariant();
        TelegramLlmPreferences snapshot;
        lock (_telegramLlmLock)
        {
            snapshot = _telegramLlmPreferences.Clone();
        }

        var builder = new StringBuilder();
        var hasSection = false;
        builder.AppendLine("[로컬 시간]");
        builder.AppendLine(BuildLocalNowText());
        builder.AppendLine();
        if (selected == "all" || selected == "groq")
        {
            hasSection = true;
            var groqModels = await _groqModelCatalog.GetModelsAsync(cancellationToken);
            builder.AppendLine("[Groq 모델]");
            foreach (var model in groqModels.Take(16))
            {
                builder.AppendLine($"- {model.Id} | 속도={model.SpeedTokensPerSecond} tps | 컨텍스트={model.ContextWindow} | 출력={model.MaxCompletionTokens}");
            }
            if (groqModels.Count > 16)
            {
                builder.AppendLine($"... +{groqModels.Count - 16}개");
            }

            builder.AppendLine($"현재 단일 선택: {(snapshot.SingleProvider == "groq" ? snapshot.SingleModel : _llmRouter.GetSelectedGroqModel())}");
            builder.AppendLine($"현재 다중 선택: {snapshot.MultiGroqModel}");
            builder.AppendLine();
        }

        if (selected == "all" || selected == "gemini")
        {
            hasSection = true;
            builder.AppendLine("[Gemini 모델]");
            builder.AppendLine($"- 기본: {_config.GeminiModel}");
            builder.AppendLine($"- 현재 단일 선택: {(snapshot.SingleProvider == "gemini" ? snapshot.SingleModel : _config.GeminiModel)}");
            builder.AppendLine($"- 현재 다중 선택: {(string.IsNullOrWhiteSpace(snapshot.MultiGeminiModel) ? _config.GeminiModel : snapshot.MultiGeminiModel)}");
            builder.AppendLine("- 대표 지원: gemini-3-flash-preview");
            builder.AppendLine("- 대표 지원: gemini-3.1-flash-lite-preview");
            builder.AppendLine();
        }

        if (selected == "all" || selected == "copilot")
        {
            hasSection = true;
            var copilotModels = await _copilotWrapper.GetModelsAsync(cancellationToken);
            builder.AppendLine("[Copilot 모델]");
            foreach (var model in copilotModels.Take(16))
            {
                builder.AppendLine($"- {model.Id} | 공급자={model.Provider} | 속도={model.OutputTokensPerSecond} tps | 컨텍스트={model.ContextWindow}");
            }
            if (copilotModels.Count > 16)
            {
                builder.AppendLine($"... +{copilotModels.Count - 16}개");
            }

            builder.AppendLine($"현재 단일 선택: {(snapshot.SingleProvider == "copilot" ? snapshot.SingleModel : _copilotWrapper.GetSelectedModel())}");
            builder.AppendLine($"현재 다중 선택: {snapshot.MultiCopilotModel}");
            builder.AppendLine();
        }

        if (selected == "all" || selected == "cerebras")
        {
            hasSection = true;
            builder.AppendLine("[Cerebras 모델]");
            builder.AppendLine($"- 기본: {_config.CerebrasModel}");
            builder.AppendLine($"- 현재 단일 선택: {(snapshot.SingleProvider == "cerebras" ? snapshot.SingleModel : _config.CerebrasModel)}");
            builder.AppendLine($"- 현재 다중 선택: {snapshot.MultiCerebrasModel}");
            builder.AppendLine();
        }

        if (selected == "all" || selected == "codex")
        {
            hasSection = true;
            builder.AppendLine("[Codex 모델]");
            builder.AppendLine($"- 기본: {_config.CodexModel}");
            builder.AppendLine($"- 현재 단일 선택: {(snapshot.SingleProvider == "codex" ? snapshot.SingleModel : _config.CodexModel)}");
            builder.AppendLine($"- 현재 다중 선택: {snapshot.MultiCodexModel}");
            builder.AppendLine();
        }

        if (!hasSection)
        {
            return "사용법: /llm models [groq|gemini|copilot|cerebras|codex|all]";
        }

        builder.AppendLine("바꾸는 예시:");
        builder.AppendLine("/llm set groq meta-llama/llama-4-scout-17b-16e-instruct");
        builder.AppendLine("/llm set copilot gpt-5-mini");
        builder.AppendLine("/llm set codex gpt-5.4");
        builder.AppendLine("/llm single provider gemini");
        builder.AppendLine("/llm single model gemini-3.1-flash-lite-preview");
        builder.AppendLine("/llm single provider cerebras");
        builder.AppendLine("/llm single model zai-glm-4.7");
        builder.AppendLine("/llm multi gemini gemini-3.1-flash-lite-preview");
        builder.AppendLine("/llm multi cerebras zai-glm-4.7");
        builder.AppendLine("/llm multi codex gpt-5.4");
        return builder.ToString().Trim();
    }

    private async Task<string> BuildTelegramUsageReportAsync(CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var gemini = _llmRouter.GetGeminiUsageSnapshot();
        builder.AppendLine("[Gemini 사용량/추정 과금]");
        builder.AppendLine($"- requests={gemini.Requests}");
        builder.AppendLine($"- prompt_tokens={gemini.PromptTokens}, completion_tokens={gemini.CompletionTokens}, total_tokens={gemini.TotalTokens}");
        builder.AppendLine($"- input_price=${_config.GeminiInputPricePerMillionUsd:F4}/1M, output_price=${_config.GeminiOutputPricePerMillionUsd:F4}/1M");
        builder.AppendLine($"- estimated_cost_usd=${gemini.EstimatedCostUsd:F6}");
        builder.AppendLine();

        builder.AppendLine("[Copilot 사용량 - Omni-node 로컬]");
        builder.AppendLine($"- selected={_copilotWrapper.GetSelectedModel()}");
        var copilotUsage = _copilotWrapper.GetUsageSnapshot();
        var copilotLines = copilotUsage
            .OrderByDescending(x => x.Value.Requests)
            .Take(12)
            .Select(item => $"- {item.Key}: {item.Value.Requests} req")
            .ToArray();
        if (copilotLines.Length == 0)
        {
            builder.AppendLine("- usage 없음");
        }
        else
        {
            foreach (var line in copilotLines)
            {
                builder.AppendLine(line);
            }
        }
        builder.AppendLine();
        builder.AppendLine("[Copilot Premium Requests - GitHub 계정 월누적(모든 클라이언트 합산)]");
        var premium = await _copilotWrapper.GetPremiumUsageSnapshotAsync(cancellationToken, forceRefresh: true);
        if (!premium.Available)
        {
            builder.AppendLine($"- 상태={premium.Message}");
            if (premium.RequiresUserScope)
            {
                builder.AppendLine("- 조치=gh auth refresh -h github.com -s user");
            }
            builder.AppendLine($"- 확인 링크={premium.FeaturesUrl}");
            builder.AppendLine($"- 상세 링크={premium.BillingUrl}");
        }
        else
        {
            var quotaText = premium.MonthlyQuota > 0d
                ? premium.MonthlyQuota.ToString("F1", CultureInfo.InvariantCulture)
                : "-";
            builder.AppendLine($"- user={premium.Username}");
            builder.AppendLine($"- plan={premium.PlanName}");
            builder.AppendLine($"- used={premium.UsedRequests.ToString("F1", CultureInfo.InvariantCulture)}/{quotaText}");
            builder.AppendLine($"- percent={premium.PercentUsed.ToString("F1", CultureInfo.InvariantCulture)}%");
            builder.AppendLine($"- refreshed={premium.RefreshedLocal}");
            if (premium.Items.Count == 0)
            {
                builder.AppendLine("- 모델별 데이터 없음");
            }
            else
            {
                foreach (var item in premium.Items.Take(15))
                {
                    builder.AppendLine($"- {item.Model}: {item.Requests.ToString("F1", CultureInfo.InvariantCulture)} req ({item.Percent.ToString("F1", CultureInfo.InvariantCulture)}%)");
                }
            }
            builder.AppendLine($"- 확인 링크={premium.FeaturesUrl}");
            builder.AppendLine($"- 상세 링크={premium.BillingUrl}");
        }

        builder.AppendLine();
        builder.AppendLine("[Groq 제한량/사용량]");
        builder.AppendLine($"- selected={_llmRouter.GetSelectedGroqModel()}");
        var usageMap = _llmRouter.GetGroqUsageSnapshot();
        var rateMap = _llmRouter.GetGroqRateLimitSnapshot();
        var models = await _groqModelCatalog.GetModelsAsync(cancellationToken);
        foreach (var model in models.Take(12))
        {
            usageMap.TryGetValue(model.Id, out var usage);
            rateMap.TryGetValue(model.Id, out var rate);
            var usageText = $"{usage?.Requests ?? 0} req / {usage?.TotalTokens ?? 0} tok";
            var tokenLimitText = rate?.LimitTokens.HasValue == true
                ? $"{rate.RemainingTokens ?? 0}/{rate.LimitTokens.Value}"
                : "-";
            var reqLimitText = rate?.LimitRequests.HasValue == true
                ? $"{rate.RemainingRequests ?? 0}/{rate.LimitRequests.Value}"
                : "-";
            builder.AppendLine($"- {model.Id}: usage={usageText}, token 잔여/한도={tokenLimitText}, 요청 잔여/한도={reqLimitText}");
        }

        builder.AppendLine();
        builder.AppendLine("명령어:");
        builder.AppendLine("/llm models all");
        builder.AppendLine("/llm set groq <model-id>");
        builder.AppendLine("/llm set copilot <model-id>");
        return builder.ToString().Trim();
    }

    private async Task<string?> TryBuildInChatCopilotUsageResponseAsync(
        string input,
        string source,
        CancellationToken cancellationToken
    )
    {
        if (!source.Equals("web", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!IsCopilotPremiumUsageQuery(input))
        {
            return null;
        }

        var premium = await _copilotWrapper.GetPremiumUsageSnapshotAsync(cancellationToken, forceRefresh: true);
        var builder = new StringBuilder();
        builder.AppendLine("[Copilot Premium Requests - GitHub 계정 월누적(모든 클라이언트 합산)]");
        if (!premium.Available)
        {
            builder.AppendLine($"상태: {premium.Message}");
            if (premium.RequiresUserScope)
            {
                builder.AppendLine("조치: gh auth refresh -h github.com -s user");
            }
            builder.AppendLine($"확인 링크: {premium.FeaturesUrl}");
            builder.AppendLine($"상세 링크: {premium.BillingUrl}");
            return builder.ToString().Trim();
        }

        var quotaText = premium.MonthlyQuota > 0d
            ? premium.MonthlyQuota.ToString("F1", CultureInfo.InvariantCulture)
            : "-";
        builder.AppendLine($"계정: {premium.Username}");
        builder.AppendLine($"플랜: {premium.PlanName}");
        builder.AppendLine($"사용량: {premium.UsedRequests.ToString("F1", CultureInfo.InvariantCulture)}/{quotaText}");
        builder.AppendLine($"사용률: {premium.PercentUsed.ToString("F1", CultureInfo.InvariantCulture)}%");
        builder.AppendLine($"갱신 시각(로컬): {premium.RefreshedLocal}");
        builder.AppendLine();
        builder.AppendLine("[모델별 사용]");
        if (premium.Items.Count == 0)
        {
            builder.AppendLine("- 데이터 없음");
        }
        else
        {
            foreach (var item in premium.Items.Take(12))
            {
                builder.AppendLine($"- {item.Model}: {item.Requests.ToString("F1", CultureInfo.InvariantCulture)}회 ({item.Percent.ToString("F1", CultureInfo.InvariantCulture)}%)");
            }
        }

        builder.AppendLine();
        builder.AppendLine($"설정 페이지: {premium.FeaturesUrl}");
        builder.AppendLine($"청구 페이지: {premium.BillingUrl}");
        builder.AppendLine("주의: 위 Premium 수치는 GitHub 계정 월누적이며, Omni-node 외 VS Code/Web/기타 Copilot 사용도 함께 집계됩니다.");
        return builder.ToString().Trim();
    }

    private static bool IsCopilotPremiumUsageQuery(string text)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var lowered = normalized.ToLowerInvariant();
        if (lowered.StartsWith("/llm usage", StringComparison.OrdinalIgnoreCase)
            || lowered.StartsWith("/copilot usage", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!ContainsAny(lowered,
                "copilot",
                "코파일럿",
                "깃허브 코파일럿",
                "github copilot",
                "premium request",
                "프리미엄 요청"))
        {
            return false;
        }

        return ContainsAny(lowered,
            "usage",
            "사용량",
            "퍼센트",
            "percent",
            "비율",
            "quota",
            "한도",
            "모델별");
    }

    public TelegramExecutionMetadata GetCurrentTelegramExecutionMetadata()
    {
        return _telegramExecutionMetadata.Value ?? new TelegramExecutionMetadata();
    }

    private void SetCurrentTelegramExecutionMetadata(
        SearchAnswerGuardFailure? guardFailure = null,
        int retryAttempt = 0,
        int retryMaxAttempts = 0,
        string? retryStopReason = "-"
    )
    {
        _telegramExecutionMetadata.Value = new TelegramExecutionMetadata(
            guardFailure,
            Math.Max(0, retryAttempt),
            Math.Max(0, retryMaxAttempts),
            string.IsNullOrWhiteSpace(retryStopReason) ? "-" : retryStopReason.Trim()
        );
    }

    private async Task<string> ExecuteTelegramLlmMessageAsync(
        string text,
        IReadOnlyList<InputAttachment>? attachments,
        IReadOnlyList<string>? webUrls,
        bool webSearchEnabled,
        Action<string>? streamCallback,
        CancellationToken cancellationToken
    )
    {
        Action<ChatStreamUpdate>? telegramChatStream = streamCallback == null
            ? null
            : update =>
            {
                if (!string.IsNullOrEmpty(update.Delta))
                {
                    streamCallback(update.Delta);
                }
            };
        TelegramLlmPreferences snapshot;
        lock (_telegramLlmLock)
        {
            snapshot = _telegramLlmPreferences.Clone();
        }

        var telegramThread = EnsureTelegramLinkedConversation();
        var session = PrepareSessionContext(
            "chat",
            "single",
            telegramThread.Id,
            null,
            null,
            null,
            null,
            telegramThread.LinkedMemoryNotes,
            "telegram"
        );
        var snapshotSingleProvider = NormalizeProvider(snapshot.SingleProvider, allowAuto: true);
        if (snapshotSingleProvider is "auto" or "none")
        {
            snapshotSingleProvider = "gemini";
        }

        var snapshotSingleModel = ResolveModel(snapshotSingleProvider, snapshot.SingleModel);

        // Think+ 토글 키워드 감지 (입력 시작 부분)
        var rawIncomingText = text ?? string.Empty;
        var thinkPlusToggleNote = string.Empty;
        var hasActivationKeyword = LooksLikeThinkPlusActivation(rawIncomingText);
        var hasDeactivationKeyword = LooksLikeThinkPlusDeactivation(rawIncomingText);
        if (hasActivationKeyword && !hasDeactivationKeyword)
        {
            if (!IsThinkPlusActiveForThread(session.SessionId))
            {
                SetThinkPlusForThread(session.SessionId, true);
                thinkPlusToggleNote = "[추론 모드 활성화] 지금부터 모든 메시지에 대해 최신 웹 검색 결과를 참고해 답변합니다. 끄려면 \"추론 모드 꺼\"라고 말하세요.";
            }
        }
        else if (hasDeactivationKeyword)
        {
            if (IsThinkPlusActiveForThread(session.SessionId))
            {
                SetThinkPlusForThread(session.SessionId, false);
                thinkPlusToggleNote = "[추론 모드 비활성화] 일반 모드로 돌아갑니다.";
            }
        }

        // 토글 키워드를 입력에서 제거. 결합 메시지(토글 + 질문)인 경우 남은 질문으로 LLM 흐름 진행.
        if (!string.IsNullOrEmpty(thinkPlusToggleNote))
        {
            var stripped = ThinkPlusActivationRegex.Replace(rawIncomingText, " ");
            stripped = ThinkPlusDeactivationRegex.Replace(stripped, " ");
            stripped = Regex.Replace(stripped, @"\s+", " ").Trim();
            // 단독 토글 명령 (남은 텍스트 너무 짧음) → 즉시 안내 메시지만 반환
            if (stripped.Length < 6)
            {
                var note = thinkPlusToggleNote;
                _conversationStore.AppendMessage(session.Thread.Id, "user", text, "telegram:user");
                _conversationStore.AppendMessage(session.Thread.Id, "assistant", note, "telegram:think_plus_toggle");
                await EnsureConversationTitleFromFirstTurnAsync(session.Thread.Id, "system", "-", cancellationToken);
                SetCurrentTelegramExecutionMetadata(null, 0, 0, "-");
                _auditLogger.Log(
                    "telegram",
                    "think_plus_toggle",
                    IsThinkPlusActiveForThread(session.SessionId) ? "on" : "off",
                    $"thread={session.Thread.Id} bare_toggle=true"
                );
                return note;
            }
            // 결합 메시지: text 자체를 정리된 질문으로 덮어쓰고 정상 흐름 진행. 노트는 응답에 prepend.
            text = stripped;
        }

        var effectiveTopicInput = BuildTelegramFollowupAwareInput(telegramThread, text);
        var resolvedWebUrls = ResolveWebUrls(effectiveTopicInput, webUrls, webSearchEnabled);
        if (resolvedWebUrls.Count > 0 && snapshot.Mode == "single" && _llmRouter.HasGeminiApiKey())
        {
            var allowMarkdownTable = LooksLikeTableRenderRequest(effectiveTopicInput);
            var memoryHint = BuildSafeWebMemoryPreferenceHint(
                session.SessionId,
                effectiveTopicInput,
                session.LinkedMemoryNotes
            );
            var urlSingle = await GenerateGeminiUrlContextAnswerDetailedAsync(
                effectiveTopicInput,
                resolvedWebUrls,
                memoryHint,
                allowMarkdownTable,
                enforceTelegramOutputStyle: true,
                streamCallback: telegramChatStream,
                scope: session.Scope,
                mode: session.Mode,
                conversationId: session.Thread.Id,
                decisionPath: "heuristic_url_context",
                decisionMs: 0,
                cancellationToken
            );
            var urlResponseText = $"[Single {urlSingle.Response.Provider}:{urlSingle.Response.Model}]\n{FormatTelegramResponse(urlSingle.Response.Text, TelegramMaxResponseChars)}";
            var urlAssistantMeta = $"telegram-single:{urlSingle.Response.Provider}:{urlSingle.Response.Model}:gemini-url-single";
            _conversationStore.AppendMessage(session.Thread.Id, "user", text, "telegram:user");
            _conversationStore.AppendMessage(session.Thread.Id, "assistant", urlResponseText, urlAssistantMeta);
            await EnsureConversationTitleFromFirstTurnAsync(session.Thread.Id, urlSingle.Response.Provider, urlSingle.Response.Model, cancellationToken);
            _ = await MaybeCompressConversationAsync(session.Thread.Id, "chat-single", urlSingle.Response.Provider, urlSingle.Response.Model, cancellationToken);
            _auditLogger.Log("telegram", "telegram_guard_meta", "ok", $"route={urlAssistantMeta} guardCategory=- guardReason=- guardDetail=-");
            SetCurrentTelegramExecutionMetadata(null, 0, 0, "-");
            return urlResponseText;
        }

        var skillQueryText = text ?? string.Empty;
        var hasStickyActiveSkillForTelegram = !string.IsNullOrWhiteSpace(session.SessionId)
            && _activeSkillByThread.ContainsKey(session.SessionId);
        var thinkPlusActiveForTelegram = IsThinkPlusActiveForThread(session.SessionId);

        var isSkillContextQuery = LooksLikeProjectContextRequest(skillQueryText)
            || LooksLikeSkillCreationRequest(skillQueryText)
            || LooksLikeSkillDeactivationRequest(skillQueryText)
            || Regex.IsMatch(skillQueryText, @"(?i)(스킬|skill|skills|skill\.md).*(목록|리스트|뭐|보여|알려|어떤|종류|있어|있니|돼)")
            || hasStickyActiveSkillForTelegram;

        if (webSearchEnabled && snapshot.Mode == "single" && !isSkillContextQuery && !thinkPlusActiveForTelegram)
        {
            var decisionPath = "heuristic_no_web";
            var shouldUseGeminiWeb = false;
            var shouldFallbackToGeminiWeb = false;

            if (LooksLikeExplicitWebLookupQuestion(text))
            {
                decisionPath = "heuristic_explicit_web";
                shouldUseGeminiWeb = true;
            }
            else if (LooksLikeRealtimeQuestion(effectiveTopicInput))
            {
                decisionPath = "heuristic_web";
                shouldUseGeminiWeb = true;
            }
            else if (!LooksLikeClearlyNonWebQuestion(effectiveTopicInput))
            {
                var webDecision = await DecideNeedWebBySelectedProviderAsync(
                    effectiveTopicInput,
                    snapshotSingleProvider,
                    snapshotSingleModel,
                    cancellationToken
                );
                shouldFallbackToGeminiWeb = !webDecision.DecisionSucceeded && LooksLikeRealtimeQuestion(effectiveTopicInput);
                shouldUseGeminiWeb = webDecision.NeedWeb || shouldFallbackToGeminiWeb;
                decisionPath = webDecision.DecisionSucceeded ? "llm" : "heuristic_fallback";
            }

            if (shouldUseGeminiWeb)
            {
                var allowMarkdownTable = LooksLikeTableRenderRequest(effectiveTopicInput);
                var memoryHint = BuildSafeWebMemoryPreferenceHint(
                    session.SessionId,
                    effectiveTopicInput,
                    session.LinkedMemoryNotes
                );
                var webSingle = await ComposeGroundedWebAnswerWithFallbackAsync(
                    effectiveTopicInput,
                    memoryHint,
                    shouldFallbackToGeminiWeb,
                    allowMarkdownTable,
                    true,
                    telegramChatStream,
                    session.Scope,
                    session.Mode,
                    session.Thread.Id,
                    decisionPath,
                    0,
                    "telegram",
                    cancellationToken
                );
                var webResponseText = $"[Single {webSingle.Response.Provider}:{webSingle.Response.Model}]\n{FormatTelegramResponse(webSingle.Response.Text, TelegramMaxResponseChars)}";
                var webAssistantMeta = $"telegram-single:{webSingle.Response.Provider}:{webSingle.Response.Model}:{webSingle.Route}";
                _conversationStore.AppendMessage(session.Thread.Id, "user", text, "telegram:user");
                _conversationStore.AppendMessage(session.Thread.Id, "assistant", webResponseText, webAssistantMeta);
                await EnsureConversationTitleFromFirstTurnAsync(session.Thread.Id, webSingle.Response.Provider, webSingle.Response.Model, cancellationToken);
                _ = await MaybeCompressConversationAsync(session.Thread.Id, "chat-single", webSingle.Response.Provider, webSingle.Response.Model, cancellationToken);
                _auditLogger.Log("telegram", "telegram_guard_meta", "ok", $"route={webAssistantMeta} guardCategory=- guardReason=- guardDetail=-");
                SetCurrentTelegramExecutionMetadata(webSingle.GuardFailure, 0, 0, "-");
                return webResponseText;
            }
        }

        var effectiveWebSearchEnabled = snapshot.Mode == "single" ? false : webSearchEnabled;
        var normalizedAttachments = NormalizeAttachments(attachments);
        var sharedPrepared = await PrepareSharedInputAsync(
            effectiveTopicInput,
            normalizedAttachments,
            resolvedWebUrls,
            effectiveWebSearchEnabled,
            cancellationToken,
            "telegram",
            session.SessionKey,
            session.SessionId
        );
        if (!string.IsNullOrWhiteSpace(sharedPrepared.UnsupportedMessage))
        {
            var blockedAssistantMeta = "telegram-forced-context:unsupported";
            var blockedResponseText = sharedPrepared.UnsupportedMessage;
            _conversationStore.AppendMessage(session.Thread.Id, "user", text, "telegram:user");
            _conversationStore.AppendMessage(session.Thread.Id, "assistant", blockedResponseText, blockedAssistantMeta);
            await EnsureConversationTitleFromFirstTurnAsync(session.Thread.Id, "gemini", "-", cancellationToken);
            var guardCategory = NormalizeForcedGuardCategory(sharedPrepared.GuardFailure?.Category.ToString());
            var guardReason = NormalizeForcedGuardReason(sharedPrepared.GuardFailure?.ReasonCode);
            var guardDetail = NormalizeForcedToolValue(sharedPrepared.GuardFailure?.Detail, "-");
            _auditLogger.Log(
                "telegram",
                "telegram_guard_meta",
                sharedPrepared.GuardFailure is null ? "ok" : "blocked",
                $"route={NormalizeAuditToken(blockedAssistantMeta, "-")} guardCategory={guardCategory} guardReason={guardReason} guardDetail={guardDetail}"
            );
            SetCurrentTelegramExecutionMetadata(
                sharedPrepared.GuardFailure,
                sharedPrepared.RetryAttempt,
                sharedPrepared.RetryMaxAttempts,
                sharedPrepared.RetryStopReason
            );
            return blockedResponseText;
        }

        // 스킬·프로젝트 컨텍스트 또는 Think+ 가 들어간 입력은 압축 우회 (압축 시 컨텍스트가 사라짐).
        var preparedInput = (isSkillContextQuery || hasStickyActiveSkillForTelegram || thinkPlusActiveForTelegram)
            ? sharedPrepared.Text
            : await PrepareTelegramInputAsync(sharedPrepared.Text, cancellationToken);

        // Think+ 활성이면 sharedPrepared 앞에 web context prepend
        if (thinkPlusActiveForTelegram)
        {
            var thinkPlusContext = await BuildThinkPlusContextAsync(text ?? string.Empty, "telegram", cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(thinkPlusContext))
            {
                preparedInput = thinkPlusContext + preparedInput;
            }
        }

        var thinkingLevel = ResolveTelegramThinkingLevel(snapshot, text);
        var profiledInput = BuildTelegramProfilePrompt(preparedInput, snapshot.Profile, thinkingLevel);
        var contextualProfiledInput = BuildContextualInput(session.SessionId, profiledInput, session.LinkedMemoryNotes);

        string responseText;
        string providerForMemory;
        string modelForMemory;
        string assistantMeta;
        var effectiveGuardFailure = sharedPrepared.GuardFailure;

        void CaptureTelegramExecutionMeta()
        {
            SetCurrentTelegramExecutionMetadata(
                effectiveGuardFailure,
                sharedPrepared.RetryAttempt,
                sharedPrepared.RetryMaxAttempts,
                sharedPrepared.RetryStopReason
            );
        }

        void LogTelegramGuardMeta(string route)
        {
            var guardCategory = NormalizeForcedGuardCategory(effectiveGuardFailure?.Category.ToString());
            var guardReason = NormalizeForcedGuardReason(effectiveGuardFailure?.ReasonCode);
            var guardDetail = NormalizeForcedToolValue(effectiveGuardFailure?.Detail, "-");
            _auditLogger.Log(
                "telegram",
                "telegram_guard_meta",
                effectiveGuardFailure is null ? "ok" : "blocked",
                $"route={NormalizeAuditToken(route, "-")} guardCategory={guardCategory} guardReason={guardReason} guardDetail={guardDetail}"
            );
        }

        if (snapshot.Mode == "orchestration")
        {
            var orchestrated = await ChatOrchestrationAsync(
                contextualProfiledInput,
                "telegram",
                snapshot.OrchestrationProvider,
                snapshot.OrchestrationModel,
                null,
                null,
                null,
                null,
                null,
                normalizedAttachments,
                cancellationToken
            );
            var citationBundle = BuildAndLogCitationMappings(
                "telegram",
                "telegram-orchestration",
                sharedPrepared.Citations,
                ("text", orchestrated.Text)
            );
            effectiveGuardFailure = sharedPrepared.GuardFailure;
            var orchestratedValidated = ApplyListCountFallback(text, orchestrated.Text, sharedPrepared.Citations);
            orchestratedValidated = ApplySkillCreateDirective(orchestratedValidated, "telegram");
            orchestratedValidated = CleanLeakedSystemMarkers(orchestratedValidated);
            responseText = $"[{orchestrated.Route}]\n{FormatTelegramResponse(orchestratedValidated, TelegramMaxResponseChars)}";
            providerForMemory = NormalizeProvider(snapshot.OrchestrationProvider, allowAuto: true);
            if (providerForMemory is "auto" or "none")
            {
                providerForMemory = "gemini";
            }

            modelForMemory = string.IsNullOrWhiteSpace(snapshot.OrchestrationModel) ? "-" : snapshot.OrchestrationModel;
            assistantMeta = $"telegram-orchestration:{orchestrated.Route}";
            responseText = ApplyThinkPlusToggleNoteIfAny(thinkPlusToggleNote, responseText);
            _conversationStore.AppendMessage(session.Thread.Id, "user", text, "telegram:user");
            _conversationStore.AppendMessage(session.Thread.Id, "assistant", responseText, assistantMeta);
            await EnsureConversationTitleFromFirstTurnAsync(session.Thread.Id, providerForMemory, modelForMemory, cancellationToken);
            _ = await MaybeCompressConversationAsync(session.Thread.Id, "chat-single", providerForMemory, modelForMemory, cancellationToken);
            LogTelegramGuardMeta(assistantMeta);
            CaptureTelegramExecutionMeta();
            return responseText;
        }

        if (snapshot.Mode == "multi")
        {
            var multi = await ChatMultiAsync(
                contextualProfiledInput,
                "telegram",
                snapshot.MultiGroqModel,
                snapshot.MultiGeminiModel,
                snapshot.MultiCopilotModel,
                snapshot.MultiCerebrasModel,
                snapshot.MultiSummaryProvider,
                snapshot.MultiCodexModel,
                normalizedAttachments,
                cancellationToken
            );
            var citationBundle = BuildAndLogCitationMappings(
                "telegram",
                "telegram-multi",
                sharedPrepared.Citations,
                ("groq", multi.GroqText),
                ("gemini", multi.GeminiText),
                ("cerebras", multi.CerebrasText),
                ("copilot", multi.CopilotText),
                ("codex", multi.CodexText),
                ("summary", multi.Summary)
            );
            effectiveGuardFailure = sharedPrepared.GuardFailure;
            var multiSummaryValidated = ApplyListCountFallback(text, multi.Summary, sharedPrepared.Citations);
            multiSummaryValidated = ApplySkillCreateDirective(multiSummaryValidated, "telegram");
            multiSummaryValidated = CleanLeakedSystemMarkers(multiSummaryValidated);
            responseText = $"""
                           [Multi 요약]
                           {FormatTelegramResponse(multiSummaryValidated, TelegramMaxResponseChars)}
                           """;
            providerForMemory = NormalizeProvider(multi.ResolvedSummaryProvider, allowAuto: true);
            if (providerForMemory is "auto" or "none")
            {
                providerForMemory = "gemini";
            }

            modelForMemory = providerForMemory switch
            {
                "groq" => multi.GroqModel,
                "gemini" => multi.GeminiModel,
                "cerebras" => multi.CerebrasModel,
                "copilot" => multi.CopilotModel,
                "codex" => multi.CodexModel,
                _ => "-"
            };
            assistantMeta = $"telegram-multi:summary={multi.ResolvedSummaryProvider}";
            responseText = ApplyThinkPlusToggleNoteIfAny(thinkPlusToggleNote, responseText);
            _conversationStore.AppendMessage(session.Thread.Id, "user", text, "telegram:user");
            _conversationStore.AppendMessage(session.Thread.Id, "assistant", responseText, assistantMeta);
            await EnsureConversationTitleFromFirstTurnAsync(session.Thread.Id, providerForMemory, modelForMemory, cancellationToken);
            _ = await MaybeCompressConversationAsync(session.Thread.Id, "chat-single", providerForMemory, modelForMemory, cancellationToken);
            LogTelegramGuardMeta(assistantMeta);
            CaptureTelegramExecutionMeta();
            return responseText;
        }

        if (snapshot.SingleProvider == "groq")
        {
            var preferredModel = NormalizeModelSelection(snapshot.SingleModel)
                                 ?? NormalizeModelSelection(_config.GroqModel)
                                 ?? DefaultGroqPrimaryModel;
            var providerPrepared = await PrepareInputForProviderAsync(
                contextualProfiledInput,
                "groq",
                preferredModel,
                normalizedAttachments,
                webUrls,
                effectiveWebSearchEnabled,
                false,
                cancellationToken
            );
            if (!string.IsNullOrWhiteSpace(providerPrepared.UnsupportedMessage))
            {
                responseText = $"[Single groq:{preferredModel}]\n{providerPrepared.UnsupportedMessage}";
                responseText = ApplyThinkPlusToggleNoteIfAny(thinkPlusToggleNote, responseText);
                providerForMemory = "groq";
                modelForMemory = preferredModel;
                assistantMeta = $"telegram-single:groq:{preferredModel}:unsupported";
                _conversationStore.AppendMessage(session.Thread.Id, "user", text, "telegram:user");
                _conversationStore.AppendMessage(session.Thread.Id, "assistant", responseText, assistantMeta);
                await EnsureConversationTitleFromFirstTurnAsync(session.Thread.Id, providerForMemory, modelForMemory, cancellationToken);
                LogTelegramGuardMeta(assistantMeta);
                CaptureTelegramExecutionMeta();
                return responseText;
            }

            var singleGroq = await ExecuteTelegramGroqSingleAsync(
                text,
                providerPrepared.Text,
                snapshot,
                thinkingLevel,
                streamCallback,
                cancellationToken
            );
            if (!_config.EnableFastWebPipeline && ShouldRetrySingleChatWithoutHistory(text, singleGroq.Text))
            {
                var historyBypassInput = BuildHistoryBypassInput(providerPrepared.Text);
                var recovered = await ExecuteTelegramGroqSingleAsync(
                    text,
                    historyBypassInput,
                    snapshot,
                    thinkingLevel,
                    streamCallback,
                    cancellationToken
                );
                if (!string.IsNullOrWhiteSpace(recovered.Text)
                    && !ShouldRetrySingleChatWithoutHistory(text, recovered.Text))
                {
                    singleGroq = recovered;
                }
                else
                {
                    var originalRequestInput = BuildOriginalRequestRetryInput(text);
                    var originalRecovered = await ExecuteTelegramGroqSingleAsync(
                        text,
                        originalRequestInput,
                        snapshot,
                        thinkingLevel,
                        streamCallback,
                        cancellationToken
                    );
                    singleGroq = !string.IsNullOrWhiteSpace(originalRecovered.Text)
                                 && !ShouldRetrySingleChatWithoutHistory(text, originalRecovered.Text)
                        ? originalRecovered
                        : new LlmSingleChatResult(singleGroq.Provider, singleGroq.Model, BuildOffTopicGuardMessage(text));
                }
            }
            var citationBundle = BuildAndLogCitationMappings(
                "telegram",
                "telegram-single-groq",
                sharedPrepared.Citations,
                ("text", singleGroq.Text)
            );
            effectiveGuardFailure = sharedPrepared.GuardFailure;
            var singleGroqText = ApplySkillCreateDirective(singleGroq.Text, "telegram");
            singleGroqText = CleanLeakedSystemMarkers(singleGroqText);
            responseText = $"[Single {singleGroq.Provider}:{singleGroq.Model}]\n{FormatTelegramResponse(singleGroqText, TelegramMaxResponseChars)}";
            responseText = ApplyThinkPlusToggleNoteIfAny(thinkPlusToggleNote, responseText);
            providerForMemory = singleGroq.Provider;
            modelForMemory = singleGroq.Model;
            assistantMeta = $"telegram-single:{singleGroq.Provider}:{singleGroq.Model}";
            _conversationStore.AppendMessage(session.Thread.Id, "user", text, "telegram:user");
            _conversationStore.AppendMessage(session.Thread.Id, "assistant", responseText, assistantMeta);
            await EnsureConversationTitleFromFirstTurnAsync(session.Thread.Id, providerForMemory, modelForMemory, cancellationToken);
            _ = await MaybeCompressConversationAsync(session.Thread.Id, "chat-single", providerForMemory, modelForMemory, cancellationToken);
            LogTelegramGuardMeta(assistantMeta);
            CaptureTelegramExecutionMeta();
            return responseText;
        }

        var singleModel = ResolveModel(snapshot.SingleProvider, snapshot.SingleModel);
        var providerInput = await PrepareInputForProviderAsync(
            contextualProfiledInput,
            snapshot.SingleProvider,
            singleModel,
            normalizedAttachments,
            resolvedWebUrls,
            effectiveWebSearchEnabled,
            false,
            cancellationToken
        );
        if (!string.IsNullOrWhiteSpace(providerInput.UnsupportedMessage))
        {
            responseText = $"[Single {snapshot.SingleProvider}:{singleModel}]\n{providerInput.UnsupportedMessage}";
            responseText = ApplyThinkPlusToggleNoteIfAny(thinkPlusToggleNote, responseText);
            providerForMemory = snapshot.SingleProvider;
            modelForMemory = singleModel;
            assistantMeta = $"telegram-single:{snapshot.SingleProvider}:{singleModel}:unsupported";
            _conversationStore.AppendMessage(session.Thread.Id, "user", text, "telegram:user");
            _conversationStore.AppendMessage(session.Thread.Id, "assistant", responseText, assistantMeta);
            await EnsureConversationTitleFromFirstTurnAsync(session.Thread.Id, providerForMemory, modelForMemory, cancellationToken);
            LogTelegramGuardMeta(assistantMeta);
            CaptureTelegramExecutionMeta();
            return responseText;
        }

        var single = await ChatSingleAsync(
            providerInput.Text,
            snapshot.SingleProvider,
            snapshot.SingleModel,
            "telegram",
            cancellationToken,
            ResolveSingleChatMaxOutputTokens(text),
            streamCallback
        );
        if (!_config.EnableFastWebPipeline && ShouldRetrySingleChatWithoutHistory(text, single.Text))
        {
            var historyBypassInput = BuildHistoryBypassInput(providerInput.Text);
            var recovered = await ChatSingleAsync(
                historyBypassInput,
                snapshot.SingleProvider,
                snapshot.SingleModel,
                "telegram",
                cancellationToken,
                ResolveSingleChatMaxOutputTokens(text),
                streamCallback
            );
            if (!string.IsNullOrWhiteSpace(recovered.Text)
                && !ShouldRetrySingleChatWithoutHistory(text, recovered.Text))
            {
                single = recovered;
            }
            else
            {
                var originalRequestInput = BuildOriginalRequestRetryInput(text);
                var originalRecovered = await ChatSingleAsync(
                    originalRequestInput,
                    snapshot.SingleProvider,
                    snapshot.SingleModel,
                    "telegram",
                    cancellationToken,
                    ResolveSingleChatMaxOutputTokens(text),
                    streamCallback
                );
                single = !string.IsNullOrWhiteSpace(originalRecovered.Text)
                         && !ShouldRetrySingleChatWithoutHistory(text, originalRecovered.Text)
                    ? originalRecovered
                    : new LlmSingleChatResult(single.Provider, single.Model, BuildOffTopicGuardMessage(text));
            }
        }
        var singleCitationBundle = BuildAndLogCitationMappings(
            "telegram",
            "telegram-single",
            sharedPrepared.Citations,
            ("text", single.Text)
        );
        effectiveGuardFailure = sharedPrepared.GuardFailure;
        var singleText = ApplySkillCreateDirective(single.Text, "telegram");
        singleText = CleanLeakedSystemMarkers(singleText);
        responseText = $"[Single {single.Provider}:{single.Model}]\n{FormatTelegramResponse(singleText, TelegramMaxResponseChars)}";
        responseText = ApplyThinkPlusToggleNoteIfAny(thinkPlusToggleNote, responseText);
        providerForMemory = single.Provider;
        modelForMemory = single.Model;
        assistantMeta = $"telegram-single:{single.Provider}:{single.Model}";
        _conversationStore.AppendMessage(session.Thread.Id, "user", text, "telegram:user");
        _conversationStore.AppendMessage(session.Thread.Id, "assistant", responseText, assistantMeta);
        await EnsureConversationTitleFromFirstTurnAsync(session.Thread.Id, providerForMemory, modelForMemory, cancellationToken);
        _ = await MaybeCompressConversationAsync(session.Thread.Id, "chat-single", providerForMemory, modelForMemory, cancellationToken);
        LogTelegramGuardMeta(assistantMeta);
        CaptureTelegramExecutionMeta();
        return responseText;
    }

    private ConversationThreadView EnsureTelegramLinkedConversation()
    {
        var existing = _conversationStore
            .List("chat", "single")
            .FirstOrDefault(item => item.Tags.Any(tag =>
                string.Equals(tag, "telegram-link", StringComparison.OrdinalIgnoreCase)));
        if (existing != null)
        {
            return _conversationStore.Get(existing.Id)
                   ?? _conversationStore.Ensure("chat", "single", existing.Id, null, null, null, null);
        }

        return _conversationStore.Create(
            "chat",
            "single",
            "Telegram 연동 대화",
            "Telegram",
            "연동",
            new[] { "telegram-link", "shared" }
        );
    }

    private string BuildTelegramFollowupAwareInput(ConversationThreadView thread, string input)
    {
        var normalized = (input ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var anchor = FindTelegramAnchorUserMessage(thread, normalized);
        if (string.IsNullOrWhiteSpace(anchor))
        {
            return normalized;
        }

        if (LooksLikeExplicitWebLookupQuestion(normalized) && IsTelegramWeakFollowupInput(normalized))
        {
            return $"""
                    [직전 주제]
                    {anchor}

                    [현재 요청]
                    위 주제에 대해 웹에서 검색해서 근거 중심으로 다시 설명해줘.
                    사용자 추가 요청: {normalized}
                    """;
        }

        if (IsTelegramCorrectionFollowup(normalized))
        {
            return $"""
                    [직전 주제]
                    {anchor}

                    [정정 요청]
                    {normalized}

                    위 정정을 반영해서, 이전에 잘못 잡은 대상이 있으면 바로잡아 다시 답해줘.
                    """;
        }

        if (IsTelegramExhaustedFeedback(normalized))
        {
            var latestAssistant = thread.Messages
                .LastOrDefault(msg => msg.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
                ?.Text?.Trim();
            var assistantBlock = string.IsNullOrWhiteSpace(latestAssistant)
                ? string.Empty
                : $"""

                  [직전 답변 요약]
                  {latestAssistant}
                  """;
            return $"""
                    [직전 주제]
                    {anchor}{assistantBlock}

                    [사용자 추가 피드백]
                    {normalized}

                    사용자는 앞서 제안한 기본 조치를 이미 시도했다고 본다.
                    같은 해결책을 반복하지 말고, 남은 원인 후보와 확인 순서를 더 좁혀서 답해줘.
                    """;
        }

        return normalized;
    }

    private static string? FindTelegramAnchorUserMessage(ConversationThreadView thread, string currentInput)
    {
        if (thread.Messages.Count == 0)
        {
            return null;
        }

        foreach (var message in thread.Messages
                     .Where(msg => msg.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
                     .Reverse())
        {
            var text = (message.Text ?? string.Empty).Trim();
            if (text.Length == 0 || text.Equals(currentInput, StringComparison.Ordinal))
            {
                continue;
            }

            if (IsTelegramWeakFollowupInput(text)
                || IsTelegramCorrectionFollowup(text)
                || IsTelegramExhaustedFeedback(text))
            {
                continue;
            }

            return text;
        }

        return thread.Messages
            .Where(msg => msg.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
            .Select(msg => (msg.Text ?? string.Empty).Trim())
            .LastOrDefault(text => text.Length > 0 && !text.Equals(currentInput, StringComparison.Ordinal));
    }

    private static bool IsTelegramWeakFollowupInput(string input)
    {
        var normalized = Regex.Replace((input ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ");
        if (normalized.Length == 0)
        {
            return false;
        }

        if (normalized.Length <= 18
            && (LooksLikeExplicitWebLookupQuestion(normalized)
                || normalized is "그건?" or "이건?" or "왜?" or "진짜?" or "다시"))
        {
            return true;
        }

        return normalized is "검색해서 말해"
            or "검색해줘"
            or "검색해 줘"
            or "웹에서 찾아줘"
            or "웹에서 말해줘"
            or "그거 검색해줘"
            or "그거 검색해서 말해줘";
    }

    private static bool IsTelegramCorrectionFollowup(string input)
    {
        var normalized = Regex.Replace((input ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ");
        if (normalized.Length == 0)
        {
            return false;
        }

        return normalized.Contains("아니라", StringComparison.Ordinal)
               || normalized.Contains("말한게 아니라", StringComparison.Ordinal)
               || normalized.Contains("말한 게 아니라", StringComparison.Ordinal)
               || normalized.Contains("정정", StringComparison.Ordinal)
               || normalized.Contains("오타", StringComparison.Ordinal)
               || normalized.Contains("p2s 라고", StringComparison.Ordinal)
               || normalized.Contains("h2s 가 아니라", StringComparison.Ordinal);
    }

    private static bool IsTelegramExhaustedFeedback(string input)
    {
        var normalized = Regex.Replace((input ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ");
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsAny(
            normalized,
            "다했다고",
            "다 했다고",
            "다해도",
            "다 해도",
            "해봤는데",
            "해 봤는데",
            "이미 해봤",
            "이미 해 봤",
            "너가 말한거 다했다고",
            "너가 말한 거 다했다고",
            "다 했는데도",
            "했는데도"
        );
    }

    private async Task<LlmSingleChatResult> ExecuteTelegramGroqSingleAsync(
        string rawUserInput,
        string profiledInput,
        TelegramLlmPreferences snapshot,
        string thinkingLevel,
        Action<string>? streamCallback,
        CancellationToken cancellationToken
    )
    {
        _ = rawUserInput;
        _ = snapshot;

        var selectedModel = NormalizeModelSelection(snapshot.SingleModel)
            ?? (string.IsNullOrWhiteSpace(_config.GroqModel) ? DefaultGroqPrimaryModel : _config.GroqModel);
        var maxTokens = thinkingLevel == "high"
            ? TelegramComplexModeMaxOutputTokens
            : TelegramFastModeMaxOutputTokens;
        var generated = await ExecuteGroqSingleChainAsync(
            profiledInput,
            selectedModel,
            cancellationToken,
            maxTokens,
            streamCallback
        );
        return new LlmSingleChatResult(generated.Provider, generated.Model, SanitizeChatOutput(generated.Text));
    }

    private async Task<string> PrepareTelegramInputAsync(string input, CancellationToken cancellationToken)
    {
        var text = (input ?? string.Empty).Trim();
        if (text.Length <= TelegramLongContextThresholdChars)
        {
            return BuildTelegramConcisePrompt(text);
        }

        var compressionPrompt = BuildTelegramCompressionPrompt(text);
        string compressed;
        if (_llmRouter.HasGroqApiKey())
        {
            var groq = await GenerateByProviderSafeAsync(
                "groq",
                string.IsNullOrWhiteSpace(_config.GroqModel) ? DefaultGroqPrimaryModel : _config.GroqModel,
                compressionPrompt,
                cancellationToken,
                700
            );
            compressed = SanitizeChatOutput(groq.Text);
        }
        else if (_llmRouter.HasGeminiApiKey())
        {
            var gemini = await GenerateByProviderSafeAsync("gemini", _config.GeminiModel, compressionPrompt, cancellationToken, 700);
            compressed = SanitizeChatOutput(gemini.Text);
        }
        else
        {
            compressed = text.Length <= TelegramLongContextTargetChars
                ? text
                : text[..TelegramLongContextTargetChars] + "\n...(long_input_trimmed)";
        }

        if (string.IsNullOrWhiteSpace(compressed))
        {
            compressed = text.Length <= TelegramLongContextTargetChars
                ? text
                : text[..TelegramLongContextTargetChars] + "\n...(long_input_trimmed)";
        }

        return BuildTelegramConcisePrompt($"[긴 입력 자동 요약]\n{compressed}");
    }

    private static string BuildTelegramCompressionPrompt(string input)
    {
        return $"""
                아래 긴 입력을 핵심만 유지해 한국어로 압축 요약하세요.
                규칙:
                - 최대 8줄
                - 요구사항/제약/에러/결정포인트 보존
                - 불필요한 수식어 제거

                [원문]
                {input}
                """;
    }

    private static string BuildTelegramProfilePrompt(string concisePrompt, string profile, string thinkingLevel)
    {
        var modeGuide = profile switch
        {
            "code" => "코딩 모드: 변경 포인트, 실행/검증 명령, 실패 시 다음 조치까지 간결히 제시하세요.",
            "talk" => "대화 모드: 핵심 결론 중심으로 정리하세요.",
            _ => "기본 모드: 핵심만 간결하게 답하세요."
        };
        var thinkingGuide = thinkingLevel == "high"
            ? "사고 강도: high (정확성, 리스크, 예외 케이스를 우선)"
            : "사고 강도: low (빠르고 간결한 결론 우선)";
        var localNow = BuildLocalNowText();

        return $"""
                로컬 시간 기준:
                {localNow}

                {modeGuide}
                {thinkingGuide}

                {concisePrompt}
                """;
    }

    private static string ResolveTelegramThinkingLevel(TelegramLlmPreferences snapshot, string userText)
    {
        if (snapshot.Profile == "code")
        {
            return snapshot.CodeThinkingLevel == "high" ? "high" : "low";
        }

        if (snapshot.TalkThinkingLevel == "high")
        {
            return "high";
        }

        return IsDecisionOrRiskQuestion(userText) ? "high" : "low";
    }

    private static bool IsDecisionOrRiskQuestion(string input)
    {
        var normalized = (input ?? string.Empty).ToLowerInvariant();
        return ContainsAny(
            normalized,
            "비교",
            "결정",
            "결론",
            "추천",
            "정확",
            "리스크",
            "위험",
            "근거",
            "정책",
            "장단점",
            "tradeoff",
            "risk"
        );
    }

    private static bool UserRequiresConclusion(string input)
    {
        var normalized = (input ?? string.Empty).ToLowerInvariant();
        return ContainsAny(
            normalized,
            "결론",
            "결정",
            "확정",
            "하나만",
            "추천",
            "최종안",
            "choose",
            "final answer"
        );
    }

    private static bool ModelShowsUncertainty(string answer)
    {
        var normalized = (answer ?? string.Empty).ToLowerInvariant();
        return ContainsAny(
            normalized,
            "확실하지",
            "알 수 없",
            "근거 부족",
            "불확실",
            "모르겠",
            "추정",
            "insufficient",
            "uncertain",
            "not sure"
        );
    }

    private static string BuildTelegramConclusionEscalationPrompt(string contextualPrompt, string priorAnswer, string thinkingLevel)
    {
        var style = thinkingLevel == "high"
            ? "정확성과 리스크를 우선해 단일 결론을 제시하세요."
            : "간결하게 단일 결론을 제시하세요.";
        return $"""
                아래는 기존 답변입니다.
                이 답변의 불확실성을 줄이고 반드시 결론을 한 가지로 확정하세요.
                {style}
                출력 규칙:
                - 첫 줄에 결론 1문장
                - 이후 최대 5줄로 근거
                - 군더더기 금지

                [이전 답변]
                {priorAnswer}

                [원 질문]
                {contextualPrompt}
                """;
    }

    private static string BuildOrchestrationPrompt(
        string userText,
        IReadOnlyList<LlmSingleChatResult> workerResults,
        IReadOnlyDictionary<string, string> roleByProvider
    )
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("다음은 오케스트레이션 워커들이 역할을 나눠 처리한 결과입니다.");
        builder.AppendLine($"사용자 질문: {userText}");
        builder.AppendLine();
        builder.AppendLine("[역할 계획]");
        foreach (var item in workerResults)
        {
            var role = roleByProvider.TryGetValue(item.Provider, out var assignedRole)
                ? assignedRole
                : "보조 워커";
            builder.AppendLine($"- {item.Provider}:{item.Model} => {role}");
        }

        builder.AppendLine();
        builder.AppendLine("[역할별 결과]");
        foreach (var item in workerResults)
        {
            var role = roleByProvider.TryGetValue(item.Provider, out var assignedRole)
                ? assignedRole
                : "보조 워커";
            builder.AppendLine($"[{item.Provider}:{item.Model}]");
            builder.AppendLine($"역할: {role}");
            builder.AppendLine(item.Text);
            builder.AppendLine();
        }

        builder.AppendLine("요구사항:");
        builder.AppendLine("1) 역할별 장점을 취합해 하나의 최종 답변으로 통합");
        builder.AppendLine("2) 사실 충돌이 있으면 가장 보수적이고 검증 친화적인 결론을 선택");
        builder.AppendLine("3) 내부 역할 분담 과정은 드러내지 말고 사용자에게 바로 쓸 답변만 출력");
        builder.AppendLine("4) 한국어로 간결하고 실행 가능하게 답변");
        builder.AppendLine("5) 마크다운 코드블록은 필요할 때만 사용");
        return builder.ToString().Trim();
    }

    private static bool TryParseKillCommand(string text, out int pid)
    {
        pid = 0;
        if (!text.StartsWith("/kill ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length != 2)
        {
            return false;
        }

        return int.TryParse(tokens[1], out pid) && pid > 1;
    }

    private async Task<(bool Allowed, string Reason)> ValidateKillTargetAsync(int pid, string source, CancellationToken cancellationToken)
    {
        var (selfUidOk, selfUid) = await ReadCurrentUidAsync(cancellationToken);
        if (!selfUidOk)
        {
            return (false, "현재 사용자 UID 확인 실패");
        }

        var (targetUidOk, targetUid) = await ReadProcessUidAsync(pid, cancellationToken);
        if (!targetUidOk)
        {
            return (false, "대상 프로세스 UID 확인 실패");
        }

        if (!string.Equals(selfUid, targetUid, StringComparison.Ordinal))
        {
            return (false, $"다른 사용자 프로세스(uid={targetUid})는 종료할 수 없습니다.");
        }

        if (_killAllowlist.Length > 0)
        {
            var processName = await ReadProcessCommandAsync(pid, cancellationToken);
            if (string.IsNullOrWhiteSpace(processName))
            {
                return (false, "대상 프로세스 이름 확인 실패");
            }

            var matched = _killAllowlist.Any(item =>
                processName.Contains(item, StringComparison.OrdinalIgnoreCase));
            if (!matched)
            {
                return (false, $"allowlist 미일치 프로세스({processName})");
            }
        }

        if (source.Equals("telegram", StringComparison.OrdinalIgnoreCase))
        {
            return (true, "ok (telegram verified)");
        }

        return (true, "ok");
    }

    private static async Task<(bool Ok, string Uid)> ReadCurrentUidAsync(CancellationToken cancellationToken)
    {
        var result = await RunShellCaptureAsync("id -u", cancellationToken);
        if (result.ExitCode != 0)
        {
            return (false, string.Empty);
        }

        var uid = (result.StdOut ?? string.Empty).Trim();
        return (string.IsNullOrWhiteSpace(uid) ? false : true, uid);
    }

    private static async Task<(bool Ok, string Uid)> ReadProcessUidAsync(int pid, CancellationToken cancellationToken)
    {
        var cmd = $"ps -o uid= -p {pid}";
        var result = await RunShellCaptureAsync(cmd, cancellationToken);
        if (result.ExitCode != 0)
        {
            return (false, string.Empty);
        }

        var uid = (result.StdOut ?? string.Empty).Trim();
        return (string.IsNullOrWhiteSpace(uid) ? false : true, uid);
    }

    private static async Task<string> ReadProcessCommandAsync(int pid, CancellationToken cancellationToken)
    {
        var cmd = $"ps -o comm= -p {pid}";
        var result = await RunShellCaptureAsync(cmd, cancellationToken);
        if (result.ExitCode != 0)
        {
            return string.Empty;
        }

        return (result.StdOut ?? string.Empty).Trim();
    }

    private static async Task<ShellRunResult> RunShellCaptureAsync(string command, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/zsh",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (OperatingSystem.IsWindows())
        {
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(command);
        }
        else
        {
            startInfo.ArgumentList.Add("-lc");
            startInfo.ArgumentList.Add(command);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new ShellRunResult(127, string.Empty, ex.Message, false);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
            return new ShellRunResult(process.ExitCode, await stdoutTask, await stderrTask, false);
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            return new ShellRunResult(124, string.Empty, "timeout", true);
        }
    }

    private static string TrimForOutput(string text, int limit = 3500)
    {
        var normalized = text ?? string.Empty;
        var safeLimit = Math.Max(200, limit);
        if (normalized.Length <= safeLimit)
        {
            return normalized;
        }

        return normalized[..safeLimit] + "...(truncated)";
    }

    private static string BuildTelegramConcisePrompt(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        var looksLikeListRequest = RequestedCountRegex.IsMatch(normalized)
            || TopCountRegex.IsMatch(normalized)
            || ContainsAny(normalized, "뉴스", "news", "헤드라인", "속보", "목록", "리스트", "top");
        var requestedCount = ResolveRequestedResultCountFromQuery(input ?? string.Empty);
        var lengthRule = looksLikeListRequest
            ? $"- 목록형 요청은 항목 수를 임의로 줄이지 말고 요청 건수({requestedCount})를 유지"
            : "- 최대 7줄";
        var localNow = BuildLocalNowText();
        return $"""
                아래 질문에 한국어로 간결하게 답하세요.
                규칙:
                - 결론 먼저
                - 불필요한 인삿말/군더더기 금지
                {lengthRule}
                - 핵심 불릿 위주
                - 시간 관련 질문은 아래 로컬 시간을 기준으로 답변

                로컬 시간:
                {localNow}

                질문:
                {input}
                """;
    }

    private static string FormatTelegramResponse(string text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "응답이 비어 있습니다.";
        }

        const bool keepMarkdownTables = true;
        var normalized = SanitizeChatOutput(text, keepMarkdownTables: keepMarkdownTables);
        normalized = ConvertMarkdownToTelegramPlainText(normalized, keepMarkdownTables);
        normalized = ImproveTelegramReadability(normalized, keepMarkdownTables);
        normalized = CollapseTokenizedNumberedList(normalized);
        if (LooksLikeNumberedListResponse(normalized))
        {
            normalized = NormalizeGeminiWebNumberedListResponse(normalized);
        }
        normalized = NormalizeStructuredLabelBlocks(normalized);
        normalized = MergeDetachedTelegramDecimalLines(normalized);
        normalized = MergeDetachedTelegramNumberLines(normalized);
        normalized = AddTelegramClaimSpacing(normalized);
        var safeMaxChars = Math.Max(0, maxChars);
        var lines = normalized
            .Split('\n')
            .Take(safeMaxChars == 0 ? 200 : Math.Clamp(safeMaxChars / 16, 200, 400))
            .ToArray();
        return lines.Length == 0 ? normalized : string.Join('\n', lines).Trim();
    }

    private static string ConvertMarkdownToTelegramPlainText(string text, bool keepMarkdownTables = false)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        normalized = ExpandCollapsedMarkdownForTelegram(normalized);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var lines = normalized.Split('\n', StringSplitOptions.None);
        var result = new List<string>(lines.Length + 12);
        var inCodeFence = false;
        string[]? tableHeaders = null;
        var tableAutoIndex = 0;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            var trimmed = line.Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                tableHeaders = null;
                tableAutoIndex = 0;
                if (!inCodeFence)
                {
                    if (result.Count > 0 && !string.IsNullOrWhiteSpace(result[^1]))
                    {
                        result.Add(string.Empty);
                    }
                    result.Add("[코드]");
                    inCodeFence = true;
                }
                else
                {
                    inCodeFence = false;
                    if (result.Count > 0 && !string.IsNullOrWhiteSpace(result[^1]))
                    {
                        result.Add(string.Empty);
                    }
                }
                continue;
            }

            if (inCodeFence)
            {
                result.Add(line);
                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                tableHeaders = null;
                tableAutoIndex = 0;
                if (result.Count > 0 && !string.IsNullOrWhiteSpace(result[^1]))
                {
                    result.Add(string.Empty);
                }
                continue;
            }

            if (Regex.IsMatch(trimmed, @"^#{1,6}\s+"))
            {
                tableHeaders = null;
                tableAutoIndex = 0;
                var heading = Regex.Replace(trimmed, @"^#{1,6}\s+", string.Empty);
                heading = StripInlineMarkdownForTelegram(heading);
                if (!string.IsNullOrWhiteSpace(heading))
                {
                    if (result.Count > 0 && !string.IsNullOrWhiteSpace(result[^1]))
                    {
                        result.Add(string.Empty);
                    }
                    result.Add(heading);
                }
                continue;
            }

            if (Regex.IsMatch(trimmed, @"^(-{3,}|\*{3,}|_{3,})$"))
            {
                tableHeaders = null;
                tableAutoIndex = 0;
                if (result.Count > 0 && !string.IsNullOrWhiteSpace(result[^1]))
                {
                    result.Add(string.Empty);
                }
                result.Add("-----");
                continue;
            }

            if (trimmed.StartsWith("|", StringComparison.Ordinal) && trimmed.EndsWith("|", StringComparison.Ordinal))
            {
                if (keepMarkdownTables)
                {
                    tableHeaders = null;
                    tableAutoIndex = 0;
                    var preservedTableLine = NormalizeTelegramTableRowForPlainText(trimmed);
                    if (!string.IsNullOrWhiteSpace(preservedTableLine))
                    {
                        result.Add(preservedTableLine);
                    }
                    continue;
                }

                var cells = trimmed
                    .Trim('|')
                    .Split('|', StringSplitOptions.TrimEntries)
                    .Select(StripInlineMarkdownForTelegram)
                    .ToArray();
                if (cells.Length == 0)
                {
                    continue;
                }

                if (cells.All(cell => Regex.IsMatch(cell, @"^:?-{2,}:?$")))
                {
                    continue;
                }

                if (tableHeaders == null && IsLikelyTableHeaderRow(cells))
                {
                    tableHeaders = cells;
                    tableAutoIndex = 0;
                    continue;
                }

                tableAutoIndex += 1;
                var itemNo = tableAutoIndex;
                var firstCell = cells[0];
                if (TryExtractLeadingNumber(firstCell, out var parsedNo, out var firstCellRemainder))
                {
                    itemNo = parsedNo;
                    firstCell = firstCellRemainder;
                }

                var details = new List<string>(4);
                if (tableHeaders != null && tableHeaders.Length >= 2)
                {
                    for (var ci = 0; ci < Math.Min(cells.Length, tableHeaders.Length); ci += 1)
                    {
                        var key = StripInlineMarkdownForTelegram(tableHeaders[ci]);
                        var value = ci == 0 ? firstCell : cells[ci];
                        if (string.IsNullOrWhiteSpace(value))
                        {
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(key))
                        {
                            details.Add(value);
                            continue;
                        }

                        details.Add($"{key}: {value}");
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(firstCell))
                    {
                        details.Add(firstCell);
                    }

                    foreach (var extra in cells.Skip(1))
                    {
                        if (!string.IsNullOrWhiteSpace(extra))
                        {
                            details.Add(extra);
                        }
                    }
                }

                if (details.Count > 0)
                {
                    result.Add($"{itemNo}. {details[0]}");
                    foreach (var detail in details.Skip(1))
                    {
                        result.Add($"- {detail}");
                    }
                    result.Add(string.Empty);
                }
                continue;
            }

            if (trimmed.StartsWith(">", StringComparison.Ordinal))
            {
                tableHeaders = null;
                tableAutoIndex = 0;
                var quote = StripInlineMarkdownForTelegram(trimmed.TrimStart('>', ' '));
                if (!string.IsNullOrWhiteSpace(quote))
                {
                    result.Add($"인용: {quote}");
                }
                continue;
            }

            if (Regex.IsMatch(trimmed, @"^\d+[.)]\s+"))
            {
                tableHeaders = null;
                tableAutoIndex = 0;
                var orderedLine = Regex.Replace(trimmed, @"^(\d+)[.)]\s+", "$1. ");
                orderedLine = StripInlineMarkdownForTelegram(orderedLine);
                if (!string.IsNullOrWhiteSpace(orderedLine))
                {
                    result.Add(orderedLine);
                }
                continue;
            }

            if (Regex.IsMatch(trimmed, @"^[-*+]\s+"))
            {
                tableHeaders = null;
                tableAutoIndex = 0;
                var bulletContent = Regex.Replace(trimmed, @"^[-*+]\s+", string.Empty);
                bulletContent = StripInlineMarkdownForTelegram(bulletContent);
                if (!string.IsNullOrWhiteSpace(bulletContent))
                {
                    result.Add($"- {bulletContent}");
                }
                continue;
            }

            tableHeaders = null;
            tableAutoIndex = 0;
            var plainLine = StripInlineMarkdownForTelegram(trimmed);
            if (!string.IsNullOrWhiteSpace(plainLine))
            {
                result.Add(plainLine);
            }
        }

        var merged = string.Join('\n', result).Trim();
        return Regex.Replace(merged, @"\n{3,}", "\n\n");
    }

    private static string NormalizeTelegramTableRowForPlainText(string tableRow)
    {
        var trimmed = (tableRow ?? string.Empty).Trim();
        if (!trimmed.StartsWith("|", StringComparison.Ordinal)
            || !trimmed.EndsWith("|", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var cells = trimmed
            .Trim('|')
            .Split('|', StringSplitOptions.TrimEntries)
            .Select(StripInlineMarkdownForTelegram)
            .ToArray();
        if (cells.Length == 0)
        {
            return string.Empty;
        }

        if (cells.All(cell => Regex.IsMatch(cell, @"^:?-{2,}:?$")))
        {
            var normalizedSeparator = cells.Select(cell =>
            {
                var compact = cell.Trim();
                var leadingColon = compact.StartsWith(':');
                var trailingColon = compact.EndsWith(':');
                var dashCount = Math.Max(3, compact.Count(ch => ch == '-'));
                return string.Concat(
                    leadingColon ? ":" : string.Empty,
                    new string('-', dashCount),
                    trailingColon ? ":" : string.Empty
                );
            });
            return "| " + string.Join(" | ", normalizedSeparator) + " |";
        }

        return "| " + string.Join(" | ", cells.Select(cell => cell.Trim())) + " |";
    }

    private static string ExpandCollapsedMarkdownForTelegram(string text)
    {
        var normalized = text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        normalized = Regex.Replace(normalized, @"\s+\|\s+\|", " |\n|");
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
        return normalized.Trim();
    }

    private static bool IsLikelyTableHeaderRow(string[] cells)
    {
        if (cells == null || cells.Length < 2)
        {
            return false;
        }

        if (cells.All(cell => Regex.IsMatch(cell, @"^:?-{2,}:?$")))
        {
            return false;
        }

        if (cells.Any(cell => Regex.IsMatch(cell, @"^\d+[.)]?\s*")))
        {
            return false;
        }

        return true;
    }

    private static bool TryExtractLeadingNumber(string value, out int number, out string remainder)
    {
        number = 0;
        remainder = (value ?? string.Empty).Trim();
        var match = Regex.Match(remainder, @"^(?<num>\d+)[.)]?\s*(?<rest>.*)$");
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups["num"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            number = 0;
            return false;
        }

        var rest = match.Groups["rest"].Value.Trim();
        remainder = string.IsNullOrWhiteSpace(rest) ? remainder : rest;
        return true;
    }

    private static string StripInlineMarkdownForTelegram(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var value = text;
        value = Regex.Replace(value, @"!\[(?<alt>[^\]]*)\]\((?<url>[^)]+)\)", "[이미지] ${alt} (${url})");
        value = Regex.Replace(value, @"\[(?<title>[^\]]+)\]\((?<url>[^)]+)\)", "${title} (${url})");
        value = Regex.Replace(value, @"\[(?<title>[^\]]+)\]\[[^\]]*\]", "${title}");
        value = Regex.Replace(value, @"`{1,3}([^`]+)`{1,3}", "$1");
        value = Regex.Replace(value, @"(\*\*|__)(?<inner>.+?)\1", "${inner}");
        value = Regex.Replace(value, @"(\*|_)(?<inner>.+?)\1", "${inner}");
        value = Regex.Replace(value, @"~~(?<inner>.+?)~~", "${inner}");
        value = Regex.Replace(value, @"<[^>]+>", string.Empty);
        value = value
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\t", " ", StringComparison.Ordinal)
            .Replace("\\*", "*", StringComparison.Ordinal)
            .Replace("\\_", "_", StringComparison.Ordinal)
            .Replace("\\`", "`", StringComparison.Ordinal);
        value = Regex.Replace(value, @"[ \t]{2,}", " ");
        return value.Trim();
    }

    private static string ImproveTelegramReadability(string text, bool keepMarkdownTables = false)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal);
        var tightWrap = ShouldUseTightTelegramWrap(normalized);
        var wrapWidth = tightWrap ? 200 : 4000;

        var sourceLines = normalized.Split('\n', StringSplitOptions.None);
        var lines = new List<string>(sourceLines.Length + 8);
        foreach (var rawLine in sourceLines)
        {
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                {
                    lines.Add(string.Empty);
                }
                continue;
            }

            if (keepMarkdownTables && IsMarkdownTableRow(line))
            {
                lines.Add(line.Trim());
                continue;
            }

            if (TrySplitAsHeadlineList(line, out var headlineLines))
            {
                foreach (var headlineLine in headlineLines)
                {
                    lines.Add(headlineLine);
                }
                continue;
            }

            if (tightWrap && line.Length > 120)
            {
                line = Regex.Replace(
                    line,
                    @"([.!?])\s+(?!(?:md|js|cs|ts|jsx|tsx|txt|py|json|html|css|xml|yml|yaml|sh|ps1|bat|cmd|log|csv|exe|dll|zip|tar|gz)\b)(?=(?:[""'“‘(\[])?[가-힣A-Z])",
                    "$1\n",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
                );
                line = Regex.Replace(
                    line,
                    @"(다\.|요\.)\s+(?!(?:md|js|cs|ts|jsx|tsx|txt|py|json|html|css|xml|yml|yaml|sh|ps1|bat|cmd|log|csv|exe|dll|zip|tar|gz)\b)(?=(?:[""'“‘(\[])?[가-힣A-Z])",
                    "$1\n",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
                );
                line = Regex.Replace(line, @"(…+)\s+", "$1\n");
                line = Regex.Replace(line, @"\s+\|\s+\|", " |\n|");
                var splitLines = line.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var splitLine in splitLines)
                {
                    AddWrappedTelegramLines(lines, splitLine, wrapWidth);
                }
                continue;
            }

            AddWrappedTelegramLines(lines, line, wrapWidth);
        }

        var merged = string.Join('\n', lines).Trim();
        merged = Regex.Replace(merged, @"\n{3,}", "\n\n");
        return merged;
    }

    private static bool ShouldUseTightTelegramWrap(string text)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        var hasStructuredTitle = Regex.IsMatch(
            normalized,
            @"(?mi)^\s*(?:[-•▪]\s*)?(?:(?:No\.\d+|\d+[.)])\s*)?제목\s*[:：]",
            RegexOptions.CultureInvariant
        );
        var hasStructuredContent = Regex.IsMatch(
            normalized,
            @"(?mi)^\s*(?:[-•▪]\s*)?(?:(?:No\.\d+|\d+[.)])\s*)?내용\s*[:：]",
            RegexOptions.CultureInvariant
        );
        return hasStructuredTitle || hasStructuredContent;
    }

    private static void AddWrappedTelegramLines(List<string> output, string line, int maxWidth)
    {
        var normalizedLine = (line ?? string.Empty).Trim();
        if (normalizedLine.Length == 0)
        {
            return;
        }

        if (IsTelegramTitleLine(normalizedLine) || IsTelegramSourceLine(normalizedLine) || IsTelegramCategoryLine(normalizedLine))
        {
            output.Add(normalizedLine);
            return;
        }

        var wrapped = WrapLongLineForTelegram(normalizedLine, maxWidth).ToArray();
        if (wrapped.Length == 0)
        {
            return;
        }

        var claimLine = IsTelegramClaimLine(normalizedLine);
        for (var i = 0; i < wrapped.Length; i += 1)
        {
            output.Add(wrapped[i]);
        }
    }

    private static string CollapseTokenizedNumberedList(string text)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var lines = normalized.Split('\n', StringSplitOptions.None);
        var output = new List<string>(lines.Length);
        var index = 0;
        while (index < lines.Length)
        {
            var line = (lines[index] ?? string.Empty).Trim();
            if (!TryParseShortNumberedToken(line, out var token))
            {
                output.Add(lines[index]);
                index += 1;
                continue;
            }

            var runTokens = new List<string> { token };
            var runStart = index;
            var expectedNumber = 2;
            var cursor = index + 1;
            while (cursor < lines.Length)
            {
                var next = (lines[cursor] ?? string.Empty).Trim();
                if (!TryParseShortNumberedToken(next, out var nextToken, out var parsedNumber)
                    || parsedNumber != expectedNumber)
                {
                    break;
                }

                runTokens.Add(nextToken);
                expectedNumber += 1;
                cursor += 1;
            }

            if (runTokens.Count >= 6)
            {
                output.Add(string.Join(' ', runTokens));
                index = cursor;
                continue;
            }

            // run이 짧으면 원본 보존
            for (var i = runStart; i < cursor; i += 1)
            {
                output.Add(lines[i]);
            }
            index = cursor;
        }

        var merged = string.Join('\n', output).Trim();
        return Regex.Replace(merged, @"\n{3,}", "\n\n");
    }

    private static string MergeDetachedTelegramNumberLines(string text)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var lines = normalized.Split('\n', StringSplitOptions.None);
        var output = new List<string>(lines.Length);
        for (var i = 0; i < lines.Length; i += 1)
        {
            var current = (lines[i] ?? string.Empty).Trim();
            if (!Regex.IsMatch(current, @"^\d+\.$", RegexOptions.CultureInvariant))
            {
                output.Add(lines[i]);
                continue;
            }

            if (i + 1 >= lines.Length)
            {
                output.Add(lines[i]);
                continue;
            }

            var nextIndex = i + 1;
            while (nextIndex < lines.Length && string.IsNullOrWhiteSpace(lines[nextIndex]))
            {
                nextIndex += 1;
            }

            if (nextIndex >= lines.Length)
            {
                output.Add(lines[i]);
                continue;
            }

            var next = (lines[nextIndex] ?? string.Empty).Trim();
            if (next.Length == 0
                || Regex.IsMatch(next, @"^\d+\.$", RegexOptions.CultureInvariant)
                || next.StartsWith("```", StringComparison.Ordinal)
                || IsMarkdownTableRow(next))
            {
                output.Add(lines[i]);
                continue;
            }

            output.Add($"{current} {next}");
            i = nextIndex;
        }

        return Regex.Replace(string.Join('\n', output).Trim(), @"\n{3,}", "\n\n");
    }

    private static string MergeDetachedTelegramDecimalLines(string text)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        normalized = Regex.Replace(
            normalized,
            @"(\d+)\.\s*\n+(?=\d)",
            "$1.",
            RegexOptions.CultureInvariant
        );
        return Regex.Replace(normalized.Trim(), @"\n{3,}", "\n\n");
    }

    private static string AddTelegramClaimSpacing(string text)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var lines = normalized.Split('\n', StringSplitOptions.None);
        var output = new List<string>(lines.Length + 24);
        for (var i = 0; i < lines.Length; i += 1)
        {
            var trimmed = (lines[i] ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                if (output.Count > 0 && !string.IsNullOrWhiteSpace(output[^1]))
                {
                    output.Add(string.Empty);
                }
                continue;
            }

            if (ShouldInsertTelegramBlankBefore(trimmed)
                && output.Count > 0
                && !string.IsNullOrWhiteSpace(output[^1]))
            {
                output.Add(string.Empty);
            }

            output.Add(trimmed);

            var next = FindNextNonEmptyTelegramLine(lines, i + 1);
            if (ShouldInsertTelegramBlankAfter(trimmed, next)
                && output.Count > 0
                && !string.IsNullOrWhiteSpace(output[^1]))
            {
                output.Add(string.Empty);
            }
        }

        return Regex.Replace(string.Join('\n', output).Trim(), @"\n{3,}", "\n\n");
    }

    private static bool ShouldInsertTelegramBlankBefore(string line)
    {
        return IsTelegramClaimLine(line) || IsTelegramSourceLine(line);
    }

    private static bool ShouldInsertTelegramBlankAfter(string current, string? next)
    {
        if (string.IsNullOrWhiteSpace(next))
        {
            return false;
        }

        var currentTrimmed = (current ?? string.Empty).Trim();
        if (IsTelegramSourceLine(currentTrimmed))
        {
            return true;
        }

        var nextTrimmed = next.Trim();
        if (!IsTelegramClaimLine(nextTrimmed) && !IsTelegramSourceLine(nextTrimmed))
        {
            return false;
        }

        return currentTrimmed.EndsWith(".", StringComparison.Ordinal)
            || currentTrimmed.EndsWith("다.", StringComparison.Ordinal)
            || currentTrimmed.EndsWith("요.", StringComparison.Ordinal)
            || currentTrimmed.EndsWith("니다.", StringComparison.Ordinal)
            || currentTrimmed.EndsWith(":", StringComparison.Ordinal)
            || currentTrimmed.EndsWith("?", StringComparison.Ordinal)
            || currentTrimmed.EndsWith("!", StringComparison.Ordinal);
    }

    private static bool IsTelegramClaimLine(string line)
    {
        var trimmed = (line ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        return IsStandaloneNumberedHeadlineLine(trimmed)
            || trimmed.StartsWith("- ", StringComparison.Ordinal)
            || Regex.IsMatch(trimmed, @"^\d+\.\s+", RegexOptions.CultureInvariant)
            || Regex.IsMatch(trimmed, @"^[■□▪●◆▶▷]\s+", RegexOptions.CultureInvariant)
            || Regex.IsMatch(
                trimmed,
                @"^(?:(?:No\.\d+|\d+[.)])\s*)?\*\*[A-Za-z가-힣0-9('‘’][A-Za-z가-힣0-9()'‘’,.&+_/\-·\s]{0,80}\s*[:：]\*\*",
                RegexOptions.CultureInvariant
            )
            || Regex.IsMatch(
                trimmed,
                @"^(?:(?:No\.\d+|\d+[.)])\s*)?(제목|내용)\s*[:：]",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
            );
    }

    private static bool IsTelegramSourceLine(string line)
    {
        return Regex.IsMatch(
            (line ?? string.Empty).Trim(),
            @"^(?:\*\*)?\s*출처\s*[:：](?:\*\*)?",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
        );
    }

    private static bool IsTelegramTitleLine(string line)
    {
        var trimmed = (line ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        return IsStandaloneNumberedHeadlineLine(trimmed)
            || Regex.IsMatch(
            trimmed,
            @"^(?:[-•▪]\s*)?(?:(?:No\.\d+|\d+[.)])\s*)?제목\s*[:：]",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
        );
    }

    private static bool IsTelegramCategoryLine(string line)
    {
        var trimmed = (line ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (LooksLikeStandaloneTelegramTimeLine(trimmed))
        {
            return false;
        }

        var match = Regex.Match(
            trimmed,
            @"^(?:[-•▪]\s*)?(?<label>[A-Za-z가-힣0-9()'‘’,.&+\- ]{1,24})\s*[:：]\s*(?<value>.+)$",
            RegexOptions.CultureInvariant
        );
        if (!match.Success)
        {
            return false;
        }

        var label = match.Groups["label"].Value.Trim();
        if (label.Length == 0)
        {
            return false;
        }

        if (label.Equals("제목", StringComparison.OrdinalIgnoreCase)
            || label.Equals("내용", StringComparison.OrdinalIgnoreCase)
            || label.Equals("출처", StringComparison.OrdinalIgnoreCase)
            || label.Equals("출처링크", StringComparison.OrdinalIgnoreCase)
            || label.Equals("http", StringComparison.OrdinalIgnoreCase)
            || label.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool LooksLikeStandaloneTelegramTimeLine(string line)
    {
        var normalized = (line ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        return Regex.IsMatch(
            normalized,
            @"^(?:[-•▪]\s*)?(?:(?:No\.\d+|\d+[.)])\s*)?(?:(?:\d{4}[-/.]\d{1,2}[-/.]\d{1,2}|\d{1,2}[-/.]\d{1,2}(?:[-/.]\d{2,4})?)\s+)?\d{1,2}\s*:\s*\d{2}(?:\s*:\s*\d{2})?(?:\s*(?:AM|PM|am|pm))?$",
            RegexOptions.CultureInvariant
        );
    }

    private static string? FindNextNonEmptyTelegramLine(string[] lines, int startIndex)
    {
        for (var i = startIndex; i < lines.Length; i += 1)
        {
            var candidate = (lines[i] ?? string.Empty).Trim();
            if (candidate.Length > 0)
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool TryParseShortNumberedToken(string line, out string token)
    {
        return TryParseShortNumberedToken(line, out token, out _);
    }

    private static bool TryParseShortNumberedToken(string line, out string token, out int number)
    {
        token = string.Empty;
        number = 0;
        var normalized = (line ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        var match = Regex.Match(
            normalized,
            @"^(?<n>\d{1,2})\.\s+(?<token>[^\s]{1,12})$",
            RegexOptions.CultureInvariant
        );
        if (!match.Success
            || !int.TryParse(match.Groups["n"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            return false;
        }

        token = match.Groups["token"].Value.Trim();
        if (token.Length == 0)
        {
            return false;
        }

        if (token.Contains("출처", StringComparison.OrdinalIgnoreCase)
            || token.Contains("제목", StringComparison.OrdinalIgnoreCase)
            || token.Contains("내용", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (token.Contains(':', StringComparison.Ordinal)
            || token.Contains('/', StringComparison.Ordinal)
            || token.Contains('|', StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static bool TrySplitAsHeadlineList(string line, out IReadOnlyList<string> splitLines)
    {
        splitLines = Array.Empty<string>();
        var raw = (line ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        if (Regex.IsMatch(raw, @"^\d+[.)]\s+"))
        {
            return false;
        }

        if (raw.Contains("http://", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("https://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (raw.Contains(" / ", StringComparison.Ordinal)
            || raw.Contains("|", StringComparison.Ordinal)
            || raw.Contains("출처:", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("제목:", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("내용:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var commaCount = raw.Count(ch => ch == ',');
        if (raw.Length < 90 || commaCount < 2)
        {
            return false;
        }

        var candidate = raw;
        candidate = Regex.Replace(
            candidate,
            @"([.…]{1,3})(?=(?:[""'“‘(\[])?[가-힣A-Z])",
            "$1\n",
            RegexOptions.CultureInvariant
        );
        candidate = Regex.Replace(
            candidate,
            @"([.…]{1,3})\s+(?=(?:[""'“‘(\[])?[가-힣A-Z])",
            "$1\n",
            RegexOptions.CultureInvariant
        );
        candidate = Regex.Replace(candidate, @"\s+(?=[^,\n]{6,36},)", "\n");
        candidate = Regex.Replace(candidate, @"\n{2,}", "\n");

        var items = candidate
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        if (items.Count < 3 || items.Count > 8)
        {
            return false;
        }

        var shortOrSingleWordCount = items.Count(item =>
            item.Length < 8 || !item.Contains(' ', StringComparison.Ordinal));
        if (shortOrSingleWordCount > (items.Count / 3))
        {
            return false;
        }

        var output = new List<string>(items.Count + 2);
        for (var i = 0; i < items.Count; i += 1)
        {
            output.Add($"- {items[i]}");
        }

        splitLines = output;
        return true;
    }

    private static IEnumerable<string> WrapLongLineForTelegram(string line, int maxWidth)
    {
        var value = (line ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        var safeWidth = Math.Max(10, maxWidth);
        while (value.Length > safeWidth)
        {
            var cut = value.LastIndexOf(' ', safeWidth);
            if (cut < Math.Max(8, safeWidth / 2))
            {
                var nextSpace = value.IndexOf(' ', safeWidth);
                if (nextSpace > safeWidth && nextSpace <= safeWidth + 12)
                {
                    cut = nextSpace;
                }
                else
                {
                    cut = safeWidth;
                }
            }

            var head = value[..cut].Trim();
            if (!string.IsNullOrWhiteSpace(head))
            {
                yield return head;
            }

            value = value[cut..].TrimStart();
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            yield return value;
        }
    }

    private static string BuildLocalNowText()
    {
        var now = DateTimeOffset.Now;
        var offset = FormatUtcOffsetLabel(now.Offset);
        var timezoneId = TimeZoneInfo.Local.Id;
        return $"{now:yyyy-MM-dd HH:mm:ss} ({offset}, {timezoneId})";
    }

    private static string FormatUtcOffsetLabel(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var abs = offset < TimeSpan.Zero ? offset.Negate() : offset;
        return $"UTC{sign}{abs:hh\\:mm}";
    }

    private static HttpClient CreateWebFetchClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Omni-node/1.0");
        return client;
    }

    private static string ParseHelpTopicFromInput(string text)
    {
        var tokens = (text ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 2)
        {
            return string.Empty;
        }

        var topic = tokens[1].Trim().ToLowerInvariant();
        if (topic is "llm" or "model" or "models" or "모델")
        {
            return "llm";
        }

        if (topic is "routine" or "routines" or "루틴")
        {
            return "routine";
        }

        if (topic is "coding" or "code-run" or "코딩")
        {
            return "coding";
        }

        if (topic is "refactor" or "safe-refactor" or "safe_refactor" or "리팩터")
        {
            return "refactor";
        }

        if (topic is "plan" or "plans" or "planning" or "계획")
        {
            return "plan";
        }

        if (topic is "task" or "tasks" or "작업" or "태스크")
        {
            return "task";
        }

        if (topic is "doctor" or "진단" or "점검")
        {
            return "doctor";
        }

        if (topic is "notebook" or "노트북" or "handoff" or "인수인계")
        {
            return "notebook";
        }

        if (topic is "memory" or "메모리")
        {
            return "memory";
        }

        if (topic is "natural" or "대화" or "자연어")
        {
            return "natural";
        }

        return string.Empty;
    }

    private static string BuildTelegramHelpText(string? topic = null)
    {
        var normalized = (topic ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized == "llm")
        {
            return """
                   [LLM 도움말]
                   그냥 자연어로 먼저 말해도 됩니다.
                   - "단일 모드로 바꿔"
                   - "Codex로 바꿔"
                   - "모델 목록 보여줘"
                   - "다중 요약 담당을 Gemini로 설정해"

                   자주 쓰는 slash:
                   - /talk [low|high]
                   - /code [low|high]
                   - /model <groq|gemini|copilot|cerebras|codex>
                   - /llm status
                   - /llm models [groq|gemini|copilot|cerebras|codex|all]
                   - /llm usage
                   - /llm mode <single|orchestration|multi>
                   - /llm single provider <groq|gemini|copilot|cerebras|codex>
                   - /llm single model <model-id>
                   - /llm orchestration provider <auto|groq|gemini|copilot|cerebras|codex>
                   - /llm orchestration model <model-id>
                   - /llm multi <groq|gemini|copilot|cerebras|codex> <model-id>
                   - /llm multi summary <auto|groq|gemini|copilot|cerebras|codex>
                   """;
        }

        if (normalized == "routine")
        {
            return """
                   [루틴 도움말]
                   자연어 예시:
                   - "루틴 목록 보여줘"
                   - "루틴 생성: 매일 아침 8시에 뉴스 요약"
                   - "루틴 수정 rt-20260301093000-ab12cd34 매일 9시에 서버 상태 점검"
                   - "루틴 실행 rt-20260301093000-ab12cd34"
                   - "루틴 실행 이력 rt-20260301093000-ab12cd34"
                   - "루틴 재전송 rt-20260301093000-ab12cd34 1741482000000"

                   정확히 제어할 때:
                   - /routine list
                   - /routine create <요청>
                   - /routine update <routine-id> <요청>
                   - /routine run <routine-id>
                   - /routine runs <routine-id>
                   - /routine detail <routine-id> <ts>
                   - /routine resend <routine-id> <ts>
                   - /routine on <routine-id>
                   - /routine off <routine-id>
                   - /routine delete <routine-id>
                   """;
        }

        if (normalized == "skill" || normalized == "skills")
        {
            return """
                   [스킬 도움말]
                   자연어 예시:
                   - "스킬 목록 보여줘"
                   - "casual-empathy 스킬 사용해"
                   - "스킬 해제"
                   - "공감하는 일상 대화 스킬 만들어줘"

                   정확히 제어할 때:
                   - /skill list
                   - /skill use <name> [project|global]
                   - /skill get <name> [project|global]
                   - /skill create <name> [project|global]
                     한 줄 설명
                     ---
                     스킬 본문
                   - /skill off
                   """;
        }

        if (normalized == "coding")
        {
            return """
                   [코딩 도움말]
                   자연어 예시:
                   - "단일 코딩으로 로그인 페이지와 API까지 만들어줘"
                   - "오케스트레이션 코딩으로 지금 워크스페이스 점검하고 개선해줘"
                   - "다중 코딩으로 같은 요구사항 비교해줘"
                   - "단일 코딩 제공자를 Codex로 바꿔"
                   - "다중 코딩 워커 Gemini 모델을 gemini-2.5-pro로 설정해"
                   - "최근 코딩 결과 보여줘"
                   - "코딩 파일 1 보여줘"

                   자주 쓰는 slash:
                   - /coding status
                   - /coding mode <single|orchestration|multi>
                   - /coding language [single|orchestration|multi] <language|auto>
                   - /coding run <요구사항>
                   - /coding single provider <auto|groq|gemini|copilot|cerebras|codex>
                   - /coding single model <model-id>
                   - /coding single run <요구사항>
                   - /coding orchestration provider <auto|groq|gemini|copilot|cerebras|codex>
                   - /coding orchestration model <model-id>
                   - /coding orchestration worker <provider> <model-id|none>
                   - /coding orchestration run [요구사항]
                   - /coding multi provider <auto|groq|gemini|copilot|cerebras|codex>
                   - /coding multi model <model-id>
                   - /coding multi worker <provider> <model-id|none>
                   - /coding multi run <요구사항>
                   - /coding last
                   - /coding files
                   - /coding file <번호|경로>
                   """;
        }

        if (normalized == "refactor")
        {
            return """
                   [Safe Refactor 도움말]
                   쉬운 흐름:
                   1. /refactor read <path> [start] [end]
                   2. /refactor preview <path> <start> <end>
                      다음 줄부터 교체 코드를 붙여 넣기
                   3. /refactor apply

                   예시:
                   /refactor read apps/omninode-middleware/src/CommandService.Telegram.cs 10 20
                   /refactor preview apps/omninode-middleware/src/CommandService.Telegram.cs 12 14
                   새 코드...

                   또는:
                   /refactor preview 12 14
                   새 코드...

                   slash 없이도 가능:
                   refactor preview apps/omninode-middleware/src/CommandService.Telegram.cs 12 14 ::: 새 코드

                   상태 확인:
                   - /refactor status
                   """;
        }

        if (normalized == "doctor")
        {
            return """
                   [진단 도움말]
                   - /doctor
                   - /doctor last
                   - /doctor json
                   - /doctor last json

                   자연어 예시:
                   - "환경 진단해줘"
                   - "최근 진단 보여줘"
                   - "doctor 결과를 json으로 보여줘"
                   """;
        }

        if (normalized == "plan")
        {
            return """
                   [계획 도움말]
                   자연어 예시:
                   - "계획 목록 보여줘"
                   - "계획 생성: doctor 기능 구현"
                   - "계획 리뷰 plan_20260308103000001"

                   정확히 제어할 때:
                   - /plan list
                   - /plan get <plan-id>
                   - /plan create [--mode fast|interview] [--constraint <제약>]... <요청>
                   - /plan review <plan-id>
                   - /plan approve <plan-id>
                   - /plan run <plan-id>
                   """;
        }

        if (normalized == "task")
        {
            return """
                   [작업 도움말]
                   자연어 예시:
                   - "작업 목록 보여줘"
                   - "작업 상태 graph_20260308123500001"
                   - "작업 실행 graph_20260308123500001"

                   정확히 제어할 때:
                   - /task list
                   - /task create <plan-id>
                   - /task status <graph-id>
                   - /task run <graph-id>
                   - /task cancel <graph-id> <task-id>
                   - /task output <graph-id> <task-id>
                   """;
        }

        if (normalized == "notebook" || normalized == "handoff")
        {
            return """
                   [노트북 도움말]
                   자연어 예시:
                   - "노트북 보여줘"
                   - "노트북에 decision 계획은 task graph로 실행한다고 기록해줘"
                   - "인수인계 문서 만들어줘"

                   정확히 제어할 때:
                   - /notebook show [project-key]
                   - /notebook append <learning|decision|verification> <내용>
                   - /handoff [project-key]
                   """;
        }

        if (normalized == "memory")
        {
            return """
                   [메모리 도움말]
                   자연어 예시:
                   - "메모리 초기화해줘"
                   - "메모리 노트 만들어줘"
                   - "메모리 compact로 저장해줘"

                   정확히 제어할 때:
                   - /memory clear
                   - /memory create [compact]
                   """;
        }

        if (normalized == "natural")
        {
            return """
                   [자연어 제어 도움말]
                   슬래시 없이도 대부분의 제어를 처리합니다.

                   지원 예시:
                   - "단일 모드로 바꿔"
                   - "Codex로 바꿔"
                   - "단일 코딩으로 로그인 페이지 만들어줘"
                   - "최근 코딩 결과 보여줘"
                   - "리팩터 상태 보여줘"
                   - "모델 목록 보여줘"
                   - "메모리 초기화"
                   - "환경 진단해줘"
                   - "계획 목록 보여줘"
                   - "작업 상태 graph_..."
                   - "노트북 보여줘"
                   - "루틴 생성: 매일 09:00 서버 상태 점검"

                   보안 정책:
                   - 프로세스 종료는 /kill <pid> 슬래시 명령으로만 허용됩니다.
                   """;
        }

        return """
               [Omni-node Telegram 도움말]
               먼저 자연어로 말해도 됩니다.
               - "단일 모드로 바꿔"
               - "Codex로 바꿔"
               - "단일 코딩으로 로그인 페이지 만들어줘"
               - "단일 코딩 제공자를 Codex로 바꿔"
               - "최근 코딩 결과 보여줘"
               - "환경 진단해줘"
               - "루틴 목록 보여줘"
               - "노트북 보여줘"

               자주 쓰는 slash:
               - /talk [low|high]
               - /code [low|high]
               - /coding status
               - /coding run <요구사항>
               - /coding last
               - /refactor read <path>
               - /model <groq|gemini|copilot|cerebras|codex>
               - /llm status
               - /skill list
               - /skill use <name>
               - /doctor
               - /routine list
               - /plan list
               - /task list
               - /notebook show
               - /memory create [compact]

               더 보기:
               - /help llm
               - /help skill
               - /help coding
               - /help refactor
               - /help doctor
               - /help routine
               - /help plan
               - /help task
               - /help notebook
               - /help memory
               - /help natural
               """;
    }

    private string BuildTelegramUpgradeQuotaStatePath()
    {
        var baseDir = Path.GetDirectoryName(_config.LlmUsageStatePath);
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            baseDir = string.IsNullOrWhiteSpace(home) ? Path.GetTempPath() : Path.Combine(home, ".omninode");
        }

        return Path.Combine(baseDir, "telegram_upgrade_quota.state");
    }

    private void LoadTelegramUpgradeQuotaState()
    {
        lock (_telegramUpgradeQuotaLock)
        {
            _telegramUpgradeQuotaDay = GetCurrentQuotaDayKey();
            _telegramUpgradeQuotaCount = 0;
            try
            {
                if (!File.Exists(_telegramUpgradeQuotaStatePath))
                {
                    return;
                }

                var text = File.ReadAllText(_telegramUpgradeQuotaStatePath, Encoding.UTF8).Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return;
                }

                var parts = text.Split('|', StringSplitOptions.TrimEntries);
                if (parts.Length < 2)
                {
                    return;
                }

                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
                {
                    return;
                }

                if (parts[0] == _telegramUpgradeQuotaDay)
                {
                    _telegramUpgradeQuotaCount = Math.Max(0, count);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[telegram-quota] load failed: {ex.Message}");
            }
        }
    }

    private void SaveTelegramUpgradeQuotaState()
    {
        lock (_telegramUpgradeQuotaLock)
        {
            try
            {
                var dir = Path.GetDirectoryName(_telegramUpgradeQuotaStatePath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var content = $"{_telegramUpgradeQuotaDay}|{_telegramUpgradeQuotaCount.ToString(CultureInfo.InvariantCulture)}";
                AtomicFileStore.WriteAllText(_telegramUpgradeQuotaStatePath, content, ownerOnly: true);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[telegram-quota] save failed: {ex.Message}");
            }
        }
    }

    private void NormalizeTelegramQuotaDayLocked()
    {
        var day = GetCurrentQuotaDayKey();
        if (_telegramUpgradeQuotaDay == day)
        {
            return;
        }

        _telegramUpgradeQuotaDay = day;
        _telegramUpgradeQuotaCount = 0;
        SaveTelegramUpgradeQuotaState();
    }

    private bool TryConsumeTelegramUpgradeQuota()
    {
        lock (_telegramUpgradeQuotaLock)
        {
            NormalizeTelegramQuotaDayLocked();
            if (_telegramUpgradeQuotaCount >= TelegramUpgradeDailyCap)
            {
                return false;
            }

            _telegramUpgradeQuotaCount += 1;
            SaveTelegramUpgradeQuotaState();
            return true;
        }
    }

    private (string DayKey, int Used, int Cap) GetTelegramUpgradeQuotaSnapshot()
    {
        lock (_telegramUpgradeQuotaLock)
        {
            NormalizeTelegramQuotaDayLocked();
            return (_telegramUpgradeQuotaDay, _telegramUpgradeQuotaCount, TelegramUpgradeDailyCap);
        }
    }

    private static string GetCurrentQuotaDayKey()
    {
        return DateTimeOffset.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

}
