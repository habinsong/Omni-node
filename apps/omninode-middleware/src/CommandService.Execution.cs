namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    public Task<string> ExecuteAsync(
        string input,
        string source,
        CancellationToken cancellationToken,
        IReadOnlyList<InputAttachment>? attachments = null,
        IReadOnlyList<string>? webUrls = null,
        bool webSearchEnabled = true,
        Action<string>? streamCallback = null
    )
    {
        return ExecuteCoreAsync(input, source, cancellationToken, attachments, webUrls, webSearchEnabled, streamCallback);
    }

    private async Task<string> ExecuteCoreAsync(
        string input,
        string source,
        CancellationToken cancellationToken,
        IReadOnlyList<InputAttachment>? attachments = null,
        IReadOnlyList<string>? webUrls = null,
        bool webSearchEnabled = true,
        Action<string>? streamCallback = null
    )
    {
        var normalizedAttachments = NormalizeAttachments(attachments);
        var text = (input ?? string.Empty).Trim();
        if (source.Equals("telegram", StringComparison.OrdinalIgnoreCase)
            && TryGetAudioAttachment(normalizedAttachments, out var audioAttachment)
            && (string.IsNullOrWhiteSpace(text) || text.Equals("첨부 파일을 분석해줘", StringComparison.OrdinalIgnoreCase)))
        {
            var transcribed = await _llmRouter.TranscribeAudioAsync(audioAttachment, cancellationToken);
            if (transcribed.StartsWith("음성 변환 설정 필요", StringComparison.OrdinalIgnoreCase)
                || transcribed.StartsWith("음성 변환 실패", StringComparison.OrdinalIgnoreCase)
                || transcribed.StartsWith("음성 변환 오류", StringComparison.OrdinalIgnoreCase)
                || transcribed.StartsWith("음성 변환 시간이 초과", StringComparison.OrdinalIgnoreCase))
            {
                return transcribed;
            }

            text = transcribed.Trim();

            // 사용자가 자기 음성이 어떻게 들렸는지 즉시 확인할 수 있게 transcript를 별도 메시지로 echo.
            // 잘못 들렸을 때 다시 말하도록 유도한다. 메시지 전송 실패는 무시.
            try
            {
                var preview = text.Length > 360 ? text[..360] + "…" : text;
                await _telegramClient.SendMessageAsync(
                    $"🎙️ 들은 내용:\n\"{preview}\"\n(분석을 시작합니다.)",
                    cancellationToken
                );
            }
            catch
            {
            }
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            if (normalizedAttachments.Count > 0 && source.Equals("telegram", StringComparison.OrdinalIgnoreCase))
            {
                text = "첨부 파일을 분석해줘";
            }
            else
            {
                return "empty command";
            }
        }

        if (text.Length > _config.CommandMaxLength)
        {
            return $"command too long (max={_config.CommandMaxLength})";
        }

        if (source.Equals("telegram", StringComparison.OrdinalIgnoreCase))
        {
            SetCurrentTelegramExecutionMetadata();
        }
        attachments = normalizedAttachments;

        RecordEvent($"{source}:user:{text}");
        _auditLogger.Log(source, "command_received", "ok", text);

        try
        {
            // 텔레그램에서 등록한 스킬 별명을 슬래시 명령으로 받았을 때 자연어 호출로 rewrite.
            // 예: "/e 디지털 카메라" + alias e→eli5  =>  "eli5 스킬 사용해서 디지털 카메라"
            if (source.Equals("telegram", StringComparison.OrdinalIgnoreCase) && text.StartsWith("/", StringComparison.Ordinal))
            {
                var rewritten = TryRewriteSlashAliasToSkillInvocation(text);
                if (!string.IsNullOrWhiteSpace(rewritten))
                {
                    text = rewritten!;
                }
            }

            if (text.StartsWith("/help", StringComparison.OrdinalIgnoreCase)
                || text.Equals("/start", StringComparison.OrdinalIgnoreCase))
            {
                if (source.Equals("telegram", StringComparison.OrdinalIgnoreCase))
                {
                    var helpTopic = ParseHelpTopicFromInput(text);
                    return BuildTelegramHelpText(helpTopic);
                }

                return """
                       Omni-node commands
                       /metrics
                       /doctor
                       /doctor json
                       /plan list
                       /plan create <요청>
                       /plan review <plan-id>
                       /plan approve <plan-id>
                       /plan run <plan-id>
                       /task list
                       /task create <plan-id>
                       /task status <graph-id>
                       /task run <graph-id>
                       /task cancel <graph-id> <task-id>
                       /notebook show [project-key]
                       /notebook append <learning|decision|verification> <내용>
                       /handoff [project-key]
                       /kill <pid>
                       /code <instruction>
                       /profile <talk|code> [low|high]
                       /mode <single|orchestration|multi>
                       /provider <single|orchestration|summary> <groq|gemini|copilot|cerebras|nvidia|codex|auto>
                       /model <single|orchestration|multi.groq|multi.gemini|multi.copilot|multi.cerebras|multi.nvidia|multi.codex> <model-id>
                       /status model
                       /llm status
                       /llm mode <single|orchestration|multi>
                       /llm single provider <groq|gemini|copilot|cerebras|nvidia|codex>
                       /llm single model <model-id>
                       /llm orchestration provider <auto|groq|gemini|copilot|cerebras|nvidia|codex>
                       /llm orchestration model <model-id>
                       /llm multi groq <model-id>
                       /llm multi gemini <model-id>
                       /llm multi copilot <model-id>
                       /llm multi cerebras <model-id>
                       /llm multi nvidia <model-id>
                       /llm multi codex <model-id>
                       /llm multi summary <auto|groq|gemini|copilot|cerebras|nvidia|codex>
                       /help
                       """;
            }

            if (source.Equals("telegram", StringComparison.OrdinalIgnoreCase))
            {
                var codingCommandResult = await TryHandleTelegramCodingCommandAsync(text, attachments, webUrls, webSearchEnabled, cancellationToken);
                if (codingCommandResult != null)
                {
                    return codingCommandResult;
                }

                var refactorCommandResult = await TryHandleTelegramRefactorCommandAsync(text, cancellationToken);
                if (refactorCommandResult != null)
                {
                    return refactorCommandResult;
                }

                var profileResult = await TryHandleTelegramProfileCommandAsync(text, cancellationToken);
                if (profileResult != null)
                {
                    return profileResult;
                }

                var quickModelResult = await TryHandleTelegramQuickModelCommandAsync(text, cancellationToken);
                if (quickModelResult != null)
                {
                    return quickModelResult;
                }

                var llmCommandResult = await TryHandleTelegramLlmControlCommandAsync(text, cancellationToken);
                if (llmCommandResult != null)
                {
                    return llmCommandResult;
                }

                var skillCommandResult = await TryHandleTelegramSkillCommandAsync(text, cancellationToken);
                if (skillCommandResult != null)
                {
                    return skillCommandResult;
                }

                var doctorCommandResult = await TryHandleTelegramDoctorCommandAsync(text, cancellationToken);
                if (doctorCommandResult != null)
                {
                    return doctorCommandResult;
                }

                var planCommandResult = await TryHandleTelegramPlanCommandAsync(text, cancellationToken);
                if (planCommandResult != null)
                {
                    return planCommandResult;
                }

                var taskCommandResult = await TryHandleTelegramTaskCommandAsync(text, cancellationToken);
                if (taskCommandResult != null)
                {
                    return taskCommandResult;
                }

                var notebookCommandResult = await TryHandleTelegramNotebookCommandAsync(text, cancellationToken);
                if (notebookCommandResult != null)
                {
                    return notebookCommandResult;
                }

                var memoryCommandResult = await TryHandleTelegramMemoryCommandAsync(text, cancellationToken);
                if (memoryCommandResult != null)
                {
                    return memoryCommandResult;
                }

                var historySlashResult = TryHandleTelegramHistorySlashCommand(text);
                if (historySlashResult != null)
                {
                    return historySlashResult;
                }

                var thinkToggleResult = TryHandleTelegramThinkSlashCommand(text);
                if (thinkToggleResult != null)
                {
                    return thinkToggleResult;
                }

                var webToggleResult = TryHandleTelegramWebSlashCommand(text);
                if (webToggleResult != null)
                {
                    return webToggleResult;
                }
            }

            var unifiedSlashResult = await TryHandleUnifiedSlashCommandAsync(text, source, cancellationToken);
            if (unifiedSlashResult != null)
            {
                return unifiedSlashResult;
            }

            if (!text.StartsWith("/", StringComparison.Ordinal))
            {
                if (source.Equals("telegram", StringComparison.OrdinalIgnoreCase))
                {
                    var skillNaturalResult = await TryHandleTelegramNaturalSkillCommandAsync(
                        text,
                        cancellationToken
                    );
                    if (skillNaturalResult != null)
                    {
                        return skillNaturalResult;
                    }
                }

                var naturalByLlmResult = await TryHandleNaturalCommandByLlmAsync(
                    source,
                    text,
                    attachments,
                    webUrls,
                    webSearchEnabled,
                    cancellationToken
                );
                if (naturalByLlmResult != null)
                {
                    return naturalByLlmResult;
                }

                if (source.Equals("telegram", StringComparison.OrdinalIgnoreCase))
                {
                    var legacyNaturalControlResult = await TryHandleTelegramNaturalControlCommandAsync(
                        text,
                        attachments,
                        webUrls,
                        webSearchEnabled,
                        cancellationToken
                    );
                    if (legacyNaturalControlResult != null)
                    {
                        return legacyNaturalControlResult;
                    }
                }
            }

            var routineCommandResult = await TryHandleRoutineCommandAsync(text, source, cancellationToken);
            if (routineCommandResult != null)
            {
                return routineCommandResult;
            }

            if (text.Equals("/metrics", StringComparison.OrdinalIgnoreCase))
            {
                var metrics = await _coreClient.GetMetricsAsync(cancellationToken);
                RecordEvent($"{source}:core:{metrics}");
                _auditLogger.Log(source, "metrics", "ok", metrics);
                return metrics;
            }

            if (TryParseKillCommand(text, out var pid))
            {
                var guard = await ValidateKillTargetAsync(pid, source, cancellationToken);
                if (!guard.Allowed)
                {
                    _auditLogger.Log(source, "kill", "deny", $"pid={pid} reason={guard.Reason}");
                    return $"kill denied: {guard.Reason}";
                }

                var result = await _coreClient.KillAsync(pid, cancellationToken);
                RecordEvent($"{source}:core:{result}");
                _auditLogger.Log(source, "kill", "ok", $"pid={pid}");
                return result;
            }

            if (source.Equals("telegram", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("/", StringComparison.Ordinal))
            {
                var naturalRoutineResult = await TryHandleNaturalRoutineRequestAsync(text, source, cancellationToken);
                if (naturalRoutineResult != null)
                {
                    return naturalRoutineResult;
                }

                var routed = await ExecuteTelegramLlmMessageAsync(
                    text,
                    attachments,
                    webUrls,
                    webSearchEnabled,
                    streamCallback,
                    cancellationToken
                );
                _auditLogger.Log(source, "telegram_llm_route", "ok", "mode_routed");
                return routed;
            }

            var intent = await _llmRouter.ClassifyIntentAsync(text, cancellationToken);
            _auditLogger.Log(source, "intent_classified", "ok", intent.ToString());

            if (intent == RouterIntent.DynamicCode)
            {
                if (!_config.EnableDynamicCode)
                {
                    return "dynamic code is disabled. set OMNINODE_ENABLE_DYNAMIC_CODE=true";
                }

                var copilotStatus = await _copilotWrapper.GetStatusAsync(cancellationToken);
                if (!copilotStatus.Installed)
                {
                    return "copilot cli not installed";
                }
                if (!copilotStatus.Authenticated)
                {
                    return "copilot cli is not authenticated. run `gh auth login` and copilot sign-in first.";
                }

                var metrics = await _coreClient.GetMetricsAsync(cancellationToken);
                RecordEvent($"{source}:core:{metrics}");
                var context = BuildContextSnapshot(metrics);
                var plan = await _llmRouter.BuildExecutionPlanAsync(text, context, cancellationToken);
                RecordEvent($"{source}:plan:{plan}");
                var code = await _copilotWrapper.SuggestCodeAsync(plan, cancellationToken);
                if (string.IsNullOrWhiteSpace(code))
                {
                    _auditLogger.Log(source, "dynamic_code", "fail", "empty code");
                    return "no code generated from copilot cli";
                }

                var result = await _sandboxClient.ExecuteCodeAsync(code, cancellationToken);
                RecordEvent($"{source}:sandbox:{result}");
                _auditLogger.Log(source, "dynamic_code", "ok", result);
                return TrimForOutput(result);
            }

            if (intent == RouterIntent.QuerySystem)
            {
                var metrics = await _coreClient.GetMetricsAsync(cancellationToken);
                RecordEvent($"{source}:core:{metrics}");
                _auditLogger.Log(source, "query_system", "ok", metrics);
                return metrics;
            }

            if (intent == RouterIntent.OsControl)
            {
                _auditLogger.Log(source, "os_control", "deny", text);
                return "os control intent detected. use explicit allowlisted command (/kill <pid>) only.";
            }

            _auditLogger.Log(source, "unknown", "ok", intent.ToString());
            if (source.Equals("telegram", StringComparison.OrdinalIgnoreCase))
            {
                var fallback = await ChatFallbackForUnknownAsync(BuildTelegramConcisePrompt(text), cancellationToken);
                _auditLogger.Log(source, "telegram_unknown_fallback", "ok", "llm_chat");
                return FormatTelegramResponse(fallback, TelegramMaxResponseChars);
            }

            return $"intent={intent}";
        }
        catch (Exception ex)
        {
            _auditLogger.Log(source, "command_error", "fail", ex.Message);
            return $"error: {ex.Message}";
        }
    }

    private static bool TryGetAudioAttachment(IReadOnlyList<InputAttachment>? attachments, out InputAttachment attachment)
    {
        attachment = null!;
        if (attachments == null || attachments.Count == 0)
        {
            return false;
        }

        foreach (var item in attachments)
        {
            var mime = (item.MimeType ?? string.Empty).Trim();
            var name = (item.Name ?? string.Empty).Trim();
            if (mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".oga", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                attachment = item;
                return true;
            }
        }

        return false;
    }
}
