using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    private const double NaturalCommandMinConfidence = 0.72d;
    private const int NaturalCommandInterpretMaxTokens = 260;

    private async Task<string?> TryHandleUnifiedSlashCommandAsync(
        string text,
        string source,
        CancellationToken cancellationToken
    )
    {
        var normalized = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized) || !normalized.StartsWith("/", StringComparison.Ordinal))
        {
            return null;
        }

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return null;
        }

        var head = tokens[0].Trim().ToLowerInvariant();
        if (head is "/talk" or "/code")
        {
            var profile = head == "/talk" ? "talk" : "code";
            var thinking = tokens.Length >= 2 ? NormalizeThinkingLevel(tokens[1], "auto") : "auto";
            return ApplyChannelProfile(source, profile, thinking);
        }

        if (head == "/profile")
        {
            if (tokens.Length < 2)
            {
                return "usage: /profile <talk|code> [low|high]";
            }

            var profile = tokens[1].Trim().ToLowerInvariant();
            if (profile is not ("talk" or "code"))
            {
                return "invalid profile. use talk|code";
            }

            var thinking = tokens.Length >= 3 ? NormalizeThinkingLevel(tokens[2], "auto") : "auto";
            return ApplyChannelProfile(source, profile, thinking);
        }

        if (head == "/mode")
        {
            if (tokens.Length < 2)
            {
                return "usage: /mode <single|orchestration|multi>";
            }

            return SetChannelMode(source, tokens[1].Trim().ToLowerInvariant());
        }

        if (head == "/provider")
        {
            if (tokens.Length < 3)
            {
                return "사용법: /provider <single|orchestration|summary> <groq|gemini|copilot|cerebras|nvidia|codex|auto>";
            }

            var slot = tokens[1].Trim().ToLowerInvariant();
            var provider = tokens[2].Trim().ToLowerInvariant();
            return SetChannelProvider(source, slot, provider);
        }

        if (head == "/model")
        {
            if (tokens.Length == 2)
            {
                var quickProvider = tokens[1].Trim().ToLowerInvariant();
                if (quickProvider is "nvidia-nim" or "nvidia_nim" or "nim")
                {
                    quickProvider = "nvidia";
                }

                if (quickProvider is "groq" or "gemini" or "copilot" or "cerebras" or "nvidia" or "codex")
                {
                    return SetChannelProvider(source, "single", quickProvider);
                }
            }

            if (tokens.Length < 3)
            {
                return "사용법: /model <single|orchestration|multi.groq|multi.gemini|multi.copilot|multi.cerebras|multi.nvidia|multi.codex> <model-id>";
            }

            var slot = tokens[1].Trim().ToLowerInvariant();
            var modelId = string.Join(' ', tokens.Skip(2)).Trim();
            return SetChannelModel(source, slot, modelId);
        }

        if (head == "/status")
        {
            if (tokens.Length >= 2 && tokens[1].Trim().Equals("model", StringComparison.OrdinalIgnoreCase))
            {
                return BuildChannelModelStatus(source);
            }

            return "usage: /status model";
        }

        if (head == "/memory")
        {
            if (tokens.Length >= 2 && tokens[1].Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                var result = ClearMemory(source, source);
                return $"메모리를 비웠습니다. {result}";
            }

            if (tokens.Length >= 2 && tokens[1].Equals("create", StringComparison.OrdinalIgnoreCase))
            {
                if (!source.Equals("telegram", StringComparison.OrdinalIgnoreCase))
                {
                    return "메모리 노트 생성은 현재 텔레그램 대화에서만 바로 지원합니다.";
                }

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

            return BuildMemoryCommandHelpText();
        }

        if (head == "/doctor")
        {
            var latestOnly = tokens.Skip(1).Any(token => token.Equals("last", StringComparison.OrdinalIgnoreCase));
            var json = tokens.Skip(1).Any(token => token.Equals("json", StringComparison.OrdinalIgnoreCase));
            return await ExecuteDoctorReportCommandAsync(json, latestOnly, cancellationToken);
        }

        if (head == "/plan")
        {
            return await ExecutePlanSlashCommandAsync(tokens, source, cancellationToken);
        }

        if (head == "/task")
        {
            return await ExecuteTaskSlashCommandAsync(tokens, source, cancellationToken);
        }

        if (head == "/notebook")
        {
            return await ExecuteNotebookSlashCommandAsync(tokens, source, cancellationToken);
        }

        if (head == "/handoff")
        {
            return await ExecuteHandoffSlashCommandAsync(tokens, source, cancellationToken);
        }

        if (head == "/llm")
        {
            return await TryHandleUnifiedLlmCommandAsync(source, tokens, cancellationToken);
        }

        return null;
    }

    private async Task<string?> TryHandleUnifiedLlmCommandAsync(
        string source,
        IReadOnlyList<string> tokens,
        CancellationToken cancellationToken
    )
    {
        if (tokens.Count <= 1 || tokens[1].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            return BuildUnifiedLlmHelpText(source);
        }

        var sub = tokens[1].Trim().ToLowerInvariant();
        if (sub == "status")
        {
            return BuildChannelModelStatus(source);
        }

        if (sub is "usage" or "limits" or "quota")
        {
            return await BuildTelegramUsageReportAsync(cancellationToken);
        }

        if (sub == "mode")
        {
            if (tokens.Count < 3)
            {
                return "사용법: /llm mode <single|orchestration|multi>";
            }

            return SetChannelMode(source, tokens[2].Trim().ToLowerInvariant());
        }

        if (sub == "single")
        {
            if (tokens.Count < 4)
            {
                return "사용법: /llm single provider <groq|gemini|copilot|cerebras|nvidia|codex> | /llm single model <model-id>";
            }

            if (tokens[2].Equals("provider", StringComparison.OrdinalIgnoreCase))
            {
                return SetChannelProvider(source, "single", tokens[3].Trim().ToLowerInvariant());
            }

            if (tokens[2].Equals("model", StringComparison.OrdinalIgnoreCase))
            {
                var model = string.Join(' ', tokens.Skip(3)).Trim();
                return SetChannelModel(source, "single", model);
            }

            return "사용법: /llm single provider <groq|gemini|copilot|cerebras|nvidia|codex> | /llm single model <model-id>";
        }

        if (sub == "orchestration")
        {
            if (tokens.Count < 4)
            {
                return "사용법: /llm orchestration provider <auto|groq|gemini|copilot|cerebras|nvidia|codex> | /llm orchestration model <model-id>";
            }

            if (tokens[2].Equals("provider", StringComparison.OrdinalIgnoreCase))
            {
                return SetChannelProvider(source, "orchestration", tokens[3].Trim().ToLowerInvariant());
            }

            if (tokens[2].Equals("model", StringComparison.OrdinalIgnoreCase))
            {
                var model = string.Join(' ', tokens.Skip(3)).Trim();
                return SetChannelModel(source, "orchestration", model);
            }

            return "사용법: /llm orchestration provider <auto|groq|gemini|copilot|cerebras|nvidia|codex> | /llm orchestration model <model-id>";
        }

        if (sub == "multi")
        {
            if (tokens.Count < 4)
            {
                return "사용법: /llm multi <groq|gemini|copilot|cerebras|nvidia|codex> <model-id> | /llm multi summary <auto|groq|gemini|copilot|cerebras|nvidia|codex>";
            }

            var slot = tokens[2].Trim().ToLowerInvariant();
            if (slot == "summary")
            {
                return SetChannelProvider(source, "summary", tokens[3].Trim().ToLowerInvariant());
            }

            var model = string.Join(' ', tokens.Skip(3)).Trim();
            var canonicalSlot = slot switch
            {
                "groq" => "multi.groq",
                "gemini" => "multi.gemini",
                "copilot" => "multi.copilot",
                "cerebras" => "multi.cerebras",
                "nvidia" or "nvidia-nim" or "nvidia_nim" or "nim" => "multi.nvidia",
                "codex" => "multi.codex",
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(canonicalSlot))
            {
                return "사용법: /llm multi <groq|gemini|copilot|cerebras|nvidia|codex> <model-id> | /llm multi summary <auto|groq|gemini|copilot|cerebras|nvidia|codex>";
            }

            return SetChannelModel(source, canonicalSlot, model);
        }

        if (sub == "models")
        {
            var target = tokens.Count >= 3 ? tokens[2].Trim().ToLowerInvariant() : "all";
            return await BuildTelegramModelsReportAsync(target, cancellationToken);
        }

        if (sub == "set")
        {
            if (tokens.Count < 4)
            {
                return "사용법: /llm set <groq|copilot|nvidia|codex> <model-id>";
            }

            var provider = tokens[2].Trim().ToLowerInvariant();
            var model = string.Join(' ', tokens.Skip(3)).Trim();
            if (provider == "groq")
            {
                var models = await _groqModelCatalog.GetModelsAsync(cancellationToken);
                if (!models.Any(x => x.Id.Equals(model, StringComparison.OrdinalIgnoreCase)))
                {
                    return $"알 수 없는 Groq 모델: {model}";
                }

                _llmRouter.TrySetSelectedGroqModel(model);
                var providerSet = SetChannelProvider(source, "single", "groq");
                if (providerSet.StartsWith("invalid", StringComparison.OrdinalIgnoreCase))
                {
                    return providerSet;
                }

                return SetChannelModel(source, "single", model);
            }

            if (provider == "copilot")
            {
                var models = await _copilotWrapper.GetModelsAsync(cancellationToken);
                if (!models.Any(x => x.Id.Equals(model, StringComparison.OrdinalIgnoreCase)))
                {
                    return $"알 수 없는 Copilot 모델: {model}";
                }

                if (!_copilotWrapper.TrySetSelectedModel(model))
                {
                    return $"Copilot 모델 설정 실패: {model}";
                }

                var providerSet = SetChannelProvider(source, "single", "copilot");
                if (providerSet.StartsWith("invalid", StringComparison.OrdinalIgnoreCase))
                {
                    return providerSet;
                }

                return SetChannelModel(source, "single", model);
            }

            if (provider == "codex")
            {
                var providerSet = SetChannelProvider(source, "single", "codex");
                if (providerSet.StartsWith("지원", StringComparison.OrdinalIgnoreCase)
                    || providerSet.StartsWith("invalid", StringComparison.OrdinalIgnoreCase))
                {
                    return providerSet;
                }

                return SetChannelModel(source, "single", model);
            }

            if (provider == "nvidia" || provider == "nvidia-nim" || provider == "nvidia_nim" || provider == "nim")
            {
                var providerSet = SetChannelProvider(source, "single", "nvidia");
                if (providerSet.StartsWith("지원", StringComparison.OrdinalIgnoreCase)
                    || providerSet.StartsWith("invalid", StringComparison.OrdinalIgnoreCase))
                {
                    return providerSet;
                }

                return SetChannelModel(source, "single", model);
            }

            return "사용법: /llm set <groq|copilot|nvidia|codex> <model-id>";
        }

        return "알 수 없는 /llm 명령입니다. /llm help 또는 자연어 요청을 사용하세요.";
    }

    private async Task<string?> TryHandleNaturalCommandByLlmAsync(
        string source,
        string text,
        IReadOnlyList<InputAttachment>? attachments,
        IReadOnlyList<string>? webUrls,
        bool webSearchEnabled,
        CancellationToken cancellationToken
    )
    {
        var normalized = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith("/", StringComparison.Ordinal))
        {
            return null;
        }

        if (!source.Equals("telegram", StringComparison.OrdinalIgnoreCase)
            && !source.Equals("web", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!ShouldAttemptNaturalCommandInterpretation(normalized))
        {
            return null;
        }

        if (LooksLikeNaturalKillIntent(normalized))
        {
            return "보안 정책상 자연어 종료 요청은 허용되지 않습니다. /kill <pid> 형식으로만 실행할 수 있습니다.";
        }

        var interpretation = await ResolveNaturalCommandInterpretationAsync(source, normalized, cancellationToken);
        if (interpretation == null)
        {
            return null;
        }

        var validation = ValidateNaturalCommandInterpretation(source, interpretation, normalized);
        if (validation.IsChat)
        {
            return null;
        }

        if (!validation.Valid)
        {
            if (validation.Code == "natural_kill_disallowed")
            {
                return validation.Message;
            }

            return null;
        }

        if (validation.Canonical == null || string.IsNullOrWhiteSpace(validation.Canonical.SlashCommand))
        {
            return null;
        }

        var slashCommand = validation.Canonical.SlashCommand.Trim();
        if (!slashCommand.StartsWith("/", StringComparison.Ordinal))
        {
            return null;
        }

        _auditLogger.Log(
            source,
            "natural_command_resolved",
            "ok",
            $"cmd={NormalizeAuditToken(slashCommand, "-")} confidence={interpretation.Confidence.ToString("0.00", CultureInfo.InvariantCulture)}"
        );
        return await ExecuteAsync(slashCommand, source, cancellationToken, attachments, webUrls, webSearchEnabled);
    }

    private async Task<NaturalCommandInterpretation?> ResolveNaturalCommandInterpretationAsync(
        string source,
        string input,
        CancellationToken cancellationToken
    )
    {
        var candidates = await BuildNaturalInterpretCandidatesAsync(source, input, cancellationToken);
        if (candidates.Count == 0)
        {
            return null;
        }

        var prompt = BuildNaturalCommandResolverPrompt(input);
        NaturalCommandInterpretation? best = null;
        foreach (var candidate in candidates)
        {
            try
            {
                var generated = await GenerateByProviderSafeAsync(
                    candidate.Provider,
                    candidate.Model,
                    prompt,
                    cancellationToken,
                    NaturalCommandInterpretMaxTokens,
                    useRawCodexPrompt: true
                );
                if (!TryParseNaturalCommandInterpretation(generated.Text, out var parsed))
                {
                    continue;
                }

                if (parsed.Kind == "command" && parsed.Confidence >= NaturalCommandMinConfidence)
                {
                    return parsed;
                }

                if (best == null || parsed.Confidence > best.Confidence)
                {
                    best = parsed;
                }
            }
            catch
            {
                // 자연어 제어는 실패 시 다음 후보 모델로 우회한다.
            }
        }

        return best;
    }

    private async Task<IReadOnlyList<(string Provider, string Model)>> BuildNaturalInterpretCandidatesAsync(
        string source,
        string input,
        CancellationToken cancellationToken
    )
    {
        var availabilityByProvider = await GetProviderAvailabilityMapAsync(cancellationToken);
        if (source.Equals("telegram", StringComparison.OrdinalIgnoreCase))
        {
            TelegramLlmPreferences snapshot;
            lock (_telegramLlmLock)
            {
                snapshot = _telegramLlmPreferences.Clone();
            }

            return BuildNaturalInterpretCandidates(
                availabilityByProvider,
                snapshot.Mode,
                snapshot.SingleProvider,
                snapshot.SingleModel,
                snapshot.OrchestrationProvider,
                snapshot.OrchestrationModel,
                snapshot.MultiSummaryProvider,
                snapshot.MultiGroqModel,
                snapshot.MultiGeminiModel,
                snapshot.MultiCopilotModel,
                snapshot.MultiCerebrasModel,
                snapshot.MultiNvidiaModel,
                snapshot.MultiCodexModel,
                input
            );
        }

        WebLlmPreferences webSnapshot;
        lock (_webLlmLock)
        {
            webSnapshot = _webLlmPreferences.Clone();
        }

        return BuildNaturalInterpretCandidates(
            availabilityByProvider,
            webSnapshot.Mode,
            webSnapshot.SingleProvider,
            webSnapshot.SingleModel,
            webSnapshot.OrchestrationProvider,
            webSnapshot.OrchestrationModel,
            webSnapshot.MultiSummaryProvider,
            webSnapshot.MultiGroqModel,
            webSnapshot.MultiGeminiModel,
            webSnapshot.MultiCopilotModel,
            webSnapshot.MultiCerebrasModel,
            webSnapshot.MultiNvidiaModel,
            webSnapshot.MultiCodexModel,
            input
        );
    }

    private IReadOnlyList<(string Provider, string Model)> BuildNaturalInterpretCandidates(
        IReadOnlyDictionary<string, ProviderAvailability> availabilityByProvider,
        string mode,
        string singleProvider,
        string singleModel,
        string orchestrationProvider,
        string orchestrationModel,
        string multiSummaryProvider,
        string multiGroqModel,
        string multiGeminiModel,
        string multiCopilotModel,
        string multiCerebrasModel,
        string multiNvidiaModel,
        string multiCodexModel,
        string input
    )
    {
        var normalizedMode = (mode ?? string.Empty).Trim().ToLowerInvariant();
        var preferredProviders = new List<string>();
        if (normalizedMode == "orchestration")
        {
            var provider = NormalizeProvider(orchestrationProvider, allowAuto: true);
            if (provider is "auto" or "none")
            {
                provider = "gemini";
            }
            preferredProviders.Add(provider);
        }
        else if (normalizedMode == "multi")
        {
            var provider = NormalizeProvider(multiSummaryProvider, allowAuto: true);
            if (provider is "auto" or "none")
            {
                provider = "gemini";
            }
            preferredProviders.Add(provider);
        }
        else
        {
            var single = NormalizeProvider(singleProvider, allowAuto: true);
            if (single is "auto" or "none")
            {
                single = "gemini";
            }
            preferredProviders.Add(single);
        }

        foreach (var provider in new[] { "gemini", "groq", "nvidia", "codex", "copilot", "cerebras" })
        {
            if (!preferredProviders.Contains(provider, StringComparer.OrdinalIgnoreCase))
            {
                preferredProviders.Add(provider);
            }
        }

        var candidates = new List<(string Provider, string Model)>();
        foreach (var provider in preferredProviders)
        {
            if (!availabilityByProvider.TryGetValue(provider, out var availability) || !availability.Available)
            {
                continue;
            }

            var model = ResolveNaturalInterpretModelForProvider(
                provider,
                normalizedMode,
                singleProvider,
                singleModel,
                orchestrationProvider,
                orchestrationModel,
                multiGroqModel,
                multiGeminiModel,
                multiCopilotModel,
                multiCerebrasModel,
                multiNvidiaModel,
                multiCodexModel,
                input
            );
            if (!string.IsNullOrWhiteSpace(model))
            {
                candidates.Add((provider, model));
            }
        }

        return candidates.Take(3).ToArray();
    }

    private string ResolveNaturalInterpretModelForProvider(
        string provider,
        string normalizedMode,
        string singleProvider,
        string singleModel,
        string orchestrationProvider,
        string orchestrationModel,
        string multiGroqModel,
        string multiGeminiModel,
        string multiCopilotModel,
        string multiCerebrasModel,
        string multiNvidiaModel,
        string multiCodexModel,
        string input
    )
    {
        if (provider == "groq")
        {
            if (normalizedMode == "multi")
            {
                return NormalizeModelSelection(multiGroqModel) ?? ResolveGroqModelForInput(input, null);
            }

            if (normalizedMode == "orchestration"
                && NormalizeProvider(orchestrationProvider, allowAuto: true) == "groq")
            {
                return ResolveGroqModelForInput(input, orchestrationModel);
            }

            if (NormalizeProvider(singleProvider, allowAuto: true) == "groq")
            {
                return ResolveGroqModelForInput(input, singleModel);
            }

            return ResolveGroqModelForInput(input, null);
        }

        if (normalizedMode == "multi")
        {
            return provider switch
            {
                "gemini" => ResolveModel("gemini", multiGeminiModel),
                "copilot" => ResolveModel("copilot", multiCopilotModel),
                "cerebras" => ResolveModel("cerebras", multiCerebrasModel),
                "nvidia" => ResolveModel("nvidia", multiNvidiaModel),
                "codex" => ResolveModel("codex", multiCodexModel),
                _ => ResolveModel(provider, null)
            };
        }

        if (normalizedMode == "orchestration"
            && NormalizeProvider(orchestrationProvider, allowAuto: true) == provider)
        {
            return ResolveModel(provider, orchestrationModel);
        }

        if (NormalizeProvider(singleProvider, allowAuto: true) == provider)
        {
            return ResolveModel(provider, singleModel);
        }

        return ResolveModel(provider, null);
    }

    private static string BuildNaturalCommandResolverPrompt(string input)
    {
        var user = (input ?? string.Empty).Trim();
        return """
               당신은 omni-node 명령 해석기입니다.
               반드시 JSON 객체 하나만 출력하세요. 코드블록/설명/주석 금지.

               스키마:
               {
                 "kind": "command|chat",
                 "command": "mode.set|provider.set|model.set|profile.set|memory.clear|memory.create|doctor.run|plan.list|plan.get|plan.create|plan.review|plan.approve|plan.run|task.list|task.create|task.status|task.run|task.cancel|task.output|notebook.show|notebook.append|handoff.create|routine.list|routine.create|routine.update|routine.run|routine.runs|routine.detail|routine.resend|routine.on|routine.off|routine.delete|coding.status|coding.run|coding.result|coding.files|coding.file|coding.mode.set|coding.language.set|coding.provider.set|coding.model.set|coding.worker.set|refactor.status|refactor.read|refactor.apply|metrics.get|llm.status|llm.usage|llm.models|help.show|kill.request",
                 "args": {"k":"v"},
                 "confidence": 0.0,
                 "reason": "짧은 근거"
               }

               규칙:
               - 설정/제어 의도가 명확하면 kind=command.
               - 일반 질문/대화면 kind=chat.
               - pid 종료 의도는 command=kill.request 로만 표기.
               - args는 문자열 값만 사용.
               - profile.set args: profile=talk|code, thinking=low|high(선택)
               - mode.set args: mode=single|orchestration|multi
               - provider.set args: slot=single|orchestration|summary, provider=groq|gemini|copilot|cerebras|nvidia|codex|auto
               - model.set args: slot=single|orchestration|multi.groq|multi.gemini|multi.copilot|multi.cerebras|multi.nvidia|multi.codex, model=<id>
               - memory.create args: compact=true|false(선택)
               - doctor.run args: latest=true|false(선택), format=text|json(선택)
               - plan.get/review/approve/run args: plan_id=<id>
               - plan.create args: request=<원문>
               - task.create args: plan_id=<id>
               - task.status/run args: graph_id=<id>
               - task.cancel/output args: graph_id=<id>, task_id=<id>
               - notebook.show args: project_key=<선택>
               - notebook.append args: kind=learning|decision|verification, content=<내용>
               - handoff.create args: project_key=<선택>
               - help.show args: topic=llm|routine|coding|refactor|doctor|plan|task|notebook|memory|natural (선택)
               - llm.models args: target=all|groq|gemini|copilot|cerebras|nvidia|codex(선택)
               - routine.create args: request=<원문>
               - routine.update args: routine_id=<id>, request=<원문>
               - routine.run/runs/on/off/delete args: routine_id=<id>
               - routine.detail/resend args: routine_id=<id>, ts=<실행시각 숫자>
               - coding.status/result/files args 없음
               - coding.file args: query=<번호 또는 경로>
               - coding.run args: mode=single|orchestration|multi(선택), request=<요구사항>
               - coding.mode.set args: mode=single|orchestration|multi
               - coding.language.set args: mode=single|orchestration|multi(선택), language=<language|auto>
               - coding.provider.set args: mode=single|orchestration|multi, provider=auto|groq|gemini|copilot|cerebras|nvidia|codex
               - coding.model.set args: mode=single|orchestration|multi, model=<id>
               - coding.worker.set args: mode=orchestration|multi, provider=groq|gemini|copilot|cerebras|nvidia|codex, model=<id|none>
               - refactor.status args 없음
               - refactor.read args: path=<상대경로 또는 절대경로>, start=<선택>, end=<선택>
               - refactor.apply args: preview_id=<선택>
               - confidence는 0~1

               사용자 입력:
               """
               + user;
    }

    private static bool TryParseNaturalCommandInterpretation(string raw, out NaturalCommandInterpretation interpretation)
    {
        interpretation = new NaturalCommandInterpretation("chat", string.Empty, new Dictionary<string, string>(), 0d, string.Empty);
        if (!TryExtractJsonObject(raw, out var json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(NormalizeNaturalCommandJson(json));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var kind = TryReadJsonString(root, "kind");
            var command = TryReadJsonString(root, "command");
            var reason = TryReadJsonString(root, "reason");
            var confidence = TryReadJsonDouble(root, "confidence");
            var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (TryGetPropertyIgnoreCase(root, "args", out var argsElement)
                && argsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in argsElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        args[prop.Name] = (prop.Value.GetString() ?? string.Empty).Trim();
                        continue;
                    }

                    if (prop.Value.ValueKind == JsonValueKind.Number)
                    {
                        args[prop.Name] = prop.Value.GetRawText();
                        continue;
                    }

                    if (prop.Value.ValueKind == JsonValueKind.True || prop.Value.ValueKind == JsonValueKind.False)
                    {
                        args[prop.Name] = prop.Value.GetBoolean() ? "true" : "false";
                    }
                }
            }

            var normalizedKind = (kind ?? string.Empty).Trim().ToLowerInvariant();
            if (normalizedKind is not ("command" or "chat"))
            {
                normalizedKind = string.IsNullOrWhiteSpace(command) ? "chat" : "command";
            }

            if (confidence <= 0d)
            {
                confidence = normalizedKind == "command" ? 0.51d : 0.99d;
            }

            interpretation = new NaturalCommandInterpretation(
                normalizedKind,
                (command ?? string.Empty).Trim().ToLowerInvariant(),
                args,
                Math.Clamp(confidence, 0d, 1d),
                (reason ?? string.Empty).Trim()
            );
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeNaturalCommandJson(string json)
    {
        var normalized = (json ?? string.Empty).Trim();
        normalized = normalized
            .Replace('“', '"')
            .Replace('”', '"')
            .Replace('’', '\'');
        normalized = JsonTrailingCommaRegex.Replace(normalized, "$1");
        return normalized;
    }

    private static bool TryExtractJsonObject(string raw, out string json)
    {
        json = string.Empty;
        var normalized = (raw ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        var fence = CodeFenceRegex.Match(normalized);
        if (fence.Success && fence.Groups.Count >= 3)
        {
            normalized = fence.Groups[2].Value.Trim();
        }

        var start = normalized.IndexOf('{');
        var end = normalized.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return false;
        }

        json = normalized[start..(end + 1)];
        return true;
    }

    private static string? TryReadJsonString(JsonElement root, string property)
    {
        if (!TryGetPropertyIgnoreCase(root, property, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static double TryReadJsonDouble(JsonElement root, string property)
    {
        if (!TryGetPropertyIgnoreCase(root, property, out var value))
        {
            return 0d;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var numeric))
        {
            return numeric;
        }

        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0d;
    }

    private static NaturalCommandValidationResult ValidateNaturalCommandInterpretation(
        string source,
        NaturalCommandInterpretation interpretation,
        string rawInput
    )
    {
        if (interpretation == null)
        {
            return new NaturalCommandValidationResult(false, true, null, "empty", "empty interpretation");
        }

        if (interpretation.Kind == "chat")
        {
            return new NaturalCommandValidationResult(false, true, null, "chat", "chat intent");
        }

        if (interpretation.Confidence < NaturalCommandMinConfidence)
        {
            return new NaturalCommandValidationResult(false, false, null, "low_confidence", "confidence too low");
        }

        var command = NormalizeNaturalCommandKey(interpretation.Command);
        var args = interpretation.Args ?? new Dictionary<string, string>();

        if (command.StartsWith("routine.", StringComparison.Ordinal)
            && !ContainsExplicitRoutineKeyword(rawInput))
        {
            return new NaturalCommandValidationResult(false, false, null, "routine_keyword_required", "루틴 키워드가 필요합니다.");
        }

        string GetArg(params string[] keys)
        {
            foreach (var key in keys)
            {
                if (args.TryGetValue(key, out var found) && !string.IsNullOrWhiteSpace(found))
                {
                    return found.Trim();
                }
            }

            return string.Empty;
        }

        switch (command)
        {
            case "profile.set":
            {
                if (!ContainsExplicitProfileControlIntent(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "profile_keyword_required", "프로필 키워드가 필요합니다.");
                }

                var profile = GetArg("profile", "name", "value").ToLowerInvariant();
                if (profile is not ("talk" or "code"))
                {
                    return new NaturalCommandValidationResult(false, false, null, "invalid_profile", "invalid profile");
                }

                var thinking = GetArg("thinking", "level").ToLowerInvariant();
                if (thinking is "low" or "high")
                {
                    return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, $"/profile {profile} {thinking}"), "ok", string.Empty);
                }

                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, $"/profile {profile}"), "ok", string.Empty);
            }
            case "mode.set":
            {
                if (!ContainsExplicitModeControlIntent(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "mode_keyword_required", "모드 변경 키워드가 필요합니다.");
                }

                var mode = GetArg("mode", "value").ToLowerInvariant();
                if (mode is not ("single" or "orchestration" or "multi"))
                {
                    return new NaturalCommandValidationResult(false, false, null, "invalid_mode", "invalid mode");
                }

                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, $"/mode {mode}"), "ok", string.Empty);
            }
            case "provider.set":
            {
                var slot = GetArg("slot", "target").ToLowerInvariant();
                var provider = GetArg("provider", "value").ToLowerInvariant();
                if (!ContainsExplicitProviderControlIntent(rawInput, slot))
                {
                    return new NaturalCommandValidationResult(false, false, null, "provider_keyword_required", "제공자 변경 키워드가 필요합니다.");
                }

                if (slot is not ("single" or "orchestration" or "summary"))
                {
                    return new NaturalCommandValidationResult(false, false, null, "invalid_provider_slot", "invalid provider slot");
                }

                if (provider is not ("groq" or "gemini" or "copilot" or "cerebras" or "nvidia" or "codex" or "auto"))
                {
                    return new NaturalCommandValidationResult(false, false, null, "invalid_provider", "invalid provider");
                }

                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, $"/provider {slot} {provider}"), "ok", string.Empty);
            }
            case "model.set":
            {
                if (!ContainsExplicitModelControlIntent(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "model_keyword_required", "모델 변경 키워드가 필요합니다.");
                }

                var slot = GetArg("slot", "target").ToLowerInvariant();
                var model = GetArg("model", "value");
                if (slot is not ("single" or "orchestration" or "multi.groq" or "multi.gemini" or "multi.copilot" or "multi.cerebras" or "multi.nvidia" or "multi.codex"))
                {
                    return new NaturalCommandValidationResult(false, false, null, "invalid_model_slot", "invalid model slot");
                }

                if (string.IsNullOrWhiteSpace(model))
                {
                    return new NaturalCommandValidationResult(false, false, null, "missing_model", "model is required");
                }

                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, $"/model {slot} {model}"), "ok", string.Empty);
            }
            case "memory.clear":
                if (!ContainsExplicitMemoryKeyword(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "memory_keyword_required", "메모리 키워드가 필요합니다.");
                }

                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, "/memory clear"), "ok", string.Empty);
            case "memory.create":
            {
                if (!ContainsExplicitMemoryKeyword(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "memory_keyword_required", "메모리 키워드가 필요합니다.");
                }

                var compact = GetArg("compact", "mode", "style").ToLowerInvariant();
                return compact is "true" or "compact" or "yes"
                    ? new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, "/memory create compact"), "ok", string.Empty)
                    : new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, "/memory create"), "ok", string.Empty);
            }
            case "doctor.run":
            {
                if (!ContainsExplicitDoctorIntent(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "doctor_keyword_required", "진단 키워드가 필요합니다.");
                }

                var latest = GetArg("latest", "last").ToLowerInvariant();
                var format = GetArg("format", "output").ToLowerInvariant();
                var parts = new List<string> { "/doctor" };
                if (latest is "true" or "last" or "latest")
                {
                    parts.Add("last");
                }

                if (format == "json")
                {
                    parts.Add("json");
                }

                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, string.Join(' ', parts)), "ok", string.Empty);
            }
            case "plan.list":
                if (!ContainsExplicitPlanKeyword(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "plan_keyword_required", "계획 키워드가 필요합니다.");
                }

                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, "/plan list"), "ok", string.Empty);
            case "plan.get":
            case "plan.review":
            case "plan.approve":
            case "plan.run":
            {
                if (!ContainsExplicitPlanKeyword(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "plan_keyword_required", "계획 키워드가 필요합니다.");
                }

                var planId = GetArg("plan_id", "id", "value");
                if (string.IsNullOrWhiteSpace(planId))
                {
                    return new NaturalCommandValidationResult(false, false, null, "missing_plan_id", "plan id is required");
                }

                var action = command.Split('.')[1];
                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, $"/plan {action} {planId}"), "ok", string.Empty);
            }
            case "plan.create":
            {
                if (!ContainsExplicitPlanKeyword(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "plan_keyword_required", "계획 키워드가 필요합니다.");
                }

                var request = GetArg("request", "objective", "text", "value");
                if (string.IsNullOrWhiteSpace(request))
                {
                    return new NaturalCommandValidationResult(false, false, null, "missing_request", "plan request is required");
                }

                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, $"/plan create {request}"), "ok", string.Empty);
            }
            case "task.list":
                if (!ContainsExplicitTaskKeyword(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "task_keyword_required", "작업 키워드가 필요합니다.");
                }

                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, "/task list"), "ok", string.Empty);
            case "task.create":
            {
                if (!ContainsExplicitTaskKeyword(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "task_keyword_required", "작업 키워드가 필요합니다.");
                }

                var planId = GetArg("plan_id", "id", "value");
                if (string.IsNullOrWhiteSpace(planId))
                {
                    return new NaturalCommandValidationResult(false, false, null, "missing_plan_id", "plan id is required");
                }

                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, $"/task create {planId}"), "ok", string.Empty);
            }
            case "task.status":
            case "task.run":
            {
                if (!ContainsExplicitTaskKeyword(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "task_keyword_required", "작업 키워드가 필요합니다.");
                }

                var graphId = GetArg("graph_id", "id", "value");
                if (string.IsNullOrWhiteSpace(graphId))
                {
                    return new NaturalCommandValidationResult(false, false, null, "missing_graph_id", "graph id is required");
                }

                var action = command == "task.status" ? "status" : "run";
                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, $"/task {action} {graphId}"), "ok", string.Empty);
            }
            case "task.cancel":
            case "task.output":
            {
                if (!ContainsExplicitTaskKeyword(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "task_keyword_required", "작업 키워드가 필요합니다.");
                }

                var graphId = GetArg("graph_id", "graph", "id");
                var taskId = GetArg("task_id", "task", "value");
                if (string.IsNullOrWhiteSpace(graphId) || string.IsNullOrWhiteSpace(taskId))
                {
                    return new NaturalCommandValidationResult(false, false, null, "missing_task_target", "graph id와 task id가 필요합니다.");
                }

                var action = command == "task.cancel" ? "cancel" : "output";
                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, $"/task {action} {graphId} {taskId}"), "ok", string.Empty);
            }
            case "notebook.show":
            {
                if (!ContainsExplicitNotebookKeyword(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "notebook_keyword_required", "노트북 키워드가 필요합니다.");
                }

                var projectKey = GetArg("project_key", "project", "value");
                return string.IsNullOrWhiteSpace(projectKey)
                    ? new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, "/notebook show"), "ok", string.Empty)
                    : new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, $"/notebook show {projectKey}"), "ok", string.Empty);
            }
            case "notebook.append":
            {
                if (!ContainsExplicitNotebookKeyword(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "notebook_keyword_required", "노트북 키워드가 필요합니다.");
                }

                var kind = GetArg("kind", "type").ToLowerInvariant();
                var content = GetArg("content", "text", "value");
                if (kind is not ("learning" or "decision" or "verification"))
                {
                    return new NaturalCommandValidationResult(false, false, null, "invalid_notebook_kind", "invalid notebook kind");
                }

                if (string.IsNullOrWhiteSpace(content))
                {
                    return new NaturalCommandValidationResult(false, false, null, "missing_notebook_content", "notebook content is required");
                }

                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, $"/notebook append {kind} {content}"), "ok", string.Empty);
            }
            case "handoff.create":
            {
                if (!ContainsExplicitHandoffKeyword(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "handoff_keyword_required", "handoff 키워드가 필요합니다.");
                }

                var projectKey = GetArg("project_key", "project", "value");
                return string.IsNullOrWhiteSpace(projectKey)
                    ? new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, "/handoff"), "ok", string.Empty)
                    : new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, $"/handoff {projectKey}"), "ok", string.Empty);
            }
            case "routine.list":
                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, "/routine list"), "ok", string.Empty);
            case "routine.create":
            {
                var request = GetArg("request", "text", "value");
                if (string.IsNullOrWhiteSpace(request))
                {
                    return new NaturalCommandValidationResult(false, false, null, "missing_request", "routine request is required");
                }

                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, $"/routine create {request}"), "ok", string.Empty);
            }
            case "routine.update":
            {
                var id = GetArg("routine_id", "id", "value");
                var request = GetArg("request", "text", "value");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(request))
                {
                    return new NaturalCommandValidationResult(false, false, null, "missing_routine_update_target", "routine id와 요청이 필요합니다.");
                }

                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, $"/routine update {id} {request}"), "ok", string.Empty);
            }
            case "routine.run":
            case "routine.runs":
            case "routine.on":
            case "routine.off":
            case "routine.delete":
            {
                var id = GetArg("routine_id", "id", "value");
                if (string.IsNullOrWhiteSpace(id))
                {
                    return new NaturalCommandValidationResult(false, false, null, "missing_routine_id", "routine id is required");
                }

                var action = command.Split('.')[1];
                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, $"/routine {action} {id}"), "ok", string.Empty);
            }
            case "routine.detail":
            case "routine.resend":
            {
                var id = GetArg("routine_id", "id", "value");
                var ts = GetArg("ts", "run_ts", "timestamp", "value");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(ts))
                {
                    return new NaturalCommandValidationResult(false, false, null, "missing_routine_run_target", "routine id와 ts가 필요합니다.");
                }

                if (!long.TryParse(ts, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    return new NaturalCommandValidationResult(false, false, null, "invalid_routine_ts", "routine ts는 숫자여야 합니다.");
                }

                var action = command.Split('.')[1];
                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, $"/routine {action} {id} {ts}"), "ok", string.Empty);
            }
            case "coding.status":
            {
                if (!source.Equals("telegram", StringComparison.OrdinalIgnoreCase))
                {
                    return new NaturalCommandValidationResult(false, false, null, "telegram_only", "coding 제어는 현재 텔레그램에서만 자연어 명령으로 직접 지원합니다.");
                }

                if (!ContainsExplicitCodingKeyword(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "coding_keyword_required", "코딩 키워드가 필요합니다.");
                }

                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, "/coding status"), "ok", string.Empty);
            }
            case "coding.run":
            {
                if (!source.Equals("telegram", StringComparison.OrdinalIgnoreCase))
                {
                    return new NaturalCommandValidationResult(false, false, null, "telegram_only", "coding 제어는 현재 텔레그램에서만 자연어 명령으로 직접 지원합니다.");
                }

                if (!ContainsExplicitCodingRunIntent(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "coding_run_keyword_required", "코딩 실행 키워드가 필요합니다.");
                }

                var mode = NormalizeCodingNaturalMode(GetArg("mode", "target", "slot", "value"));
                var request = GetArg("request", "text", "input", "value");
                if (string.IsNullOrWhiteSpace(request) && mode != "orchestration")
                {
                    return new NaturalCommandValidationResult(false, false, null, "missing_coding_request", "코딩 요구사항이 필요합니다.");
                }

                var slash = string.IsNullOrWhiteSpace(mode)
                    ? string.IsNullOrWhiteSpace(request)
                        ? "/coding run"
                        : $"/coding run {request}"
                    : string.IsNullOrWhiteSpace(request)
                        ? $"/coding {mode} run"
                        : $"/coding {mode} run {request}";
                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, slash), "ok", string.Empty);
            }
            case "coding.result":
            {
                if (!source.Equals("telegram", StringComparison.OrdinalIgnoreCase))
                {
                    return new NaturalCommandValidationResult(false, false, null, "telegram_only", "coding 제어는 현재 텔레그램에서만 자연어 명령으로 직접 지원합니다.");
                }

                if (!ContainsExplicitCodingResultIntent(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "coding_result_keyword_required", "최근 코딩 결과 키워드가 필요합니다.");
                }

                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, "/coding last"), "ok", string.Empty);
            }
            case "coding.files":
            {
                if (!source.Equals("telegram", StringComparison.OrdinalIgnoreCase))
                {
                    return new NaturalCommandValidationResult(false, false, null, "telegram_only", "coding 제어는 현재 텔레그램에서만 자연어 명령으로 직접 지원합니다.");
                }

                if (!ContainsExplicitCodingFilesIntent(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "coding_files_keyword_required", "코딩 파일 목록 키워드가 필요합니다.");
                }

                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, "/coding files"), "ok", string.Empty);
            }
            case "coding.file":
            {
                if (!source.Equals("telegram", StringComparison.OrdinalIgnoreCase))
                {
                    return new NaturalCommandValidationResult(false, false, null, "telegram_only", "coding 제어는 현재 텔레그램에서만 자연어 명령으로 직접 지원합니다.");
                }

                if (!ContainsExplicitCodingFileIntent(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "coding_file_keyword_required", "코딩 파일 키워드가 필요합니다.");
                }

                var query = GetArg("query", "path", "file", "index", "value");
                if (string.IsNullOrWhiteSpace(query))
                {
                    return new NaturalCommandValidationResult(false, false, null, "missing_coding_file_query", "파일 번호나 경로가 필요합니다.");
                }

                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, $"/coding file {query}"), "ok", string.Empty);
            }
            case "coding.mode.set":
            {
                if (!source.Equals("telegram", StringComparison.OrdinalIgnoreCase))
                {
                    return new NaturalCommandValidationResult(false, false, null, "telegram_only", "coding 제어는 현재 텔레그램에서만 자연어 명령으로 직접 지원합니다.");
                }

                if (!ContainsExplicitCodingModeControlIntent(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "coding_mode_keyword_required", "코딩 모드 변경 키워드가 필요합니다.");
                }

                var mode = NormalizeCodingNaturalMode(GetArg("mode", "value"));
                if (string.IsNullOrWhiteSpace(mode))
                {
                    return new NaturalCommandValidationResult(false, false, null, "invalid_coding_mode", "invalid coding mode");
                }

                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, $"/coding mode {mode}"), "ok", string.Empty);
            }
            case "coding.language.set":
            {
                if (!source.Equals("telegram", StringComparison.OrdinalIgnoreCase))
                {
                    return new NaturalCommandValidationResult(false, false, null, "telegram_only", "coding 제어는 현재 텔레그램에서만 자연어 명령으로 직접 지원합니다.");
                }

                if (!ContainsExplicitCodingLanguageIntent(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "coding_language_keyword_required", "코딩 언어 변경 키워드가 필요합니다.");
                }

                var mode = NormalizeCodingNaturalMode(GetArg("mode", "target", "slot"));
                var language = GetArg("language", "value");
                if (string.IsNullOrWhiteSpace(language))
                {
                    return new NaturalCommandValidationResult(false, false, null, "missing_coding_language", "language is required");
                }

                var slash = string.IsNullOrWhiteSpace(mode)
                    ? $"/coding language {language}"
                    : $"/coding language {mode} {language}";
                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, slash), "ok", string.Empty);
            }
            case "coding.provider.set":
            {
                if (!source.Equals("telegram", StringComparison.OrdinalIgnoreCase))
                {
                    return new NaturalCommandValidationResult(false, false, null, "telegram_only", "coding 제어는 현재 텔레그램에서만 자연어 명령으로 직접 지원합니다.");
                }

                if (!ContainsExplicitCodingProviderControlIntent(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "coding_provider_keyword_required", "코딩 제공자 변경 키워드가 필요합니다.");
                }

                var mode = NormalizeCodingNaturalMode(GetArg("mode", "target", "slot", "value"));
                var provider = NormalizeProvider(GetArg("provider", "value"), allowAuto: true);
                if (string.IsNullOrWhiteSpace(mode))
                {
                    return new NaturalCommandValidationResult(false, false, null, "invalid_coding_mode", "invalid coding mode");
                }

                if (provider is not ("auto" or "groq" or "gemini" or "copilot" or "cerebras" or "nvidia" or "codex"))
                {
                    return new NaturalCommandValidationResult(false, false, null, "invalid_provider", "invalid provider");
                }

                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, $"/coding {mode} provider {provider}"), "ok", string.Empty);
            }
            case "coding.model.set":
            {
                if (!source.Equals("telegram", StringComparison.OrdinalIgnoreCase))
                {
                    return new NaturalCommandValidationResult(false, false, null, "telegram_only", "coding 제어는 현재 텔레그램에서만 자연어 명령으로 직접 지원합니다.");
                }

                if (!ContainsExplicitCodingModelControlIntent(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "coding_model_keyword_required", "코딩 모델 변경 키워드가 필요합니다.");
                }

                var mode = NormalizeCodingNaturalMode(GetArg("mode", "target", "slot", "value"));
                var model = GetArg("model", "value");
                if (string.IsNullOrWhiteSpace(mode))
                {
                    return new NaturalCommandValidationResult(false, false, null, "invalid_coding_mode", "invalid coding mode");
                }

                if (string.IsNullOrWhiteSpace(model))
                {
                    return new NaturalCommandValidationResult(false, false, null, "missing_model", "model is required");
                }

                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, $"/coding {mode} model {model}"), "ok", string.Empty);
            }
            case "coding.worker.set":
            {
                if (!source.Equals("telegram", StringComparison.OrdinalIgnoreCase))
                {
                    return new NaturalCommandValidationResult(false, false, null, "telegram_only", "coding 제어는 현재 텔레그램에서만 자연어 명령으로 직접 지원합니다.");
                }

                if (!ContainsExplicitCodingWorkerControlIntent(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "coding_worker_keyword_required", "코딩 워커 변경 키워드가 필요합니다.");
                }

                var mode = NormalizeCodingNaturalMode(GetArg("mode", "target", "slot", "value"));
                var provider = NormalizeProvider(GetArg("provider", "value"), allowAuto: false);
                var model = GetArg("model", "value");
                if (mode is not ("orchestration" or "multi"))
                {
                    return new NaturalCommandValidationResult(false, false, null, "invalid_coding_worker_mode", "worker는 orchestration 또는 multi 모드만 지원합니다.");
                }

                if (provider is not ("groq" or "gemini" or "copilot" or "cerebras" or "nvidia" or "codex"))
                {
                    return new NaturalCommandValidationResult(false, false, null, "invalid_provider", "invalid provider");
                }

                if (string.IsNullOrWhiteSpace(model))
                {
                    return new NaturalCommandValidationResult(false, false, null, "missing_model", "model is required");
                }

                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, $"/coding {mode} worker {provider} {model}"), "ok", string.Empty);
            }
            case "refactor.status":
            {
                if (!source.Equals("telegram", StringComparison.OrdinalIgnoreCase))
                {
                    return new NaturalCommandValidationResult(false, false, null, "telegram_only", "refactor 제어는 현재 텔레그램에서만 자연어 명령으로 직접 지원합니다.");
                }

                if (!ContainsExplicitRefactorKeyword(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "refactor_keyword_required", "리팩터 키워드가 필요합니다.");
                }

                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, "/refactor status"), "ok", string.Empty);
            }
            case "refactor.read":
            {
                if (!source.Equals("telegram", StringComparison.OrdinalIgnoreCase))
                {
                    return new NaturalCommandValidationResult(false, false, null, "telegram_only", "refactor 제어는 현재 텔레그램에서만 자연어 명령으로 직접 지원합니다.");
                }

                if (!ContainsExplicitRefactorReadIntent(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "refactor_read_keyword_required", "리팩터 읽기 키워드가 필요합니다.");
                }

                var path = GetArg("path", "file", "value");
                var start = GetArg("start", "line_start", "from");
                var end = GetArg("end", "line_end", "to");
                if (string.IsNullOrWhiteSpace(path))
                {
                    return new NaturalCommandValidationResult(false, false, null, "missing_refactor_path", "path is required");
                }

                var parts = new List<string> { "/refactor", "read", path };
                if (!string.IsNullOrWhiteSpace(start))
                {
                    parts.Add(start);
                }

                if (!string.IsNullOrWhiteSpace(end))
                {
                    parts.Add(end);
                }

                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, string.Join(' ', parts)), "ok", string.Empty);
            }
            case "refactor.apply":
            {
                if (!source.Equals("telegram", StringComparison.OrdinalIgnoreCase))
                {
                    return new NaturalCommandValidationResult(false, false, null, "telegram_only", "refactor 제어는 현재 텔레그램에서만 자연어 명령으로 직접 지원합니다.");
                }

                if (!ContainsExplicitRefactorApplyIntent(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "refactor_apply_keyword_required", "리팩터 적용 키워드가 필요합니다.");
                }

                var previewId = GetArg("preview_id", "preview", "id", "value");
                return string.IsNullOrWhiteSpace(previewId)
                    ? new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, "/refactor apply"), "ok", string.Empty)
                    : new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, $"/refactor apply {previewId}"), "ok", string.Empty);
            }
            case "metrics.get":
                if (!ContainsExplicitMetricsIntent(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "metrics_keyword_required", "메트릭 키워드가 필요합니다.");
                }

                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, "/metrics"), "ok", string.Empty);
            case "llm.status":
                if (!ContainsExplicitLlmStatusIntent(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "llm_status_keyword_required", "LLM 상태 확인 키워드가 필요합니다.");
                }

                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, "/llm status"), "ok", string.Empty);
            case "llm.usage":
                if (!ContainsExplicitLlmUsageIntent(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "llm_usage_keyword_required", "LLM 사용량 키워드가 필요합니다.");
                }

                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, "/llm usage"), "ok", string.Empty);
            case "llm.models":
            {
                if (!ContainsExplicitLlmModelsIntent(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "llm_models_keyword_required", "모델 목록 키워드가 필요합니다.");
                }

                var target = GetArg("target", "provider", "value").ToLowerInvariant();
                if (target is "groq" or "gemini" or "copilot" or "cerebras" or "codex")
                {
                    return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, $"/llm models {target}"), "ok", string.Empty);
                }

                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, "/llm models"), "ok", string.Empty);
            }
            case "help.show":
            {
                if (!ContainsExplicitHelpIntent(rawInput))
                {
                    return new NaturalCommandValidationResult(false, false, null, "help_keyword_required", "도움말 키워드가 필요합니다.");
                }

                var topic = GetArg("topic", "value").ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(topic))
                {
                    return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, "/help"), "ok", string.Empty);
                }

                if (topic is "llm" or "routine" or "coding" or "refactor" or "doctor" or "plan" or "task" or "notebook" or "memory" or "natural")
                {
                    return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, $"/help {topic}"), "ok", string.Empty);
                }

                return new NaturalCommandValidationResult(true, false, new CanonicalCommand(command, "/help"), "ok", string.Empty);
            }
            case "kill.request":
                return new NaturalCommandValidationResult(
                    false,
                    false,
                    null,
                    "natural_kill_disallowed",
                    "보안 정책상 자연어 종료 요청은 허용되지 않습니다. /kill <pid> 형식으로만 실행할 수 있습니다."
                );
            default:
                return new NaturalCommandValidationResult(false, false, null, "unknown_command", "unknown command");
        }
    }

    private static string NormalizeNaturalCommandKey(string command)
    {
        var normalized = (command ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "mode" => "mode.set",
            "provider" => "provider.set",
            "model" => "model.set",
            "profile" => "profile.set",
            "memory" => "memory.clear",
            "doctor" => "doctor.run",
            "plan" => "plan.list",
            "task" => "task.list",
            "notebook" => "notebook.show",
            "handoff" => "handoff.create",
            "routine" => "routine.list",
            "coding" => "coding.status",
            "refactor" => "refactor.status",
            "metrics" => "metrics.get",
            "status" => "llm.status",
            "usage" => "llm.usage",
            "models" => "llm.models",
            "help" => "help.show",
            "kill" => "kill.request",
            _ => normalized
        };
    }

    private static bool LooksLikeNaturalKillIntent(string text)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return Regex.IsMatch(
            normalized,
            @"(?:pid|프로세스|process)\s*([0-9]{2,}).*(?:종료|kill|중지)|(?:종료|kill|중지).*(?:pid|프로세스|process)\s*([0-9]{2,})",
            RegexOptions.CultureInvariant
        );
    }

    private static bool ShouldAttemptNaturalCommandInterpretation(string text)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0 || normalized.StartsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        return LooksLikeNaturalKillIntent(normalized)
            || ContainsExplicitMemoryKeyword(normalized)
            || ContainsExplicitDoctorIntent(normalized)
            || ContainsExplicitPlanKeyword(normalized)
            || ContainsExplicitTaskKeyword(normalized)
            || ContainsExplicitNotebookKeyword(normalized)
            || ContainsExplicitHandoffKeyword(normalized)
            || ContainsExplicitProfileControlIntent(normalized)
            || ContainsExplicitModeControlIntent(normalized)
            || ContainsExplicitProviderControlIntent(normalized, "single")
            || ContainsExplicitProviderControlIntent(normalized, "summary")
            || ContainsExplicitModelControlIntent(normalized)
            || ContainsExplicitRoutineKeyword(normalized)
            || ContainsExplicitCodingRunIntent(normalized)
            || ContainsExplicitCodingResultIntent(normalized)
            || ContainsExplicitCodingFilesIntent(normalized)
            || ContainsExplicitCodingFileIntent(normalized)
            || ContainsExplicitCodingModeControlIntent(normalized)
            || ContainsExplicitCodingLanguageIntent(normalized)
            || ContainsExplicitCodingProviderControlIntent(normalized)
            || ContainsExplicitCodingModelControlIntent(normalized)
            || ContainsExplicitCodingWorkerControlIntent(normalized)
            || ContainsExplicitRefactorKeyword(normalized)
            || ContainsExplicitMetricsIntent(normalized)
            || ContainsExplicitLlmStatusIntent(normalized)
            || ContainsExplicitLlmUsageIntent(normalized)
            || ContainsExplicitLlmModelsIntent(normalized)
            || ContainsExplicitHelpIntent(normalized);
    }

    private static bool ContainsExplicitMemoryKeyword(string text)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        return normalized.Contains("메모리", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("memory", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsExplicitDoctorIntent(string text)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsAny(normalized, "doctor", "진단", "점검", "상태 점검", "health check");
    }

    private static bool ContainsExplicitPlanKeyword(string text)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsAny(normalized, "plan", "planning", "계획", "기획");
    }

    private static bool ContainsExplicitTaskKeyword(string text)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsAny(normalized, "task", "tasks", "task graph", "작업", "태스크", "그래프");
    }

    private static bool ContainsExplicitNotebookKeyword(string text)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsAny(
            normalized,
            "notebook",
            "노트북",
            "learning",
            "decision",
            "verification",
            "학습 기록",
            "결정 기록",
            "검증 기록"
        );
    }

    private static bool ContainsExplicitHandoffKeyword(string text)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsAny(normalized, "handoff", "인수인계");
    }

    private static bool ContainsExplicitProfileControlIntent(string text)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        var hasProfileKeyword = ContainsAny(normalized, "프로필", "profile", "talk", "code");
        return hasProfileKeyword && ContainsNaturalSettingVerb(normalized);
    }

    private static bool ContainsExplicitModeControlIntent(string text)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        var hasModeKeyword = ContainsAny(
            normalized,
            "모드",
            "mode",
            "단일모드",
            "멀티모드",
            "오케스트레이션",
            "orchestration",
            "single mode",
            "multi mode"
        );
        return hasModeKeyword && ContainsNaturalSettingVerb(normalized);
    }

    private static bool ContainsExplicitProviderControlIntent(string text, string slot)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        var hasProviderKeyword = ContainsAny(normalized, "provider", "제공자", "공급자");
        var hasLlmContext = ContainsAny(normalized, "llm", "모델", "model", "채팅", "single", "단일", "multi", "다중", "orchestration", "오케스트레이션", "codex", "코덱스");
        var providerNameCount = CountProviderNameMentions(normalized);
        var hasSummaryKeyword = ContainsAny(normalized, "summary", "요약");
        if (string.Equals(slot, "summary", StringComparison.OrdinalIgnoreCase) && !hasSummaryKeyword)
        {
            return false;
        }

        if (hasProviderKeyword && (hasLlmContext || hasSummaryKeyword || providerNameCount > 0))
        {
            return true;
        }

        return ContainsNaturalSettingVerb(normalized)
            && providerNameCount > 0
            && (hasLlmContext || providerNameCount >= 2 || hasSummaryKeyword);
    }

    private static bool ContainsExplicitModelControlIntent(string text)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsAny(normalized, "모델", "model", "llm")
            && ContainsNaturalSettingVerb(normalized);
    }

    private static bool ContainsExplicitCodingKeyword(string text)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsAny(normalized, "코딩", "coding", "code run", "코드 생성");
    }

    private static bool ContainsExplicitCodingRunIntent(string text)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsExplicitCodingKeyword(normalized)
            && ContainsAny(normalized, "실행", "run", "만들", "구현", "개발", "작성", "생성");
    }

    private static bool ContainsExplicitCodingResultIntent(string text)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsExplicitCodingKeyword(normalized)
            && ContainsAny(normalized, "최근", "마지막", "결과", "요약", "last", "result");
    }

    private static bool ContainsExplicitCodingFilesIntent(string text)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsExplicitCodingKeyword(normalized)
            && ContainsAny(normalized, "파일", "목록", "리스트", "files", "list");
    }

    private static bool ContainsExplicitCodingFileIntent(string text)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsExplicitCodingKeyword(normalized)
            && ContainsAny(normalized, "파일", "열어", "보여", "미리보기", "preview", "file");
    }

    private static bool ContainsExplicitCodingModeControlIntent(string text)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsExplicitCodingKeyword(normalized)
            && ContainsAny(normalized, "모드", "mode", "단일", "single", "오케스트레이션", "orchestration", "다중", "multi")
            && ContainsNaturalSettingVerb(normalized);
    }

    private static bool ContainsExplicitCodingLanguageIntent(string text)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsExplicitCodingKeyword(normalized)
            && ContainsAny(normalized, "언어", "language")
            && ContainsNaturalSettingVerb(normalized);
    }

    private static bool ContainsExplicitCodingProviderControlIntent(string text)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsExplicitCodingKeyword(normalized)
            && ContainsAny(normalized, "제공자", "provider", "요약 담당", "summary")
            && ContainsNaturalSettingVerb(normalized);
    }

    private static bool ContainsExplicitCodingModelControlIntent(string text)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsExplicitCodingKeyword(normalized)
            && ContainsAny(normalized, "모델", "model")
            && ContainsNaturalSettingVerb(normalized);
    }

    private static bool ContainsExplicitCodingWorkerControlIntent(string text)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsExplicitCodingKeyword(normalized)
            && ContainsAny(normalized, "워커", "worker")
            && ContainsNaturalSettingVerb(normalized);
    }

    private static bool ContainsExplicitRefactorKeyword(string text)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsAny(normalized, "safe refactor", "refactor", "리팩터");
    }

    private static bool ContainsExplicitRefactorReadIntent(string text)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsExplicitRefactorKeyword(normalized)
            && ContainsAny(normalized, "읽기", "read", "보기", "확인");
    }

    private static bool ContainsExplicitRefactorApplyIntent(string text)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsExplicitRefactorKeyword(normalized)
            && ContainsAny(normalized, "적용", "apply");
    }

    private static bool ContainsExplicitMetricsIntent(string text)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsAny(normalized, "metrics", "metric", "메트릭", "지표");
    }

    private static bool ContainsExplicitLlmStatusIntent(string text)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        var hasLlmKeyword = ContainsAny(normalized, "llm", "모델", "model", "provider", "제공자");
        var hasStatusKeyword = ContainsAny(normalized, "상태", "status", "뭐", "무엇", "어떤", "현재", "지금", "사용", "쓰고");
        return hasLlmKeyword && hasStatusKeyword;
    }

    private static bool ContainsExplicitLlmUsageIntent(string text)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsAny(normalized, "quota", "usage", "limit", "사용량", "한도", "쿼터", "잔여");
    }

    private static bool ContainsExplicitLlmModelsIntent(string text)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsAny(
            normalized,
            "모델 목록",
            "모델 리스트",
            "지원 모델",
            "available models",
            "model list",
            "models"
        );
    }

    private static bool ContainsExplicitHelpIntent(string text)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsAny(normalized, "help", "도움말", "명령어", "사용법", "가이드", "뭐 할 수", "뭘 할 수", "할수있는", "할 수 있는");
    }

    private static int CountProviderNameMentions(string text)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return 0;
        }

        var count = 0;
        if (ContainsAny(normalized, "groq", "그록"))
        {
            count += 1;
        }

        if (ContainsAny(normalized, "gemini", "제미니"))
        {
            count += 1;
        }

        if (ContainsAny(normalized, "copilot", "코파일럿"))
        {
            count += 1;
        }

        if (ContainsAny(normalized, "cerebras", "세레브라스", "세레브라"))
        {
            count += 1;
        }

        if (ContainsAny(normalized, "codex", "코덱스"))
        {
            count += 1;
        }

        if (ContainsAny(normalized, "auto", "자동"))
        {
            count += 1;
        }

        return count;
    }

    private static bool ContainsNaturalSettingVerb(string text)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsAny(
            normalized,
            "변경",
            "바꿔",
            "바꿔줘",
            "설정",
            "전환",
            "set",
            "switch",
            "맞춰",
            "해줘",
            "선택",
            "켜줘",
            "보여줘",
            "만들어줘"
        );
    }

    private static bool ContainsExplicitRoutineKeyword(string text)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        return normalized.Contains("루틴", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("routine", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCodingNaturalMode(string? mode)
    {
        return (mode ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "single" => "single",
            "단일" => "single",
            "orchestration" => "orchestration",
            "오케스트레이션" => "orchestration",
            "multi" => "multi",
            "다중" => "multi",
            _ => string.Empty
        };
    }

    private string ApplyChannelProfile(string source, string profile, string thinking)
    {
        var normalizedSource = (source ?? string.Empty).Trim().ToLowerInvariant();
        var thinkingLabel = thinking == "high" ? "high" : thinking == "low" ? "low" : "auto";
        if (normalizedSource == "telegram")
        {
            lock (_telegramLlmLock)
            {
                if (profile == "talk")
                {
                    ApplyTelegramTalkDefaults(thinking);
                    return $"텔레그램 프로필을 대화용으로 바꿨습니다. 모드={FormatModeDisplayName(_telegramLlmPreferences.Mode)}, thinking={thinkingLabel}";
                }

                ApplyTelegramCodeDefaults(thinking);
                return $"텔레그램 프로필을 코딩용으로 바꿨습니다. 모드={FormatModeDisplayName(_telegramLlmPreferences.Mode)}, thinking={thinkingLabel}";
            }
        }

        lock (_webLlmLock)
        {
            if (profile == "talk")
            {
                ApplyWebTalkDefaults(thinking);
                return $"웹 프로필을 대화용으로 바꿨습니다. 모드={FormatModeDisplayName(_webLlmPreferences.Mode)}, thinking={thinkingLabel}";
            }

            ApplyWebCodeDefaults(thinking);
            return $"웹 프로필을 코딩용으로 바꿨습니다. 모드={FormatModeDisplayName(_webLlmPreferences.Mode)}, thinking={thinkingLabel}";
        }
    }

    private string SetChannelMode(string source, string mode)
    {
        if (mode is not ("single" or "orchestration" or "multi"))
        {
            return "지원 모드는 single, orchestration, multi 입니다.";
        }

        if (source.Equals("telegram", StringComparison.OrdinalIgnoreCase))
        {
            lock (_telegramLlmLock)
            {
                _telegramLlmPreferences.Mode = mode;
            }

            return $"텔레그램 LLM 모드를 {FormatModeDisplayName(mode)}로 바꿨습니다.";
        }

        lock (_webLlmLock)
        {
            _webLlmPreferences.Mode = mode;
        }

        return $"웹 LLM 모드를 {FormatModeDisplayName(mode)}로 바꿨습니다.";
    }

    private string SetChannelProvider(string source, string slot, string provider)
    {
        var normalizedSlot = (slot ?? string.Empty).Trim().ToLowerInvariant();
        var allowAuto = normalizedSlot is "orchestration" or "summary";
        var normalizedProvider = NormalizeProvider(provider, allowAuto);
        if (!allowAuto && normalizedProvider == "auto")
        {
            return "지원 제공자는 groq, gemini, copilot, cerebras, nvidia, codex 입니다.";
        }

        if (source.Equals("telegram", StringComparison.OrdinalIgnoreCase))
        {
            lock (_telegramLlmLock)
            {
                return SetTelegramProviderCore(normalizedSlot, normalizedProvider);
            }
        }

        lock (_webLlmLock)
        {
            return SetWebProviderCore(normalizedSlot, normalizedProvider);
        }
    }

    private string SetChannelModel(string source, string slot, string modelId)
    {
        var normalizedSlot = (slot ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedModel = NormalizeModelSelection(modelId);
        if (string.IsNullOrWhiteSpace(normalizedModel))
        {
            return "model-id를 입력하세요.";
        }

        if (source.Equals("telegram", StringComparison.OrdinalIgnoreCase))
        {
            lock (_telegramLlmLock)
            {
                return SetTelegramModelCore(normalizedSlot, normalizedModel);
            }
        }

        lock (_webLlmLock)
        {
            return SetWebModelCore(normalizedSlot, normalizedModel);
        }
    }

    private string BuildChannelModelStatus(string source)
    {
        if (source.Equals("telegram", StringComparison.OrdinalIgnoreCase))
        {
            TelegramLlmPreferences snapshot;
            lock (_telegramLlmLock)
            {
                snapshot = _telegramLlmPreferences.Clone();
            }

            return BuildModelStatusText("telegram", snapshot.Mode, snapshot.SingleProvider, snapshot.SingleModel, snapshot.OrchestrationProvider, snapshot.OrchestrationModel, snapshot.MultiGroqModel, snapshot.MultiGeminiModel, snapshot.MultiCopilotModel, snapshot.MultiCerebrasModel, snapshot.MultiNvidiaModel, snapshot.MultiCodexModel, snapshot.MultiSummaryProvider);
        }

        WebLlmPreferences webSnapshot;
        lock (_webLlmLock)
        {
            webSnapshot = _webLlmPreferences.Clone();
        }

        return BuildModelStatusText("web", webSnapshot.Mode, webSnapshot.SingleProvider, webSnapshot.SingleModel, webSnapshot.OrchestrationProvider, webSnapshot.OrchestrationModel, webSnapshot.MultiGroqModel, webSnapshot.MultiGeminiModel, webSnapshot.MultiCopilotModel, webSnapshot.MultiCerebrasModel, webSnapshot.MultiNvidiaModel, webSnapshot.MultiCodexModel, webSnapshot.MultiSummaryProvider);
    }

    private string BuildModelStatusText(
        string channel,
        string mode,
        string singleProvider,
        string singleModel,
        string orchestrationProvider,
        string orchestrationModel,
        string multiGroqModel,
        string multiGeminiModel,
        string multiCopilotModel,
        string multiCerebrasModel,
        string multiNvidiaModel,
        string multiCodexModel,
        string multiSummaryProvider
    )
    {
        return $"""
                [{(channel == "telegram" ? "텔레그램" : "웹")} LLM 설정]
                현재 모드: {FormatModeDisplayName(mode)}
                단일: {FormatProviderWithModel(singleProvider, singleModel)}
                오케스트레이션: {FormatProviderWithModel(orchestrationProvider, orchestrationModel, allowAuto: true)}
                다중 Groq: {FormatProviderWithModel("groq", multiGroqModel)}
                다중 Gemini: {FormatProviderWithModel("gemini", multiGeminiModel)}
                다중 Copilot: {FormatProviderWithModel("copilot", multiCopilotModel)}
                다중 Cerebras: {FormatProviderWithModel("cerebras", multiCerebrasModel)}
                다중 NVIDIA NIM: {FormatProviderWithModel("nvidia", multiNvidiaModel)}
                다중 Codex: {FormatProviderWithModel("codex", multiCodexModel)}
                다중 요약 담당: {FormatProviderDisplayName(multiSummaryProvider, allowAuto: true)}
                """;
    }

    private string BuildUnifiedLlmHelpText(string source)
    {
        var channel = source.Equals("telegram", StringComparison.OrdinalIgnoreCase) ? "텔레그램" : "웹";
        return $"""
                [{channel} LLM 도움말]
                슬래시 없이도 이렇게 말하면 됩니다.
                - "단일 모드로 바꿔"
                - "Codex로 바꿔"
                - "다중 요약 제공자를 Gemini로 설정해"
                - "모델 목록 보여줘"

                자주 쓰는 명령:
                - /talk [low|high]
                - /code [low|high]
                - /model <groq|gemini|copilot|cerebras|nvidia|codex>
                - /llm status
                - /llm models [groq|gemini|copilot|cerebras|nvidia|codex|all]
                - /llm usage

                세부 설정:
                - /mode <single|orchestration|multi>
                - /provider <single|orchestration|summary> <groq|gemini|copilot|cerebras|nvidia|codex|auto>
                - /model <single|orchestration|multi.groq|multi.gemini|multi.copilot|multi.cerebras|multi.nvidia|multi.codex> <model-id>
                """;
    }

    private static string BuildMemoryCommandHelpText()
    {
        return """
               [메모리 명령]
               - /memory clear
               - /memory create [compact]

               예시:
               - /memory clear
               - /memory create
               - /memory create compact
               """;
    }

    private static string FormatModeDisplayName(string mode)
    {
        return (mode ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "single" => "단일",
            "orchestration" => "오케스트레이션",
            "multi" => "다중",
            _ => "기본"
        };
    }

    private string FormatProviderWithModel(string provider, string? model, bool allowAuto = false)
    {
        var normalizedProvider = NormalizeProvider(provider, allowAuto);
        if (allowAuto && normalizedProvider == "auto")
        {
            return $"자동 선택 (기본: {FormatProviderDisplayName("gemini")})";
        }

        return $"{FormatProviderDisplayName(normalizedProvider)} / {ResolveModel(normalizedProvider, model)}";
    }

    private static string FormatProviderDisplayName(string provider, bool allowAuto = false)
    {
        return NormalizeProvider(provider, allowAuto) switch
        {
            "groq" => "Groq",
            "gemini" => "Gemini",
            "copilot" => "Copilot",
            "cerebras" => "Cerebras",
            "nvidia" => "NVIDIA NIM",
            "codex" => "Codex",
            "auto" => "자동 선택",
            _ => "Groq"
        };
    }

    private void ApplyWebTalkDefaults(string requestedThinking)
    {
        var fastModel = string.IsNullOrWhiteSpace(_config.GroqModel) ? DefaultGroqPrimaryModel : _config.GroqModel;
        _webLlmPreferences.Profile = "talk";
        _webLlmPreferences.Mode = "orchestration";
        _webLlmPreferences.SingleProvider = "groq";
        _webLlmPreferences.SingleModel = fastModel;
        _webLlmPreferences.AutoGroqComplexUpgrade = true;
        _webLlmPreferences.OrchestrationProvider = "gemini";
        _webLlmPreferences.OrchestrationModel = _config.GeminiModel;
        _webLlmPreferences.MultiGroqModel = fastModel;
        _webLlmPreferences.MultiGeminiModel = _config.GeminiModel;
        _webLlmPreferences.MultiCopilotModel = DefaultCopilotModel;
        _webLlmPreferences.MultiCerebrasModel = _config.CerebrasModel;
        _webLlmPreferences.MultiNvidiaModel = _config.NvidiaModel;
        _webLlmPreferences.MultiCodexModel = _config.CodexModel;
        _webLlmPreferences.MultiSummaryProvider = "gemini";
        _webLlmPreferences.TalkThinkingLevel = NormalizeThinkingLevel(requestedThinking, "low");
    }

    private void ApplyWebCodeDefaults(string requestedThinking)
    {
        var fastModel = string.IsNullOrWhiteSpace(_config.GroqModel) ? DefaultGroqPrimaryModel : _config.GroqModel;
        _webLlmPreferences.Profile = "code";
        _webLlmPreferences.Mode = "orchestration";
        _webLlmPreferences.SingleProvider = "copilot";
        _webLlmPreferences.SingleModel = DefaultCopilotModel;
        _webLlmPreferences.AutoGroqComplexUpgrade = false;
        _webLlmPreferences.OrchestrationProvider = "gemini";
        _webLlmPreferences.OrchestrationModel = _config.GeminiModel;
        _webLlmPreferences.MultiGroqModel = fastModel;
        _webLlmPreferences.MultiGeminiModel = _config.GeminiModel;
        _webLlmPreferences.MultiCopilotModel = DefaultCopilotModel;
        _webLlmPreferences.MultiCerebrasModel = _config.CerebrasModel;
        _webLlmPreferences.MultiNvidiaModel = _config.NvidiaModel;
        _webLlmPreferences.MultiCodexModel = _config.CodexModel;
        _webLlmPreferences.MultiSummaryProvider = "gemini";
        _webLlmPreferences.CodeThinkingLevel = NormalizeThinkingLevel(requestedThinking, "high");
    }

    private string SetTelegramProviderCore(string slot, string provider)
    {
        if (slot == "single")
        {
            _telegramLlmPreferences.SingleProvider = provider;
            if (provider == "groq")
            {
                _telegramLlmPreferences.SingleModel = string.IsNullOrWhiteSpace(_config.GroqModel) ? DefaultGroqPrimaryModel : _config.GroqModel;
                _telegramLlmPreferences.AutoGroqComplexUpgrade = true;
            }
            else if (provider == "copilot")
            {
                _telegramLlmPreferences.SingleModel = DefaultCopilotModel;
                _telegramLlmPreferences.AutoGroqComplexUpgrade = false;
            }
            else
            {
                _telegramLlmPreferences.SingleModel = provider switch
                {
                    "cerebras" => _config.CerebrasModel,
                    "nvidia" => _config.NvidiaModel,
                    "codex" => _config.CodexModel,
                    _ => _config.GeminiModel
                };
                _telegramLlmPreferences.AutoGroqComplexUpgrade = false;
            }

            return $"텔레그램 단일 제공자를 {FormatProviderDisplayName(provider)}로 바꿨습니다. 현재 모델: {ResolveModel(provider, _telegramLlmPreferences.SingleModel)}";
        }

        if (slot == "orchestration")
        {
            _telegramLlmPreferences.OrchestrationProvider = provider;
            return $"텔레그램 오케스트레이션 담당을 {FormatProviderDisplayName(provider, allowAuto: true)}로 바꿨습니다.";
        }

        if (slot == "summary")
        {
            _telegramLlmPreferences.MultiSummaryProvider = provider;
            return $"텔레그램 다중 요약 담당을 {FormatProviderDisplayName(provider, allowAuto: true)}로 바꿨습니다.";
        }

        return "지원 슬롯은 single, orchestration, summary 입니다.";
    }

    private string SetWebProviderCore(string slot, string provider)
    {
        if (slot == "single")
        {
            _webLlmPreferences.SingleProvider = provider;
            if (provider == "groq")
            {
                _webLlmPreferences.SingleModel = string.IsNullOrWhiteSpace(_config.GroqModel) ? DefaultGroqPrimaryModel : _config.GroqModel;
                _webLlmPreferences.AutoGroqComplexUpgrade = true;
            }
            else if (provider == "copilot")
            {
                _webLlmPreferences.SingleModel = DefaultCopilotModel;
                _webLlmPreferences.AutoGroqComplexUpgrade = false;
            }
            else
            {
                _webLlmPreferences.SingleModel = provider switch
                {
                    "cerebras" => _config.CerebrasModel,
                    "nvidia" => _config.NvidiaModel,
                    "codex" => _config.CodexModel,
                    _ => _config.GeminiModel
                };
                _webLlmPreferences.AutoGroqComplexUpgrade = false;
            }

            return $"웹 단일 제공자를 {FormatProviderDisplayName(provider)}로 바꿨습니다. 현재 모델: {ResolveModel(provider, _webLlmPreferences.SingleModel)}";
        }

        if (slot == "orchestration")
        {
            _webLlmPreferences.OrchestrationProvider = provider;
            return $"웹 오케스트레이션 담당을 {FormatProviderDisplayName(provider, allowAuto: true)}로 바꿨습니다.";
        }

        if (slot == "summary")
        {
            _webLlmPreferences.MultiSummaryProvider = provider;
            return $"웹 다중 요약 담당을 {FormatProviderDisplayName(provider, allowAuto: true)}로 바꿨습니다.";
        }

        return "지원 슬롯은 single, orchestration, summary 입니다.";
    }

    private string SetTelegramModelCore(string slot, string model)
    {
        if (slot == "single")
        {
            _telegramLlmPreferences.SingleModel = model;
            if (_telegramLlmPreferences.SingleProvider == "groq")
            {
                _telegramLlmPreferences.AutoGroqComplexUpgrade = model.Equals(DefaultGroqFastModel, StringComparison.OrdinalIgnoreCase);
            }

            return $"텔레그램 단일 모델을 {model}로 바꿨습니다.";
        }

        if (slot == "orchestration")
        {
            _telegramLlmPreferences.OrchestrationModel = model;
            return $"텔레그램 오케스트레이션 모델을 {model}로 바꿨습니다.";
        }

        if (slot == "multi.groq")
        {
            _telegramLlmPreferences.MultiGroqModel = model;
            return $"텔레그램 다중 Groq 모델을 {model}로 바꿨습니다.";
        }

        if (slot == "multi.gemini")
        {
            _telegramLlmPreferences.MultiGeminiModel = model;
            return $"텔레그램 다중 Gemini 모델을 {model}로 바꿨습니다.";
        }

        if (slot == "multi.copilot")
        {
            _telegramLlmPreferences.MultiCopilotModel = model;
            return $"텔레그램 다중 Copilot 모델을 {model}로 바꿨습니다.";
        }

        if (slot == "multi.cerebras")
        {
            _telegramLlmPreferences.MultiCerebrasModel = model;
            return $"텔레그램 다중 Cerebras 모델을 {model}로 바꿨습니다.";
        }

        if (slot == "multi.nvidia")
        {
            _telegramLlmPreferences.MultiNvidiaModel = model;
            return $"텔레그램 다중 NVIDIA NIM 모델을 {model}로 바꿨습니다.";
        }

        if (slot == "multi.codex")
        {
            _telegramLlmPreferences.MultiCodexModel = model;
            return $"텔레그램 다중 Codex 모델을 {model}로 바꿨습니다.";
        }

        return "지원 슬롯은 single, orchestration, multi.groq, multi.gemini, multi.copilot, multi.cerebras, multi.nvidia, multi.codex 입니다.";
    }

    private string SetWebModelCore(string slot, string model)
    {
        if (slot == "single")
        {
            _webLlmPreferences.SingleModel = model;
            if (_webLlmPreferences.SingleProvider == "groq")
            {
                _webLlmPreferences.AutoGroqComplexUpgrade = model.Equals(DefaultGroqFastModel, StringComparison.OrdinalIgnoreCase);
            }

            return $"웹 단일 모델을 {model}로 바꿨습니다.";
        }

        if (slot == "orchestration")
        {
            _webLlmPreferences.OrchestrationModel = model;
            return $"웹 오케스트레이션 모델을 {model}로 바꿨습니다.";
        }

        if (slot == "multi.groq")
        {
            _webLlmPreferences.MultiGroqModel = model;
            return $"웹 다중 Groq 모델을 {model}로 바꿨습니다.";
        }

        if (slot == "multi.gemini")
        {
            _webLlmPreferences.MultiGeminiModel = model;
            return $"웹 다중 Gemini 모델을 {model}로 바꿨습니다.";
        }

        if (slot == "multi.copilot")
        {
            _webLlmPreferences.MultiCopilotModel = model;
            return $"웹 다중 Copilot 모델을 {model}로 바꿨습니다.";
        }

        if (slot == "multi.cerebras")
        {
            _webLlmPreferences.MultiCerebrasModel = model;
            return $"웹 다중 Cerebras 모델을 {model}로 바꿨습니다.";
        }

        if (slot == "multi.nvidia")
        {
            _webLlmPreferences.MultiNvidiaModel = model;
            return $"웹 다중 NVIDIA NIM 모델을 {model}로 바꿨습니다.";
        }

        if (slot == "multi.codex")
        {
            _webLlmPreferences.MultiCodexModel = model;
            return $"웹 다중 Codex 모델을 {model}로 바꿨습니다.";
        }

        return "지원 슬롯은 single, orchestration, multi.groq, multi.gemini, multi.copilot, multi.cerebras, multi.nvidia, multi.codex 입니다.";
    }
}
