using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OmniNode.Middleware.Infrastructure.Telegram;

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

        var requestedThinking = tokens.Length >= 2 ? TelegramLlmPreferencePolicy.NormalizeThinkingLevel(tokens[1], "auto") : "auto";
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
        var fastModel = string.IsNullOrWhiteSpace(_providers.GroqModel) ? DefaultGroqPrimaryModel : _providers.GroqModel;
        TelegramLlmPreferencePolicy.ApplyTalkDefaults(
            _telegramLlmPreferences,
            requestedThinking,
            fastModel,
            _providers.GeminiModel,
            DefaultCopilotModel,
            _providers.CerebrasModel,
            _providers.CodexModel
        );
    }

    private void ApplyTelegramCodeDefaults(string requestedThinking)
    {
        var fastModel = string.IsNullOrWhiteSpace(_providers.GroqModel) ? DefaultGroqPrimaryModel : _providers.GroqModel;
        TelegramLlmPreferencePolicy.ApplyCodeDefaults(
            _telegramLlmPreferences,
            requestedThinking,
            fastModel,
            _providers.GeminiModel,
            DefaultCopilotModel,
            _providers.CerebrasModel,
            _providers.CodexModel
        );
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

            var statusBody = $"""
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

            // single chat provider 빠른 전환 버튼.
            return AppendTelegramInlineButtons(
                statusBody,
                ("/llm single provider groq", "Groq"),
                ("/llm single provider gemini", "Gemini"),
                ("/llm single provider cerebras", "Cerebras"),
                ("/llm single provider nvidia", "NVIDIA"),
                ("/llm single provider copilot", "Copilot")
            );
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
                    return "사용법: /llm set <groq|copilot|codex|nvidia> <model-id>";
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

            if (provider == "nvidia" || provider == "nvidia-nim" || provider == "nvidia_nim" || provider == "nim")
            {
                var providerSet = SetChannelProvider("telegram", "single", "nvidia");
                if (providerSet.StartsWith("지원", StringComparison.OrdinalIgnoreCase)
                    || providerSet.StartsWith("invalid", StringComparison.OrdinalIgnoreCase))
                {
                    return providerSet;
                }

                return SetChannelModel("telegram", "single", modelId);
            }

            return "사용법: /llm set <groq|copilot|codex|nvidia> <model-id>";
        }

        if (tokens[1].Equals("single", StringComparison.OrdinalIgnoreCase))
        {
            if (tokens.Length < 4)
            {
                return "사용법: /llm single provider <groq|gemini|copilot|cerebras|nvidia|codex> | /llm single model <model-id>";
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

            return "사용법: /llm single provider <groq|gemini|copilot|cerebras|nvidia|codex> | /llm single model <model-id>";
        }

        if (tokens[1].Equals("orchestration", StringComparison.OrdinalIgnoreCase))
        {
            if (tokens.Length < 4)
            {
                return "사용법: /llm orchestration provider <auto|groq|gemini|copilot|cerebras|nvidia|codex> | /llm orchestration model <model-id>";
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

            return "사용법: /llm orchestration provider <auto|groq|gemini|copilot|cerebras|nvidia|codex> | /llm orchestration model <model-id>";
        }

        if (tokens[1].Equals("multi", StringComparison.OrdinalIgnoreCase))
        {
            if (tokens.Length < 4)
            {
                return "사용법: /llm multi <groq|gemini|copilot|cerebras|nvidia|codex> <model-id> | /llm multi summary <auto|groq|gemini|copilot|cerebras|nvidia|codex>";
            }

            var key = tokens[2].ToLowerInvariant();
            var value = string.Join(' ', tokens.Skip(3)).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return "사용법: /llm multi <groq|gemini|copilot|cerebras|nvidia|codex> <model-id> | /llm multi summary <auto|groq|gemini|copilot|cerebras|nvidia|codex>";
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

                if (key == "nvidia" || key == "nvidia-nim" || key == "nvidia_nim" || key == "nim")
                {
                    return SetChannelModel("telegram", "multi.nvidia", value);
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

            return "사용법: /llm multi <groq|gemini|copilot|cerebras|nvidia|codex> <model-id> | /llm multi summary <auto|groq|gemini|copilot|cerebras|nvidia|codex>";
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
            return Task.FromResult<string?>("사용법: /model <groq|gemini|copilot|cerebras|nvidia|codex>");
        }

        var key = tokens[1].Trim().ToLowerInvariant();
        lock (_telegramLlmLock)
        {
            var selection = TelegramLlmPreferencePolicy.ResolveQuickModelSelection(
                key,
                _providers.GroqModel,
                DefaultGroqPrimaryModel,
                _providers.GeminiModel,
                DefaultCopilotModel,
                _providers.CerebrasModel,
                _providers.NvidiaModel,
                _providers.CodexModel
            );
            if (selection != null)
            {
                _telegramLlmPreferences.Profile = "default";
                _telegramLlmPreferences.Mode = "single";
                _telegramLlmPreferences.SingleProvider = selection.Provider;
                _telegramLlmPreferences.SingleModel = selection.Model;
                _telegramLlmPreferences.AutoGroqComplexUpgrade = selection.AutoGroqComplexUpgrade;
                return Task.FromResult<string?>($"단일 제공자를 {selection.ProviderDisplayName}로 바꿨습니다. 현재 모델: {_telegramLlmPreferences.SingleModel}");
            }
        }

        return Task.FromResult<string?>("사용법: /model <groq|gemini|copilot|cerebras|nvidia|codex>");
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
        if (ContainsAny(lowered, "최근 답변 노트북", "마지막 답변 노트북", "최근 응답 노트북", "save last answer to notebook"))
        {
            var latest = TryBuildLastTelegramAssistantNotebookAppend();
            return string.IsNullOrWhiteSpace(latest)
                ? "저장할 최근 텔레그램 답변이 없습니다."
                : await ExecuteTelegramPseudoCommandAsync(latest, attachments, webUrls, webSearchEnabled, cancellationToken);
        }

        if (ContainsAny(lowered, "최근 코딩 결과 노트북", "코딩 결과 노트북", "save coding result to notebook"))
        {
            var latestCoding = TryBuildLatestTelegramCodingNotebookAppend();
            return string.IsNullOrWhiteSpace(latestCoding)
                ? "저장할 최근 텔레그램 코딩 결과가 없습니다."
                : await ExecuteTelegramPseudoCommandAsync(latestCoding, attachments, webUrls, webSearchEnabled, cancellationToken);
        }

        if (ContainsAny(lowered, "최근 답변 계획", "마지막 답변 계획", "최근 응답 계획", "최근 답변으로 계획", "마지막 답변으로 계획", "plan from last answer"))
        {
            var latestPlan = TryBuildLastTelegramAssistantPlanCreate();
            return string.IsNullOrWhiteSpace(latestPlan)
                ? "계획으로 만들 최근 텔레그램 답변이 없습니다."
                : await ExecuteTelegramPseudoCommandAsync(latestPlan, attachments, webUrls, webSearchEnabled, cancellationToken);
        }

        if (ContainsAny(lowered, "최근 코딩 결과 계획", "코딩 결과 계획", "최근 코딩 결과로 계획", "plan from coding result"))
        {
            var latestCodingPlan = TryBuildLatestTelegramCodingPlanCreate();
            return string.IsNullOrWhiteSpace(latestCodingPlan)
                ? "계획으로 만들 최근 텔레그램 코딩 결과가 없습니다."
                : await ExecuteTelegramPseudoCommandAsync(latestCodingPlan, attachments, webUrls, webSearchEnabled, cancellationToken);
        }

        var pseudoCommand = TelegramNaturalCommandPolicy.TryBuildNaturalPseudoCommand(normalized, lowered);
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

        var helpTopic = TelegramNaturalCommandPolicy.ExtractHelpTopic(lowered);
        if (helpTopic != null)
        {
            return BuildTelegramHelpText(helpTopic);
        }

        var setProviderModel = Regex.Match(normalized, @"(?i)(groq|그록|gemini|제미니|copilot|코파일럿|cerebras|세레브라스|세레브라|nvidia|nvidia-nim|nim|엔비디아|codex|코덱스)\s*모델\s*([a-zA-Z0-9._/\-]+)\s*(?:로|으로)?\s*(?:바꿔|변경|설정)");
        if (setProviderModel.Success)
        {
            var provider = TelegramNaturalCommandPolicy.ExtractProviderAlias(setProviderModel.Groups[1].Value, allowAuto: false);
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
        return await TelegramPseudoCommandExecutor.ExecuteAsync(
            new TelegramPseudoCommandRequest(pseudoCommand, attachments, webUrls, webSearchEnabled),
            BuildTelegramPseudoCommandHandlers(),
            cancellationToken
        );
    }

    private TelegramPseudoCommandHandlers BuildTelegramPseudoCommandHandlers()
    {
        return new TelegramPseudoCommandHandlers(
            ParseHelpTopicFromInput,
            BuildTelegramHelpText,
            TryHandleTelegramProfileCommandAsync,
            TryHandleTelegramQuickModelCommandAsync,
            TryHandleTelegramLlmControlCommandAsync,
            TryHandleTelegramSkillCommandAsync,
            TryHandleTelegramCodingCommandAsync,
            TryHandleTelegramRefactorCommandAsync,
            TryHandleTelegramMemoryCommandAsync,
            TryHandleTelegramDoctorCommandAsync,
            TryHandleTelegramPlanCommandAsync,
            TryHandleTelegramTaskCommandAsync,
            TryHandleTelegramNotebookCommandAsync,
            (command, token) => TryHandleRoutineCommandAsync(command, "telegram", token),
            ExecuteTelegramMetricsPseudoCommandAsync,
            ExecuteTelegramKillPseudoCommandAsync
        );
    }

    private async Task<string?> ExecuteTelegramMetricsPseudoCommandAsync(string command, CancellationToken cancellationToken)
    {
        var metrics = await _coreClient.GetMetricsAsync(cancellationToken);
        RecordEvent($"telegram:natural:{command}");
        _auditLogger.Log("telegram", "metrics", "ok", "natural_control");
        return metrics;
    }

    private async Task<string?> ExecuteTelegramKillPseudoCommandAsync(string command, CancellationToken cancellationToken)
    {
        if (!TryParseKillCommand(command, out var pid))
        {
            return null;
        }

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
            builder.AppendLine($"- 기본: {_providers.GeminiModel}");
            builder.AppendLine($"- 현재 단일 선택: {(snapshot.SingleProvider == "gemini" ? snapshot.SingleModel : _providers.GeminiModel)}");
            builder.AppendLine($"- 현재 다중 선택: {(string.IsNullOrWhiteSpace(snapshot.MultiGeminiModel) ? _providers.GeminiModel : snapshot.MultiGeminiModel)}");
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
            builder.AppendLine($"- 기본: {_providers.CerebrasModel}");
            builder.AppendLine($"- 현재 단일 선택: {(snapshot.SingleProvider == "cerebras" ? snapshot.SingleModel : _providers.CerebrasModel)}");
            builder.AppendLine($"- 현재 다중 선택: {snapshot.MultiCerebrasModel}");
            builder.AppendLine();
        }

        if (selected == "all" || selected == "nvidia" || selected == "nim" || selected == "nvidia-nim")
        {
            hasSection = true;
            builder.AppendLine("[NVIDIA NIM 모델]");
            builder.AppendLine($"- 기본: {_providers.NvidiaModel}");
            builder.AppendLine("- 대표 지원: meta/llama-3.3-70b-instruct");
            builder.AppendLine("- 대표 지원: nvidia/llama-3.3-nemotron-super-49b-v1.5");
            builder.AppendLine("- 대표 지원: nvidia/nemotron-3-super-120b-a12b");
            builder.AppendLine("- 대표 지원: openai/gpt-oss-120b");
            builder.AppendLine("- 대표 지원: qwen/qwen3-coder-480b-a35b-instruct");
            builder.AppendLine($"- 현재 단일 선택: {(snapshot.SingleProvider == "nvidia" ? snapshot.SingleModel : _providers.NvidiaModel)}");
            builder.AppendLine($"- 현재 다중 선택: {snapshot.MultiNvidiaModel}");
            builder.AppendLine();
        }

        if (selected == "all" || selected == "codex")
        {
            hasSection = true;
            builder.AppendLine("[Codex 모델]");
            builder.AppendLine($"- 기본: {_providers.CodexModel}");
            builder.AppendLine($"- 현재 단일 선택: {(snapshot.SingleProvider == "codex" ? snapshot.SingleModel : _providers.CodexModel)}");
            builder.AppendLine($"- 현재 다중 선택: {snapshot.MultiCodexModel}");
            builder.AppendLine();
        }

        if (!hasSection)
        {
            return "사용법: /llm models [groq|gemini|copilot|cerebras|nvidia|codex|all]";
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
        builder.AppendLine($"- input_price=${_providers.GeminiInputPricePerMillionUsd:F4}/1M, output_price=${_providers.GeminiOutputPricePerMillionUsd:F4}/1M");
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
        return _executionContext.GetTelegramExecutionMetadata();
    }

    private void SetCurrentTelegramExecutionMetadata(
        SearchAnswerGuardFailure? guardFailure = null,
        int retryAttempt = 0,
        int retryMaxAttempts = 0,
        string? retryStopReason = "-"
    )
    {
        _executionContext.SetTelegramExecutionMetadata(
            guardFailure,
            retryAttempt,
            retryMaxAttempts,
            retryStopReason
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
        var requestText = text ?? string.Empty;
        TelegramLlmPreferences snapshot;
        lock (_telegramLlmLock)
        {
            snapshot = _telegramLlmPreferences.Clone();
        }

        var telegramThread = EnsureTelegramLinkedConversation();
        var telegramStateKey = ResolveTelegramStateKey(telegramThread);
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
        var rawIncomingText = requestText;
        var thinkPlusToggleNote = string.Empty;
        var hasActivationKeyword = LooksLikeThinkPlusActivation(rawIncomingText);
        var hasDeactivationKeyword = LooksLikeThinkPlusDeactivation(rawIncomingText);
        if (hasActivationKeyword && !hasDeactivationKeyword)
        {
            if (!IsThinkPlusActiveForThread(telegramStateKey))
            {
                SetThinkPlusForThread(telegramStateKey, true);
                thinkPlusToggleNote = "[추론 모드 활성화] 지금부터 모든 메시지에 대해 최신 웹 검색 결과를 참고해 답변합니다. 끄려면 \"추론 모드 꺼\"라고 말하세요.";
            }
        }
        else if (hasDeactivationKeyword)
        {
            if (IsThinkPlusActiveForThread(telegramStateKey))
            {
                SetThinkPlusForThread(telegramStateKey, false);
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
                _conversationStore.AppendMessage(session.Thread.Id, "user", requestText, "telegram:user");
                _conversationStore.AppendMessage(session.Thread.Id, "assistant", note, "telegram:think_plus_toggle");
                await EnsureConversationTitleFromFirstTurnAsync(session.Thread.Id, "system", "-", cancellationToken);
                SetCurrentTelegramExecutionMetadata(null, 0, 0, "-");
                _auditLogger.Log(
                    "telegram",
                    "think_plus_toggle",
                    IsThinkPlusActiveForThread(telegramStateKey) ? "on" : "off",
                    $"thread={session.Thread.Id} stateKey={telegramStateKey} bare_toggle=true"
                );
                return note;
            }
            // 결합 메시지: text 자체를 정리된 질문으로 덮어쓰고 정상 흐름 진행. 노트는 응답에 prepend.
            requestText = stripped;
        }

        var effectiveTopicInput = BuildTelegramFollowupAwareInput(telegramThread, requestText);
        var requestedSkillName = TryExtractInlineSkillName(requestText);
        var skillQueryText = requestText;
        var hasStickyActiveSkillForTelegram = !string.IsNullOrWhiteSpace(telegramStateKey)
            && _activeSkillByThread.ContainsKey(telegramStateKey);
        var thinkPlusActiveForTelegram = IsThinkPlusActiveForThread(telegramStateKey);
        var isSkillContextQuery = LooksLikeProjectContextRequest(skillQueryText)
            || LooksLikeSkillCreationRequest(skillQueryText)
            || LooksLikeSkillDeactivationRequest(skillQueryText)
            || Regex.IsMatch(skillQueryText, @"(?i)(스킬|skill|skills|skill\.md).*(목록|리스트|뭐|보여|알려|어떤|종류|있어|있니|돼)")
            || hasStickyActiveSkillForTelegram;
        var resolvedWebUrls = ResolveWebUrls(effectiveTopicInput, webUrls, webSearchEnabled);
        if (resolvedWebUrls.Count > 0
            && snapshot.Mode == "single"
            && _llmRouter.HasGeminiApiKey()
            && !isSkillContextQuery
            && !thinkPlusActiveForTelegram)
        {
            var allowMarkdownTable = SearchQueryPolicy.LooksLikeTableRenderRequest(effectiveTopicInput);
            var memoryHint = BuildSafeWebMemoryPreferenceHint(
                telegramStateKey,
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
            var urlResponseText = AppendTelegramResponseFooter(
                FormatTelegramResponse(urlSingle.Response.Text, TelegramMaxResponseChars),
                urlSingle.Response.Provider,
                urlSingle.Response.Model,
                telegramStateKey,
                "url"
            );
            var urlAssistantMeta = $"telegram-single:{urlSingle.Response.Provider}:{urlSingle.Response.Model}:gemini-url-single";
            _conversationStore.AppendMessage(session.Thread.Id, "user", requestText, "telegram:user");
            _conversationStore.AppendMessage(session.Thread.Id, "assistant", urlResponseText, urlAssistantMeta);
            await EnsureConversationTitleFromFirstTurnAsync(session.Thread.Id, urlSingle.Response.Provider, urlSingle.Response.Model, cancellationToken);
            _ = await MaybeCompressConversationAsync(session.Thread.Id, "chat-single", urlSingle.Response.Provider, urlSingle.Response.Model, cancellationToken);
            _auditLogger.Log("telegram", "telegram_guard_meta", "ok", $"route={urlAssistantMeta} guardCategory=- guardReason=- guardDetail=-");
            SetCurrentTelegramExecutionMetadata(null, 0, 0, "-");
            return urlResponseText;
        }

        var shouldAllowFastWeb = webSearchEnabled
            && snapshot.Mode == "single"
            && !thinkPlusActiveForTelegram
            && !isSkillContextQuery;

        if (shouldAllowFastWeb)
        {
            var decisionPath = "heuristic_no_web";
            var shouldUseGeminiWeb = false;
            var shouldFallbackToGeminiWeb = false;

            if (SearchQueryPolicy.LooksLikeExplicitWebLookupQuestion(requestText))
            {
                decisionPath = "heuristic_explicit_web";
                shouldUseGeminiWeb = true;
            }
            else if (SearchQueryPolicy.LooksLikeRealtimeQuestion(effectiveTopicInput))
            {
                decisionPath = "heuristic_web";
                shouldUseGeminiWeb = true;
            }
            else if (!SearchQueryPolicy.LooksLikeClearlyNonWebQuestion(effectiveTopicInput))
            {
                var webDecision = await DecideNeedWebBySelectedProviderAsync(
                    effectiveTopicInput,
                    snapshotSingleProvider,
                    snapshotSingleModel,
                    cancellationToken
                );
                shouldFallbackToGeminiWeb = !webDecision.DecisionSucceeded && SearchQueryPolicy.LooksLikeRealtimeQuestion(effectiveTopicInput);
                shouldUseGeminiWeb = webDecision.NeedWeb || shouldFallbackToGeminiWeb;
                decisionPath = webDecision.DecisionSucceeded ? "llm" : "heuristic_fallback";
            }

            if (shouldUseGeminiWeb)
            {
                var allowMarkdownTable = SearchQueryPolicy.LooksLikeTableRenderRequest(effectiveTopicInput);
                var memoryHint = BuildSafeWebMemoryPreferenceHint(
                    telegramStateKey,
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
                var webResponseText = AppendTelegramResponseFooter(
                    FormatTelegramResponse(webSingle.Response.Text, TelegramMaxResponseChars),
                    webSingle.Response.Provider,
                    webSingle.Response.Model,
                    telegramStateKey,
                    "web"
                );
                var webAssistantMeta = $"telegram-single:{webSingle.Response.Provider}:{webSingle.Response.Model}:{webSingle.Route}";
                _conversationStore.AppendMessage(session.Thread.Id, "user", requestText, "telegram:user");
                _conversationStore.AppendMessage(session.Thread.Id, "assistant", webResponseText, webAssistantMeta);
                await EnsureConversationTitleFromFirstTurnAsync(session.Thread.Id, webSingle.Response.Provider, webSingle.Response.Model, cancellationToken);
                _ = await MaybeCompressConversationAsync(session.Thread.Id, "chat-single", webSingle.Response.Provider, webSingle.Response.Model, cancellationToken);
                _auditLogger.Log("telegram", "telegram_guard_meta", "ok", $"route={webAssistantMeta} guardCategory=- guardReason=- guardDetail=-");
                SetCurrentTelegramExecutionMetadata(webSingle.GuardFailure, 0, 0, "-");
                return webResponseText;
            }
        }

        var effectiveWebSearchEnabled = snapshot.Mode == "single"
            ? webSearchEnabled && (thinkPlusActiveForTelegram || isSkillContextQuery || SearchQueryPolicy.LooksLikeExplicitWebLookupQuestion(effectiveTopicInput) || SearchQueryPolicy.LooksLikeRealtimeQuestion(effectiveTopicInput))
            : webSearchEnabled;
        var normalizedAttachments = NormalizeAttachments(attachments);
        var sharedPrepared = await PrepareSharedInputAsync(
            effectiveTopicInput,
            normalizedAttachments,
            resolvedWebUrls,
            effectiveWebSearchEnabled,
            cancellationToken,
            "telegram",
            session.SessionKey,
            telegramStateKey,
            requestedSkillName,
            null
        );
        if (!string.IsNullOrWhiteSpace(sharedPrepared.UnsupportedMessage))
        {
            var blockedAssistantMeta = "telegram-forced-context:unsupported";
            var blockedResponseText = sharedPrepared.UnsupportedMessage;
            _conversationStore.AppendMessage(session.Thread.Id, "user", requestText, "telegram:user");
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

        var preparedInput = await PrepareTelegramInputAsync(
            sharedPrepared.Text,
            cancellationToken,
            preserveContext: isSkillContextQuery
                             || hasStickyActiveSkillForTelegram
                             || thinkPlusActiveForTelegram
                             || effectiveWebSearchEnabled
                             || normalizedAttachments.Count > 0
        );

        preparedInput = ApplySelectedSkillToPrompt(
            preparedInput,
            requestedSkillName,
            null
        );

        // Think+ 활성이면 sharedPrepared 앞에 web context prepend
        if (thinkPlusActiveForTelegram)
        {
            var effectiveSkillForThinkPlus = ResolveEffectiveSkillNameForThread(requestedSkillName, telegramStateKey);
            var thinkPlusContext = await BuildThinkPlusContextAsync(
                requestText,
                "telegram",
                cancellationToken,
                effectiveSkillForThinkPlus
            ).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(thinkPlusContext))
            {
                preparedInput = thinkPlusContext + preparedInput;
            }
        }

        var thinkingLevel = TelegramPromptPolicy.ResolveThinkingLevel(snapshot, requestText);
        var profiledInput = BuildTelegramProfilePrompt(preparedInput, snapshot.Profile, thinkingLevel);
        var contextualProfiledInput = BuildContextualInput(
            session.SessionId,
            profiledInput,
            session.LinkedMemoryNotes,
            contextDecisionInput: effectiveTopicInput
        );
        var shouldSkipDriftRecovery = ShouldSkipTelegramDriftRecovery(contextualProfiledInput, effectiveTopicInput, preparedInput);

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
            var orchestratedValidated = ApplyListCountFallback(requestText, orchestrated.Text, sharedPrepared.Citations);
            orchestratedValidated = ApplySkillCreateDirective(orchestratedValidated, "telegram");
            orchestratedValidated = CleanLeakedSystemMarkers(orchestratedValidated);
            responseText = AppendTelegramResponseFooter(
                FormatTelegramResponse(orchestratedValidated, TelegramMaxResponseChars),
                "orchestration",
                orchestrated.Route,
                telegramStateKey
            );
            providerForMemory = NormalizeProvider(snapshot.OrchestrationProvider, allowAuto: true);
            if (providerForMemory is "auto" or "none")
            {
                providerForMemory = "gemini";
            }

            modelForMemory = string.IsNullOrWhiteSpace(snapshot.OrchestrationModel) ? "-" : snapshot.OrchestrationModel;
            assistantMeta = $"telegram-orchestration:{orchestrated.Route}";
            responseText = ApplyThinkPlusToggleNoteIfAny(thinkPlusToggleNote, responseText);
            _conversationStore.AppendMessage(session.Thread.Id, "user", requestText, "telegram:user");
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
                snapshot.MultiNvidiaModel,
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
                ("nvidia", multi.NvidiaText),
                ("copilot", multi.CopilotText),
                ("codex", multi.CodexText),
                ("summary", multi.Summary)
            );
            effectiveGuardFailure = sharedPrepared.GuardFailure;
            var multiSummaryValidated = ApplyListCountFallback(requestText, multi.Summary, sharedPrepared.Citations);
            multiSummaryValidated = ApplySkillCreateDirective(multiSummaryValidated, "telegram");
            multiSummaryValidated = CleanLeakedSystemMarkers(multiSummaryValidated);
            responseText = AppendTelegramResponseFooter(
                FormatTelegramResponse(multiSummaryValidated, TelegramMaxResponseChars),
                "multi",
                "summary",
                telegramStateKey
            );
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
                "nvidia" => multi.NvidiaModel,
                "copilot" => multi.CopilotModel,
                "codex" => multi.CodexModel,
                _ => "-"
            };
            assistantMeta = $"telegram-multi:summary={multi.ResolvedSummaryProvider}";
            responseText = ApplyThinkPlusToggleNoteIfAny(thinkPlusToggleNote, responseText);
            _conversationStore.AppendMessage(session.Thread.Id, "user", requestText, "telegram:user");
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
                                 ?? NormalizeModelSelection(_providers.GroqModel)
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
                responseText = AppendTelegramResponseFooter(
                    providerPrepared.UnsupportedMessage,
                    "groq",
                    preferredModel,
                    telegramStateKey
                );
                responseText = ApplyThinkPlusToggleNoteIfAny(thinkPlusToggleNote, responseText);
                providerForMemory = "groq";
                modelForMemory = preferredModel;
                assistantMeta = $"telegram-single:groq:{preferredModel}:unsupported";
                _conversationStore.AppendMessage(session.Thread.Id, "user", requestText, "telegram:user");
                _conversationStore.AppendMessage(session.Thread.Id, "assistant", responseText, assistantMeta);
                await EnsureConversationTitleFromFirstTurnAsync(session.Thread.Id, providerForMemory, modelForMemory, cancellationToken);
                LogTelegramGuardMeta(assistantMeta);
                CaptureTelegramExecutionMeta();
                return responseText;
            }

            var singleGroq = await ExecuteTelegramGroqSingleAsync(
                requestText,
                providerPrepared.Text,
                snapshot,
                thinkingLevel,
                streamCallback,
                cancellationToken
            );
            if (!shouldSkipDriftRecovery && !_context.EnableFastWebPipeline && ChatRetryGuardPolicy.ShouldRetryWithoutHistory(requestText, singleGroq.Text))
            {
                var historyBypassInput = ChatRetryGuardPolicy.BuildHistoryBypassInput(providerPrepared.Text);
                var recovered = await ExecuteTelegramGroqSingleAsync(
                    requestText,
                    historyBypassInput,
                    snapshot,
                    thinkingLevel,
                    streamCallback,
                    cancellationToken
                );
                if (!string.IsNullOrWhiteSpace(recovered.Text)
                    && !ChatRetryGuardPolicy.ShouldRetryWithoutHistory(requestText, recovered.Text))
                {
                    singleGroq = recovered;
                }
                else
                {
                    var originalRequestInput = ChatRetryGuardPolicy.BuildOriginalRequestRetryInput(requestText);
                    var originalRecovered = await ExecuteTelegramGroqSingleAsync(
                        requestText,
                        originalRequestInput,
                        snapshot,
                        thinkingLevel,
                        streamCallback,
                        cancellationToken
                    );
                    singleGroq = !string.IsNullOrWhiteSpace(originalRecovered.Text)
                                 && !ChatRetryGuardPolicy.ShouldRetryWithoutHistory(requestText, originalRecovered.Text)
                        ? originalRecovered
                        : new LlmSingleChatResult(singleGroq.Provider, singleGroq.Model, ChatRetryGuardPolicy.BuildOffTopicGuardMessage(requestText));
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
            responseText = AppendTelegramResponseFooter(
                FormatTelegramResponse(singleGroqText, TelegramMaxResponseChars),
                singleGroq.Provider,
                singleGroq.Model,
                telegramStateKey
            );
            responseText = ApplyThinkPlusToggleNoteIfAny(thinkPlusToggleNote, responseText);
            providerForMemory = singleGroq.Provider;
            modelForMemory = singleGroq.Model;
            assistantMeta = $"telegram-single:{singleGroq.Provider}:{singleGroq.Model}";
            _conversationStore.AppendMessage(session.Thread.Id, "user", requestText, "telegram:user");
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
            responseText = AppendTelegramResponseFooter(
                providerInput.UnsupportedMessage,
                snapshot.SingleProvider,
                singleModel,
                telegramStateKey
            );
            responseText = ApplyThinkPlusToggleNoteIfAny(thinkPlusToggleNote, responseText);
            providerForMemory = snapshot.SingleProvider;
            modelForMemory = singleModel;
            assistantMeta = $"telegram-single:{snapshot.SingleProvider}:{singleModel}:unsupported";
            _conversationStore.AppendMessage(session.Thread.Id, "user", requestText, "telegram:user");
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
            ChatRetryGuardPolicy.ResolveSingleChatMaxOutputTokens(requestText),
            streamCallback
        );
        if (!shouldSkipDriftRecovery && !_context.EnableFastWebPipeline && ChatRetryGuardPolicy.ShouldRetryWithoutHistory(requestText, single.Text))
        {
            var historyBypassInput = ChatRetryGuardPolicy.BuildHistoryBypassInput(providerInput.Text);
            var recovered = await ChatSingleAsync(
                historyBypassInput,
                snapshot.SingleProvider,
                snapshot.SingleModel,
                "telegram",
                cancellationToken,
                ChatRetryGuardPolicy.ResolveSingleChatMaxOutputTokens(requestText),
                streamCallback
            );
            if (!string.IsNullOrWhiteSpace(recovered.Text)
                && !ChatRetryGuardPolicy.ShouldRetryWithoutHistory(requestText, recovered.Text))
            {
                single = recovered;
            }
            else
            {
                var originalRequestInput = ChatRetryGuardPolicy.BuildOriginalRequestRetryInput(requestText);
                var originalRecovered = await ChatSingleAsync(
                    originalRequestInput,
                    snapshot.SingleProvider,
                    snapshot.SingleModel,
                    "telegram",
                    cancellationToken,
                    ChatRetryGuardPolicy.ResolveSingleChatMaxOutputTokens(requestText),
                    streamCallback
                );
                single = !string.IsNullOrWhiteSpace(originalRecovered.Text)
                         && !ChatRetryGuardPolicy.ShouldRetryWithoutHistory(requestText, originalRecovered.Text)
                    ? originalRecovered
                    : new LlmSingleChatResult(single.Provider, single.Model, ChatRetryGuardPolicy.BuildOffTopicGuardMessage(requestText));
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
        responseText = AppendTelegramResponseFooter(
            FormatTelegramResponse(singleText, TelegramMaxResponseChars),
            single.Provider,
            single.Model,
            telegramStateKey
        );
        responseText = ApplyThinkPlusToggleNoteIfAny(thinkPlusToggleNote, responseText);
        providerForMemory = single.Provider;
        modelForMemory = single.Model;
        assistantMeta = $"telegram-single:{single.Provider}:{single.Model}";
        _conversationStore.AppendMessage(session.Thread.Id, "user", requestText, "telegram:user");
        _conversationStore.AppendMessage(session.Thread.Id, "assistant", responseText, assistantMeta);
        await EnsureConversationTitleFromFirstTurnAsync(session.Thread.Id, providerForMemory, modelForMemory, cancellationToken);
        _ = await MaybeCompressConversationAsync(session.Thread.Id, "chat-single", providerForMemory, modelForMemory, cancellationToken);
        LogTelegramGuardMeta(assistantMeta);
        CaptureTelegramExecutionMeta();
        return responseText;
    }

    // 응답 텍스트 끝에 inline keyboard 버튼 마커를 첨부. TelegramUpdateLoop이 이 마커를 파싱해
    // 본문에서 떼어낸 뒤 callback_data 버튼으로 변환해 전송한다. 마커가 없으면 일반 sendMessage.
    // 형식:
    //   __TG_BUTTONS__
    //   /skill off|🚫 끄기
    //   /skill list|📋 목록
    //   __/TG_BUTTONS__
    internal const string TelegramButtonsMarkerOpen = "__TG_BUTTONS__";
    internal const string TelegramButtonsMarkerClose = "__/TG_BUTTONS__";

    private static string AppendTelegramInlineButtons(string body, params (string Command, string Label)[] buttons)
    {
        if (buttons == null || buttons.Length == 0)
        {
            return body;
        }
        var sb = new StringBuilder();
        sb.Append(body?.TrimEnd() ?? string.Empty);
        sb.Append("\n\n");
        sb.Append(TelegramButtonsMarkerOpen);
        sb.Append('\n');
        foreach (var (cmd, label) in buttons)
        {
            if (string.IsNullOrWhiteSpace(cmd) || string.IsNullOrWhiteSpace(label))
            {
                continue;
            }
            sb.Append(cmd.Trim());
            sb.Append('|');
            sb.Append(label.Trim());
            sb.Append('\n');
        }
        sb.Append(TelegramButtonsMarkerClose);
        return sb.ToString();
    }

    // /history [N] — 텔레그램 thread의 최근 N개 user/assistant 쌍을 압축 요약으로 반환.
    private string? TryHandleTelegramHistorySlashCommand(string text)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (!normalized.StartsWith("/history", StringComparison.OrdinalIgnoreCase)
            && !normalized.StartsWith("/log", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var firstLine = normalized.Split('\n', 2)[0];
        var tokens = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var n = 5;
        if (tokens.Length >= 2 && int.TryParse(tokens[1], out var requested))
        {
            n = Math.Clamp(requested, 1, 20);
        }

        var thread = EnsureTelegramLinkedConversation();
        if (thread.Messages == null || thread.Messages.Count == 0)
        {
            return "대화 기록이 비어 있습니다.";
        }

        // user/assistant 쌍을 뒤에서 모은다.
        var pairs = new List<(string User, string Assistant, DateTimeOffset Stamp)>();
        ConversationMessageView? pendingUser = null;
        for (var i = thread.Messages.Count - 1; i >= 0; i -= 1)
        {
            var msg = thread.Messages[i];
            if (string.Equals(msg.Role, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                pendingUser = msg;
                continue;
            }
            if (string.Equals(msg.Role, "user", StringComparison.OrdinalIgnoreCase) && pendingUser != null)
            {
                pairs.Add((msg.Text, pendingUser.Text, pendingUser.CreatedUtc));
                pendingUser = null;
                if (pairs.Count >= n)
                {
                    break;
                }
            }
        }

        if (pairs.Count == 0)
        {
            return "최근 user/assistant 쌍이 없습니다.";
        }

        var builder = new StringBuilder();
        builder.AppendLine($"📜 최근 대화 {pairs.Count}개:");
        for (var i = pairs.Count - 1; i >= 0; i -= 1)
        {
            var (u, a, stamp) = pairs[i];
            var idx = pairs.Count - i;
            var localStamp = stamp.ToLocalTime().ToString("MM-dd HH:mm");
            builder.AppendLine();
            builder.AppendLine($"#{idx} · {localStamp}");
            builder.AppendLine($"🙂 {TrimHistoryPreview(u, 220)}");
            builder.AppendLine($"🤖 {TrimHistoryPreview(a, 360)}");
        }
        return builder.ToString().TrimEnd();
    }

    private static string TrimHistoryPreview(string text, int maxChars)
    {
        var safe = (text ?? string.Empty).Replace("\r\n", " ").Replace("\n", " ").Trim();
        if (safe.Length <= maxChars) return safe;
        return safe[..maxChars] + "…";
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

    private string? TryBuildLastTelegramAssistantNotebookAppend()
    {
        var thread = EnsureTelegramLinkedConversation();
        var assistant = thread.Messages
            .Where(message => message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(message => message.CreatedUtc)
            .FirstOrDefault();
        if (assistant == null || string.IsNullOrWhiteSpace(assistant.Text))
        {
            return null;
        }

        var lines = new[]
        {
            "텔레그램 답변에서 저장한 내용",
            "",
            $"대화: {thread.Title}",
            $"응답: {assistant.Meta}",
            "",
            TrimForOutput(assistant.Text, 2200)
        };
        return "/notebook append learning " + string.Join("\n", lines).Trim();
    }

    private string? TryBuildLastTelegramAssistantPlanCreate()
    {
        var thread = EnsureTelegramLinkedConversation();
        var assistant = thread.Messages
            .Where(message => message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(message => message.CreatedUtc)
            .FirstOrDefault();
        if (assistant == null || string.IsNullOrWhiteSpace(assistant.Text))
        {
            return null;
        }

        var lines = new[]
        {
            "아래 텔레그램 답변을 실제 실행 가능한 작업계획으로 정리",
            "",
            $"대화: {thread.Title}",
            $"응답: {assistant.Meta}",
            "",
            TrimForOutput(assistant.Text, 2600)
        };
        return "/plan create --constraint 답변의 의도와 범위를 유지하기 --constraint 실행 가능한 단계와 검증 기준을 분리하기 " + string.Join("\n", lines).Trim();
    }

    private string ResolveTelegramStateKey(ConversationThreadView? thread = null)
    {
        var contextualKey = (_executionContext.CurrentTelegramTurn?.SessionKey ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(contextualKey))
        {
            return contextualKey;
        }

        if (thread != null && !string.IsNullOrWhiteSpace(thread.Id))
        {
            return thread.Id;
        }

        return EnsureTelegramLinkedConversation().Id;
    }

    private string BuildTelegramFollowupAwareInput(ConversationThreadView thread, string input)
    {
        return TelegramConversationContextPolicy.BuildFollowupAwareInput(thread, input);
    }

    private static (string? User, string? Assistant) FindTelegramAnchorTurn(ConversationThreadView thread, string currentInput)
    {
        return TelegramConversationContextPolicy.FindAnchorTurn(thread, currentInput);
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
            ?? (string.IsNullOrWhiteSpace(_providers.GroqModel) ? DefaultGroqPrimaryModel : _providers.GroqModel);
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
        return new LlmSingleChatResult(generated.Provider, generated.Model, ChatOutputSanitizerPolicy.Sanitize(generated.Text));
    }

    private async Task<string> PrepareTelegramInputAsync(
        string input,
        CancellationToken cancellationToken,
        bool preserveContext = false
    )
    {
        var text = (input ?? string.Empty).Trim();
        if (preserveContext)
        {
            return BuildTelegramFullFidelityPrompt(text);
        }

        if (text.Length <= TelegramLongContextThresholdChars)
        {
            return BuildTelegramConcisePrompt(text);
        }

        var compressionPrompt = TelegramPromptPolicy.BuildCompressionPrompt(text);
        string compressed;
        if (_llmRouter.HasGroqApiKey())
        {
            var groq = await GenerateByProviderSafeAsync(
                "groq",
                string.IsNullOrWhiteSpace(_providers.GroqModel) ? DefaultGroqPrimaryModel : _providers.GroqModel,
                compressionPrompt,
                cancellationToken,
                700
            );
            compressed = ChatOutputSanitizerPolicy.Sanitize(groq.Text);
        }
        else if (_llmRouter.HasGeminiApiKey())
        {
            var gemini = await GenerateByProviderSafeAsync("gemini", _providers.GeminiModel, compressionPrompt, cancellationToken, 700);
            compressed = ChatOutputSanitizerPolicy.Sanitize(gemini.Text);
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

    private static bool ShouldSkipTelegramDriftRecovery(
        string contextualInput,
        string effectiveTopicInput,
        string preparedInput
    )
    {
        var combined = $"{contextualInput}\n{effectiveTopicInput}\n{preparedInput}";
        return combined.Contains("[최근 대화]", StringComparison.Ordinal)
               || combined.Contains("[직전 주제]", StringComparison.Ordinal)
               || combined.Contains("[정정 요청]", StringComparison.Ordinal)
               || combined.Contains("[사용자 추가 피드백]", StringComparison.Ordinal)
               || combined.Contains("[Project Context]", StringComparison.Ordinal)
               || combined.Contains("[Active Skill", StringComparison.Ordinal)
               || combined.Contains("[Think+ 참고 자료", StringComparison.Ordinal)
               || combined.Contains("[첨부 텍스트 파일]", StringComparison.Ordinal)
               || combined.Contains("[첨부 이미지/파일 분석 요약]", StringComparison.Ordinal)
               || combined.Contains("[웹 컨텍스트]", StringComparison.Ordinal)
               || combined.Contains("[검색 컨텍스트]", StringComparison.Ordinal)
               || combined.Contains("[Forced", StringComparison.Ordinal);
    }

    private static string BuildTelegramProfilePrompt(string concisePrompt, string profile, string thinkingLevel)
    {
        return TelegramPromptPolicy.BuildProfilePrompt(concisePrompt, profile, thinkingLevel, BuildLocalNowText());
    }

    private static string BuildOrchestrationPrompt(
        string userText,
        IReadOnlyList<LlmSingleChatResult> workerResults,
        IReadOnlyDictionary<string, string> roleByProvider
    )
    {
        return TelegramPromptPolicy.BuildOrchestrationPrompt(userText, workerResults, roleByProvider);
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

    private static string TrimForOutput(string text, int limit = 3500) =>
        TextOutputTruncator.TruncateWithMin200(text, limit);

    private static string BuildTelegramConcisePrompt(string input)
    {
        return TelegramPromptPolicy.BuildConcisePrompt(input, BuildLocalNowText());
    }

    private static string BuildTelegramFullFidelityPrompt(string input)
    {
        return TelegramPromptPolicy.BuildFullFidelityPrompt(input, BuildLocalNowText());
    }

    // 응답 본문 끝에 provider·model·active skill 정보를 짧은 footer로 붙인다.
    // 기존의 `[Single groq:gpt-...]` 헤더 대신 본문이 먼저 보이도록 하단으로 이동.
    private string AppendTelegramResponseFooter(
        string body,
        string? provider,
        string? model,
        string? sessionId,
        string? extraLabel = null
    )
    {
        var bodyTrim = (body ?? string.Empty).TrimEnd();
        var parts = new List<string>(4);
        var providerLabel = string.IsNullOrWhiteSpace(provider) ? "—" : provider!.Trim();
        var modelLabel = string.IsNullOrWhiteSpace(model) ? "—" : model!.Trim();
        parts.Add($"{providerLabel}·{modelLabel}");
        if (!string.IsNullOrWhiteSpace(extraLabel))
        {
            parts.Add(extraLabel!.Trim());
        }
        if (!string.IsNullOrWhiteSpace(sessionId)
            && _activeSkillByThread.TryGetValue(sessionId!, out var activeSkill)
            && !string.IsNullOrWhiteSpace(activeSkill))
        {
            parts.Add($"🎯 {activeSkill}");
        }
        return bodyTrim + "\n\n— " + string.Join(" · ", parts);
    }

    private static string FormatTelegramResponse(string text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "응답이 비어 있습니다.";
        }

        const bool keepMarkdownTables = true;
        var sanitized = ChatOutputSanitizerPolicy.Sanitize(text, keepMarkdownTables: keepMarkdownTables);
        return TelegramResponseFormatterPolicy.FormatSanitizedResponse(
            sanitized,
            maxChars,
            ChatOutputSanitizerPolicy.NormalizeStructuredLabelBlocks,
            ChatOutputSanitizerPolicy.IsStandaloneNumberedHeadlineLine,
            ChatOutputSanitizerPolicy.IsMarkdownTableRow
        );
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
                   - /model <groq|gemini|copilot|cerebras|nvidia|codex>
                   - /llm status
                   - /llm models [groq|gemini|copilot|cerebras|nvidia|codex|all]
                   - /llm usage
                   - /llm mode <single|orchestration|multi>
                   - /llm single provider <groq|gemini|copilot|cerebras|nvidia|codex>
                   - /llm single model <model-id>
                   - /llm orchestration provider <auto|groq|gemini|copilot|cerebras|nvidia|codex>
                   - /llm orchestration model <model-id>
                   - /llm multi <groq|gemini|copilot|cerebras|nvidia|codex> <model-id>
                   - /llm multi summary <auto|groq|gemini|copilot|cerebras|nvidia|codex>
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
                   - /skill status — 현재 활성 스킬 확인
                   - /skill list
                   - /skill use <name> [project|global]
                   - /skill get <name> [project|global]
                   - /skill create <name> [project|global]
                     한 줄 설명
                     ---
                     스킬 본문
                   - /skill off
                   - /skill quick <별명> <스킬이름> — 단축 별명 등록 (예: /skill quick e eli5)
                   - /skill quick list — 등록된 별명 목록
                   - /skill quick remove <별명>
                     ↳ 등록 후 /<별명> [질문] 으로 즉시 호출 가능 (예: /e 디지털 카메라 원리)
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
                   - /coding single provider <auto|groq|gemini|copilot|cerebras|nvidia|codex>
                   - /coding single model <model-id>
                   - /coding single run <요구사항>
                   - /coding orchestration provider <auto|groq|gemini|copilot|cerebras|nvidia|codex>
                   - /coding orchestration model <model-id>
                   - /coding orchestration worker <provider> <model-id|none>
                   - /coding orchestration run [요구사항]
                   - /coding multi provider <auto|groq|gemini|copilot|cerebras|nvidia|codex>
                   - /coding multi model <model-id>
                   - /coding multi worker <provider> <model-id|none>
                   - /coding multi run <요구사항>
                   - /coding last
                   - /coding files
                   - /coding file <번호|경로>
                   - /coding download <번호|경로> — 텔레그램 첨부로 다운로드
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

               🎙️ 음성 메시지: 자동 전사(STT) 후 LLM에 전달. 들은 내용을 echo로 확인 후 답변.
               🖼️ 사진 첨부: Vision 모델로 이미지 분석. 캡션이 없으면 "첨부 분석" 자동 안내.
               📎 문서/파일 첨부: PDF/텍스트/코드 등은 모델이 직접 참조해 요약·분석.

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
               - /skill status (또는 /off)
               - /think on|off|status
               - /web on|off|status
               - /history [N]
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
        var baseDir = Path.GetDirectoryName(_paths.LlmUsageStatePath);
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
