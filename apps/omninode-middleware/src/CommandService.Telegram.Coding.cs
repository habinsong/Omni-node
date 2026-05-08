using System.Text;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    private async Task<string?> TryHandleTelegramCodingCommandAsync(
        string text,
        IReadOnlyList<InputAttachment>? attachments,
        IReadOnlyList<string>? webUrls,
        bool webSearchEnabled,
        CancellationToken cancellationToken
    )
    {
        if (!text.StartsWith("/coding", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var normalized = (text ?? string.Empty).Trim();
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 1 || tokens[1].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            return BuildTelegramHelpText("coding");
        }

        if (tokens[1].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            return BuildTelegramCodingStatusText();
        }

        if (tokens[1].Equals("last", StringComparison.OrdinalIgnoreCase)
            || tokens[1].Equals("result", StringComparison.OrdinalIgnoreCase))
        {
            return BuildTelegramLatestCodingResultText();
        }

        if (tokens[1].Equals("files", StringComparison.OrdinalIgnoreCase))
        {
            return BuildTelegramLatestCodingFilesText();
        }

        if (tokens[1].Equals("file", StringComparison.OrdinalIgnoreCase))
        {
            var query = ExtractCommandTail(normalized, "/coding file");
            return BuildTelegramLatestCodingFilePreviewText(query);
        }

        if (tokens[1].Equals("mode", StringComparison.OrdinalIgnoreCase))
        {
            if (tokens.Length < 3)
            {
                return "사용법: /coding mode <single|orchestration|multi>";
            }

            return SetTelegramCodingMode(tokens[2]);
        }

        if (tokens[1].Equals("language", StringComparison.OrdinalIgnoreCase))
        {
            if (tokens.Length < 3)
            {
                return "사용법: /coding language [single|orchestration|multi] <language|auto>";
            }

            if (tokens.Length >= 4 && IsCodingModeKey(tokens[2]))
            {
                return SetTelegramCodingLanguage(tokens[2], tokens[3]);
            }

            return SetTelegramCodingLanguage(null, tokens[2]);
        }

        if (tokens[1].Equals("run", StringComparison.OrdinalIgnoreCase))
        {
            var requestText = ExtractCommandTail(normalized, "/coding run");
            return await ExecuteTelegramCodingRunAsync(
                modeOverride: null,
                requestText,
                attachments,
                webUrls,
                webSearchEnabled,
                cancellationToken
            );
        }

        if (tokens[1].Equals("single", StringComparison.OrdinalIgnoreCase))
        {
            if (tokens.Length < 3)
            {
                return "사용법: /coding single <provider|model|run> ...";
            }

            if (tokens[2].Equals("provider", StringComparison.OrdinalIgnoreCase))
            {
                if (tokens.Length < 4)
                {
                    return "사용법: /coding single provider <auto|groq|gemini|copilot|cerebras|nvidia|codex>";
                }

                return SetTelegramCodingAggregateProvider("single", tokens[3]);
            }

            if (tokens[2].Equals("model", StringComparison.OrdinalIgnoreCase))
            {
                var modelId = string.Join(' ', tokens.Skip(3)).Trim();
                if (string.IsNullOrWhiteSpace(modelId))
                {
                    return "사용법: /coding single model <model-id>";
                }

                return SetTelegramCodingAggregateModel("single", modelId);
            }

            if (tokens[2].Equals("run", StringComparison.OrdinalIgnoreCase))
            {
                var requestText = ExtractCommandTail(normalized, "/coding single run");
                return await ExecuteTelegramCodingRunAsync(
                    modeOverride: "single",
                    requestText,
                    attachments,
                    webUrls,
                    webSearchEnabled,
                    cancellationToken
                );
            }

            return "사용법: /coding single <provider|model|run> ...";
        }

        if (tokens[1].Equals("orchestration", StringComparison.OrdinalIgnoreCase))
        {
            if (tokens.Length < 3)
            {
                return "사용법: /coding orchestration <provider|model|worker|run> ...";
            }

            if (tokens[2].Equals("provider", StringComparison.OrdinalIgnoreCase))
            {
                if (tokens.Length < 4)
                {
                    return "사용법: /coding orchestration provider <auto|groq|gemini|copilot|cerebras|nvidia|codex>";
                }

                return SetTelegramCodingAggregateProvider("orchestration", tokens[3]);
            }

            if (tokens[2].Equals("model", StringComparison.OrdinalIgnoreCase))
            {
                var modelId = string.Join(' ', tokens.Skip(3)).Trim();
                if (string.IsNullOrWhiteSpace(modelId))
                {
                    return "사용법: /coding orchestration model <model-id>";
                }

                return SetTelegramCodingAggregateModel("orchestration", modelId);
            }

            if (tokens[2].Equals("worker", StringComparison.OrdinalIgnoreCase))
            {
                if (tokens.Length < 5)
                {
                    return "사용법: /coding orchestration worker <groq|gemini|copilot|cerebras|nvidia|codex> <model-id|none>";
                }

                return SetTelegramCodingWorkerModel("orchestration", tokens[3], string.Join(' ', tokens.Skip(4)).Trim());
            }

            if (tokens[2].Equals("run", StringComparison.OrdinalIgnoreCase))
            {
                var requestText = ExtractCommandTail(normalized, "/coding orchestration run");
                return await ExecuteTelegramCodingRunAsync(
                    modeOverride: "orchestration",
                    requestText,
                    attachments,
                    webUrls,
                    webSearchEnabled,
                    cancellationToken
                );
            }

            return "사용법: /coding orchestration <provider|model|worker|run> ...";
        }

        if (tokens[1].Equals("multi", StringComparison.OrdinalIgnoreCase))
        {
            if (tokens.Length < 3)
            {
                return "사용법: /coding multi <provider|model|worker|run> ...";
            }

            if (tokens[2].Equals("provider", StringComparison.OrdinalIgnoreCase)
                || tokens[2].Equals("summary", StringComparison.OrdinalIgnoreCase))
            {
                if (tokens.Length < 4)
                {
                    return "사용법: /coding multi provider <auto|groq|gemini|copilot|cerebras|nvidia|codex>";
                }

                return SetTelegramCodingAggregateProvider("multi", tokens[3]);
            }

            if (tokens[2].Equals("model", StringComparison.OrdinalIgnoreCase))
            {
                var modelId = string.Join(' ', tokens.Skip(3)).Trim();
                if (string.IsNullOrWhiteSpace(modelId))
                {
                    return "사용법: /coding multi model <model-id>";
                }

                return SetTelegramCodingAggregateModel("multi", modelId);
            }

            if (tokens[2].Equals("worker", StringComparison.OrdinalIgnoreCase))
            {
                if (tokens.Length < 5)
                {
                    return "사용법: /coding multi worker <groq|gemini|copilot|cerebras|codex> <model-id|none>";
                }

                return SetTelegramCodingWorkerModel("multi", tokens[3], string.Join(' ', tokens.Skip(4)).Trim());
            }

            if (tokens[2].Equals("run", StringComparison.OrdinalIgnoreCase))
            {
                var requestText = ExtractCommandTail(normalized, "/coding multi run");
                return await ExecuteTelegramCodingRunAsync(
                    modeOverride: "multi",
                    requestText,
                    attachments,
                    webUrls,
                    webSearchEnabled,
                    cancellationToken
                );
            }

            return "사용법: /coding multi <provider|model|worker|run> ...";
        }

        return BuildTelegramHelpText("coding");
    }

    private async Task<string?> TryHandleTelegramRefactorCommandAsync(
        string text,
        CancellationToken cancellationToken
    )
    {
        if (!text.StartsWith("/refactor", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var normalized = (text ?? string.Empty).Replace("\r\n", "\n").Trim();
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 1 || tokens[1].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            return BuildTelegramHelpText("refactor");
        }

        if (tokens[1].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            return BuildTelegramRefactorStatusText();
        }

        if (tokens[1].Equals("read", StringComparison.OrdinalIgnoreCase))
        {
            if (tokens.Length < 3)
            {
                return "사용법: /refactor read <path> [start] [end]";
            }

            var path = tokens[2];
            int? start = null;
            int? end = null;
            if (tokens.Length >= 4 && int.TryParse(tokens[3], out var parsedStart))
            {
                start = Math.Max(1, parsedStart);
            }

            if (tokens.Length >= 5 && int.TryParse(tokens[4], out var parsedEnd))
            {
                end = Math.Max(start ?? 1, parsedEnd);
            }

            var result = await ReadWithAnchorsAsync(path, cancellationToken);
            if (!result.Ok || result.ReadResult == null)
            {
                UpdateTelegramRefactorSession(path, null, result.Message);
                return $"Safe Refactor 읽기 실패: {result.Message}";
            }

            UpdateTelegramRefactorSession(result.ReadResult.Path, null, result.Message);
            return BuildTelegramRefactorReadText(result.ReadResult, start, end);
        }

        if (tokens[1].Equals("preview", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseTelegramRefactorPreviewCommand(
                    normalized,
                    out var path,
                    out var startLine,
                    out var endLine,
                    out var replacement,
                    out var parseError))
            {
                return parseError;
            }

            var readResult = await ReadWithAnchorsAsync(path, cancellationToken);
            if (!readResult.Ok || readResult.ReadResult == null)
            {
                UpdateTelegramRefactorSession(path, null, readResult.Message);
                return $"Safe Refactor 미리보기 실패: {readResult.Message}";
            }

            var anchorLines = readResult.ReadResult.Lines
                .Where(line => line.LineNumber >= startLine && line.LineNumber <= endLine)
                .OrderBy(line => line.LineNumber)
                .ToArray();
            var expectedCount = endLine - startLine + 1;
            if (anchorLines.Length != expectedCount)
            {
                UpdateTelegramRefactorSession(path, null, "요청 범위 anchor를 찾지 못했습니다.");
                return $"Safe Refactor 미리보기 실패: L{startLine}~L{endLine} anchor를 읽지 못했습니다. 먼저 `/refactor read {path} {startLine} {endLine}` 로 범위를 확인하세요.";
            }

            var preview = await PreviewRefactorAsync(
                path,
                new[]
                {
                    new AnchorEditRequest(
                        startLine,
                        endLine,
                        anchorLines.Select(line => line.Hash).ToArray(),
                        replacement
                    )
                },
                cancellationToken
            );
            if (!preview.Ok || preview.Preview == null)
            {
                UpdateTelegramRefactorSession(path, null, preview.Message);
                return BuildTelegramRefactorFailureText("Safe Refactor 미리보기 실패", preview);
            }

            UpdateTelegramRefactorSession(preview.Preview.Path, preview.Preview.PreviewId, preview.Message);
            return BuildTelegramRefactorPreviewText(preview.Preview);
        }

        if (tokens[1].Equals("apply", StringComparison.OrdinalIgnoreCase))
        {
            var previewId = tokens.Length >= 3 ? tokens[2] : GetTelegramRefactorSession().PreviewId;
            if (string.IsNullOrWhiteSpace(previewId))
            {
                return "적용할 previewId가 없습니다. 먼저 `/refactor preview ...` 를 실행하세요.";
            }

            var apply = await ApplyRefactorAsync(previewId, cancellationToken);
            UpdateTelegramRefactorSession(
                apply.ApplyResult?.Path,
                apply.Ok ? string.Empty : previewId,
                apply.Message
            );
            if (!apply.Ok || apply.ApplyResult == null)
            {
                return BuildTelegramRefactorFailureText("Safe Refactor 적용 실패", apply);
            }

            return $"""
                    [Safe Refactor 적용 완료]
                    파일: {apply.ApplyResult.Path}
                    previewId: {apply.ApplyResult.PreviewId}
                    적용 시각: {apply.ApplyResult.AppliedAtUtc}
                    메시지: {apply.Message}
                    """;
        }

        return BuildTelegramHelpText("refactor");
    }

    private string SetTelegramCodingMode(string mode)
    {
        var normalized = (mode ?? string.Empty).Trim().ToLowerInvariant();
        if (!IsCodingModeKey(normalized))
        {
            return "지원 코딩 모드는 single, orchestration, multi 입니다.";
        }

        lock (_telegramCodingLock)
        {
            _telegramCodingPreferences.Mode = normalized;
        }

        return $"텔레그램 코딩 모드를 {FormatModeDisplayName(normalized)} 코딩으로 바꿨습니다.";
    }

    private string SetTelegramCodingLanguage(string? mode, string language)
    {
        var resolvedMode = string.IsNullOrWhiteSpace(mode) ? GetTelegramCodingMode() : (mode ?? string.Empty).Trim().ToLowerInvariant();
        if (!IsCodingModeKey(resolvedMode))
        {
            return "지원 코딩 모드는 single, orchestration, multi 입니다.";
        }

        var normalizedLanguage = NormalizeTelegramCodingLanguageSetting(language);
        lock (_telegramCodingLock)
        {
            if (resolvedMode == "single")
            {
                _telegramCodingPreferences.SingleLanguage = normalizedLanguage;
            }
            else if (resolvedMode == "orchestration")
            {
                _telegramCodingPreferences.OrchestrationLanguage = normalizedLanguage;
            }
            else
            {
                _telegramCodingPreferences.MultiLanguage = normalizedLanguage;
            }
        }

        return $"텔레그램 {FormatModeDisplayName(resolvedMode)} 코딩 언어를 {normalizedLanguage}로 바꿨습니다.";
    }

    private string SetTelegramCodingAggregateProvider(string mode, string provider)
    {
        var normalizedMode = (mode ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedProvider = NormalizeTelegramCodingProviderSetting(provider, allowAuto: true);
        if (!IsCodingModeKey(normalizedMode) || normalizedProvider == null)
        {
            return "지원 제공자는 auto, groq, gemini, copilot, cerebras, codex 입니다.";
        }

        lock (_telegramCodingLock)
        {
            if (normalizedMode == "single")
            {
                _telegramCodingPreferences.SingleProvider = normalizedProvider;
            }
            else if (normalizedMode == "orchestration")
            {
                _telegramCodingPreferences.OrchestrationProvider = normalizedProvider;
            }
            else
            {
                _telegramCodingPreferences.MultiProvider = normalizedProvider;
            }
        }

        return $"텔레그램 {FormatModeDisplayName(normalizedMode)} 코딩 제공자를 {FormatProviderDisplayName(normalizedProvider, allowAuto: true)}로 바꿨습니다.";
    }

    private string SetTelegramCodingAggregateModel(string mode, string modelId)
    {
        var normalizedMode = (mode ?? string.Empty).Trim().ToLowerInvariant();
        var requestedModel = NormalizeModelSelection(modelId) ?? (modelId ?? string.Empty).Trim();
        var normalizedModel = requestedModel;
        if (!IsCodingModeKey(normalizedMode) || string.IsNullOrWhiteSpace(normalizedModel))
        {
            return "model-id를 입력하세요.";
        }

        lock (_telegramCodingLock)
        {
            var targetProvider = normalizedMode switch
            {
                "single" => _telegramCodingPreferences.SingleProvider,
                "orchestration" => _telegramCodingPreferences.OrchestrationProvider,
                _ => _telegramCodingPreferences.MultiProvider
            };
            if (IsPinnedCopilotProvider(targetProvider))
            {
                normalizedModel = DefaultCopilotModel;
            }

            if (normalizedMode == "single")
            {
                _telegramCodingPreferences.SingleModel = normalizedModel;
            }
            else if (normalizedMode == "orchestration")
            {
                _telegramCodingPreferences.OrchestrationModel = normalizedModel;
            }
            else
            {
                _telegramCodingPreferences.MultiModel = normalizedModel;
            }
        }

        return $"텔레그램 {FormatModeDisplayName(normalizedMode)} 코딩 모델을 {normalizedModel}로 바꿨습니다.";
    }

    private string SetTelegramCodingWorkerModel(string mode, string provider, string modelId)
    {
        var normalizedMode = (mode ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedProvider = NormalizeTelegramCodingProviderSetting(provider, allowAuto: false);
        var normalizedModel = string.Equals((modelId ?? string.Empty).Trim(), "none", StringComparison.OrdinalIgnoreCase)
            ? "none"
            : NormalizeModelSelection(modelId) ?? (modelId ?? string.Empty).Trim();
        if ((normalizedMode != "orchestration" && normalizedMode != "multi")
            || normalizedProvider == null
            || string.IsNullOrWhiteSpace(normalizedModel))
        {
            return "사용법: /coding <orchestration|multi> worker <groq|gemini|copilot|cerebras|codex> <model-id|none>";
        }

        if (IsPinnedCopilotProvider(normalizedProvider) && !string.Equals(normalizedModel, "none", StringComparison.OrdinalIgnoreCase))
        {
            normalizedModel = DefaultCopilotModel;
        }

        lock (_telegramCodingLock)
        {
            if (normalizedMode == "orchestration")
            {
                switch (normalizedProvider)
                {
                    case "groq":
                        _telegramCodingPreferences.OrchestrationGroqModel = normalizedModel;
                        break;
                    case "gemini":
                        _telegramCodingPreferences.OrchestrationGeminiModel = normalizedModel;
                        break;
                    case "copilot":
                        _telegramCodingPreferences.OrchestrationCopilotModel = normalizedModel;
                        break;
                    case "cerebras":
                        _telegramCodingPreferences.OrchestrationCerebrasModel = normalizedModel;
                        break;
                    case "nvidia":
                        _telegramCodingPreferences.OrchestrationNvidiaModel = normalizedModel;
                        break;
                    case "codex":
                        _telegramCodingPreferences.OrchestrationCodexModel = normalizedModel;
                        break;
                }
            }
            else
            {
                switch (normalizedProvider)
                {
                    case "groq":
                        _telegramCodingPreferences.MultiGroqModel = normalizedModel;
                        break;
                    case "gemini":
                        _telegramCodingPreferences.MultiGeminiModel = normalizedModel;
                        break;
                    case "copilot":
                        _telegramCodingPreferences.MultiCopilotModel = normalizedModel;
                        break;
                    case "cerebras":
                        _telegramCodingPreferences.MultiCerebrasModel = normalizedModel;
                        break;
                    case "nvidia":
                        _telegramCodingPreferences.MultiNvidiaModel = normalizedModel;
                        break;
                    case "codex":
                        _telegramCodingPreferences.MultiCodexModel = normalizedModel;
                        break;
                }
            }
        }

        return $"텔레그램 {FormatModeDisplayName(normalizedMode)} 코딩 워커 {FormatProviderDisplayName(normalizedProvider)} 모델을 {normalizedModel}로 바꿨습니다.";
    }

    private async Task<string> ExecuteTelegramCodingRunAsync(
        string? modeOverride,
        string requestText,
        IReadOnlyList<InputAttachment>? attachments,
        IReadOnlyList<string>? webUrls,
        bool webSearchEnabled,
        CancellationToken cancellationToken
    )
    {
        var snapshot = GetTelegramCodingPreferences();
        var mode = string.IsNullOrWhiteSpace(modeOverride) ? snapshot.Mode : modeOverride.Trim().ToLowerInvariant();
        if (!IsCodingModeKey(mode))
        {
            return "지원 코딩 모드는 single, orchestration, multi 입니다.";
        }

        var normalizedAttachments = NormalizeAttachments(attachments);
        var input = (requestText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            input = mode == "orchestration"
                ? (normalizedAttachments.Count > 0 ? "첨부 파일을 반영해 코딩해줘" : string.Empty)
                : (normalizedAttachments.Count > 0 ? "첨부 파일을 반영해 코딩해줘" : string.Empty);
        }

        if (mode != "orchestration" && string.IsNullOrWhiteSpace(input))
        {
            return "코딩 요구사항을 입력하세요. 예: /coding single run 로그인 페이지와 API 연결까지 만들어줘";
        }

        var conversation = EnsureTelegramLinkedCodingConversation(mode);
        var resolvedWebUrls = ResolveWebUrls(input, webUrls, webSearchEnabled);

        if (mode == "single")
        {
            var result = await RunCodingSingleAsync(
                new CodingRunRequest(
                    input,
                    "telegram",
                    "coding",
                    "single",
                    conversation.Id,
                    conversation.Title,
                    "Telegram",
                    "코딩",
                    new[] { "telegram-coding-link", "shared", "telegram" },
                    snapshot.SingleProvider,
                    snapshot.SingleModel,
                    snapshot.SingleLanguage,
                    conversation.LinkedMemoryNotes,
                    Attachments: normalizedAttachments,
                    WebUrls: resolvedWebUrls,
                    WebSearchEnabled: webSearchEnabled
                ),
                cancellationToken
            );
            return BuildTelegramCodingRunText(result, "코딩 실행 완료");
        }

        if (mode == "orchestration")
        {
            var result = await RunCodingOrchestrationAsync(
                new CodingRunRequest(
                    input,
                    "telegram",
                    "coding",
                    "orchestration",
                    conversation.Id,
                    conversation.Title,
                    "Telegram",
                    "코딩",
                    new[] { "telegram-coding-link", "shared", "telegram" },
                    snapshot.OrchestrationProvider,
                    snapshot.OrchestrationModel,
                    snapshot.OrchestrationLanguage,
                    conversation.LinkedMemoryNotes,
                    snapshot.OrchestrationGroqModel,
                    snapshot.OrchestrationGeminiModel,
                    snapshot.OrchestrationCerebrasModel,
                    snapshot.OrchestrationCopilotModel,
                    normalizedAttachments,
                    resolvedWebUrls,
                    webSearchEnabled,
                    snapshot.OrchestrationCodexModel,
                    NvidiaModel: snapshot.OrchestrationNvidiaModel
                ),
                cancellationToken
            );
            return BuildTelegramCodingRunText(result, "코딩 실행 완료");
        }

        var multiResult = await RunCodingMultiAsync(
            new CodingRunRequest(
                input,
                "telegram",
                "coding",
                "multi",
                conversation.Id,
                conversation.Title,
                "Telegram",
                "코딩",
                new[] { "telegram-coding-link", "shared", "telegram" },
                snapshot.MultiProvider,
                snapshot.MultiModel,
                snapshot.MultiLanguage,
                conversation.LinkedMemoryNotes,
                snapshot.MultiGroqModel,
                snapshot.MultiGeminiModel,
                snapshot.MultiCerebrasModel,
                snapshot.MultiCopilotModel,
                normalizedAttachments,
                resolvedWebUrls,
                webSearchEnabled,
                snapshot.MultiCodexModel,
                NvidiaModel: snapshot.MultiNvidiaModel
            ),
            cancellationToken
        );
        return BuildTelegramCodingRunText(multiResult, "코딩 실행 완료");
    }

    private string BuildTelegramCodingStatusText()
    {
        var snapshot = GetTelegramCodingPreferences();
        var latest = GetLatestTelegramCodingConversation();
        var builder = new StringBuilder();
        builder.AppendLine("[텔레그램 코딩 설정]");
        builder.AppendLine($"현재 모드: {FormatModeDisplayName(snapshot.Mode)} 코딩");
        builder.AppendLine($"단일: {FormatProviderWithModel(snapshot.SingleProvider, snapshot.SingleModel, allowAuto: true)} / 언어={snapshot.SingleLanguage}");
        builder.AppendLine($"오케스트레이션 주 구현: {FormatProviderWithModel(snapshot.OrchestrationProvider, snapshot.OrchestrationModel, allowAuto: true)} / 언어={snapshot.OrchestrationLanguage}");
        builder.AppendLine($"오케스트레이션 워커: Groq={FormatCodingWorkerModel(snapshot.OrchestrationGroqModel)}, Gemini={FormatCodingWorkerModel(snapshot.OrchestrationGeminiModel)}, Cerebras={FormatCodingWorkerModel(snapshot.OrchestrationCerebrasModel)}, NVIDIA NIM={FormatCodingWorkerModel(snapshot.OrchestrationNvidiaModel)}, Copilot={FormatCodingWorkerModel(snapshot.OrchestrationCopilotModel)}, Codex={FormatCodingWorkerModel(snapshot.OrchestrationCodexModel)}");
        builder.AppendLine($"다중 요약: {FormatProviderWithModel(snapshot.MultiProvider, snapshot.MultiModel, allowAuto: true)} / 언어={snapshot.MultiLanguage}");
        builder.AppendLine($"다중 워커: Groq={FormatCodingWorkerModel(snapshot.MultiGroqModel)}, Gemini={FormatCodingWorkerModel(snapshot.MultiGeminiModel)}, Cerebras={FormatCodingWorkerModel(snapshot.MultiCerebrasModel)}, NVIDIA NIM={FormatCodingWorkerModel(snapshot.MultiNvidiaModel)}, Copilot={FormatCodingWorkerModel(snapshot.MultiCopilotModel)}, Codex={FormatCodingWorkerModel(snapshot.MultiCodexModel)}");
        builder.AppendLine();
        if (latest?.LatestCodingResult != null)
        {
            builder.AppendLine("[최근 결과 요약]");
            builder.AppendLine($"대화: {latest.Title}");
            builder.AppendLine($"모드: {FormatModeDisplayName(latest.LatestCodingResult.Mode)} 코딩");
            builder.AppendLine($"상태: {latest.LatestCodingResult.Execution.Status} (exit={latest.LatestCodingResult.Execution.ExitCode})");
            builder.AppendLine($"변경 파일: {latest.LatestCodingResult.ChangedFiles.Count}개");
            builder.AppendLine($"열기: /coding last");
        }
        else
        {
            builder.AppendLine("[최근 결과 요약]");
            builder.AppendLine("아직 텔레그램 코딩 실행 이력이 없습니다.");
        }

        return builder.ToString().Trim();
    }

    private string BuildTelegramLatestCodingResultText()
    {
        var latest = GetLatestTelegramCodingConversation();
        if (latest?.LatestCodingResult == null)
        {
            return "최근 텔레그램 코딩 결과가 없습니다. 먼저 `/coding run <요구사항>` 또는 `단일/오케스트레이션/다중 코딩으로 ...` 를 실행하세요.";
        }

        return BuildTelegramCodingResultText(latest, latest.LatestCodingResult, "최근 코딩 결과");
    }

    private string BuildTelegramLatestCodingFilesText()
    {
        var latest = GetLatestTelegramCodingConversation();
        var result = latest?.LatestCodingResult;
        if (latest == null || result == null)
        {
            return "최근 텔레그램 코딩 결과가 없습니다.";
        }

        if (result.ChangedFiles.Count == 0)
        {
            return "최근 코딩 결과에 변경 파일이 없습니다.";
        }

        var builder = new StringBuilder();
        builder.AppendLine("[최근 코딩 파일]");
        builder.AppendLine($"대화: {latest.Title}");
        for (var i = 0; i < result.ChangedFiles.Count; i += 1)
        {
            builder.AppendLine($"{i + 1}. {ToTelegramRelativePath(result.Execution.RunDirectory, result.ChangedFiles[i])}");
        }

        builder.AppendLine();
        builder.AppendLine("프리뷰 예시:");
        builder.AppendLine("- /coding file 1");
        builder.AppendLine("- /coding file src/App.tsx");
        return builder.ToString().Trim();
    }

    private string BuildTelegramLatestCodingFilePreviewText(string query)
    {
        var latest = GetLatestTelegramCodingConversation();
        var result = latest?.LatestCodingResult;
        if (latest == null || result == null)
        {
            return "최근 텔레그램 코딩 결과가 없습니다.";
        }

        if (!TryResolveTelegramCodingFilePath(result, query, out var path, out var displayPath))
        {
            return "파일을 찾지 못했습니다. 먼저 `/coding files` 로 번호나 경로를 확인하세요.";
        }

        if (!File.Exists(path))
        {
            return $"파일을 찾을 수 없습니다: {displayPath}";
        }

        var content = File.ReadAllText(path);
        var preview = TrimTelegramCodePreview(content, 2600);
        return $"""
                [코딩 파일 프리뷰]
                대화: {latest.Title}
                파일: {displayPath}

                {preview}
                """;
    }

    private string BuildTelegramCodingRunText(CodingRunResult result, string heading)
    {
        var snapshot = BuildConversationCodingResultSnapshot(result);
        return BuildTelegramCodingResultText(result.Conversation, snapshot, heading);
    }

    private string BuildTelegramCodingResultText(
        ConversationThreadView conversation,
        ConversationCodingResultSnapshot result,
        string heading
    )
    {
        var builder = new StringBuilder();
        builder.AppendLine($"[{heading}]");
        builder.AppendLine($"대화: {conversation.Title}");
        builder.AppendLine($"모드: {FormatModeDisplayName(result.Mode)} 코딩");
        builder.AppendLine($"모델: {FormatProviderWithModel(result.Provider, result.Model, allowAuto: true)}");
        builder.AppendLine($"언어: {result.Language}");
        builder.AppendLine($"작업 폴더: {result.Execution.RunDirectory}");
        builder.AppendLine($"상태: {result.Execution.Status} (exit={result.Execution.ExitCode})");
        if (!string.IsNullOrWhiteSpace(result.Execution.Command)
            && result.Execution.Command != "(none)"
            && result.Execution.Command != "-")
        {
            builder.AppendLine($"실행 명령: {TrimForOutput(result.Execution.Command, 200)}");
        }

        builder.AppendLine($"변경 파일: {result.ChangedFiles.Count}개");
        foreach (var path in result.ChangedFiles.Take(8))
        {
            builder.AppendLine($"- {ToTelegramRelativePath(result.Execution.RunDirectory, path)}");
        }

        if (result.ChangedFiles.Count > 8)
        {
            builder.AppendLine($"- ...(추가 {result.ChangedFiles.Count - 8}개)");
        }

        var summaryText = TrimForOutput(RemoveCodeBlocksFromText(result.Summary), 1400);
        if (!string.IsNullOrWhiteSpace(summaryText))
        {
            builder.AppendLine();
            builder.AppendLine("요약:");
            builder.AppendLine(summaryText);
        }

        if (result.Mode == "multi")
        {
            AppendTelegramCodingMultiSections(builder, result);
        }
        else if (result.Workers.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("단계별 결과:");
            foreach (var worker in result.Workers)
            {
                builder.AppendLine(BuildTelegramCodingWorkerSnapshotDigest(worker));
            }
        }

        return FormatTelegramResponse(builder.ToString().Trim(), TelegramMaxResponseChars);
    }

    private void AppendTelegramCodingMultiSections(StringBuilder builder, ConversationCodingResultSnapshot result)
    {
        if (!string.IsNullOrWhiteSpace(result.CommonSummary))
        {
            builder.AppendLine();
            builder.AppendLine("공통 요약:");
            builder.AppendLine(TrimForOutput(RemoveCodeBlocksFromText(result.CommonSummary), 700));
        }

        if (!string.IsNullOrWhiteSpace(result.CommonPoints))
        {
            builder.AppendLine();
            builder.AppendLine("공통점:");
            builder.AppendLine(TrimForOutput(RemoveCodeBlocksFromText(result.CommonPoints), 500));
        }

        if (!string.IsNullOrWhiteSpace(result.Differences))
        {
            builder.AppendLine();
            builder.AppendLine("차이:");
            builder.AppendLine(TrimForOutput(RemoveCodeBlocksFromText(result.Differences), 500));
        }

        if (!string.IsNullOrWhiteSpace(result.Recommendation))
        {
            builder.AppendLine();
            builder.AppendLine("추천:");
            builder.AppendLine(TrimForOutput(RemoveCodeBlocksFromText(result.Recommendation), 400));
        }

        if (result.Workers.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("모델별 결과:");
            for (var i = 0; i < result.Workers.Count; i += 1)
            {
                builder.AppendLine($"{i + 1}. {BuildTelegramCodingWorkerSnapshotDigest(result.Workers[i])}");
            }
        }
    }

    private static string BuildTelegramCodingWorkerSnapshotDigest(CodingWorkerResultSnapshot worker)
    {
        var changedFiles = worker.ChangedFiles.Take(4).Select(path => Path.GetFileName(path)).ToArray();
        var filesText = changedFiles.Length == 0 ? "파일 없음" : string.Join(", ", changedFiles);
        var summary = TrimForOutput(RemoveCodeBlocksFromText(worker.Summary), 220);
        var summaryText = string.IsNullOrWhiteSpace(summary) ? "요약 없음" : summary;
        var role = string.IsNullOrWhiteSpace(worker.Role) ? "독립 완주" : worker.Role;
        return $"{FormatProviderDisplayName(worker.Provider)} ({worker.Model}) · 역할={role} · status={worker.Execution.Status} · exit={worker.Execution.ExitCode} · 파일={filesText} · 요약={summaryText}";
    }

    private string BuildTelegramRefactorStatusText()
    {
        var snapshot = GetTelegramRefactorSession();
        if (string.IsNullOrWhiteSpace(snapshot.Path) && string.IsNullOrWhiteSpace(snapshot.PreviewId))
        {
            return "최근 Safe Refactor 상태가 없습니다. `/refactor read <path>` 또는 `/refactor preview ...` 를 먼저 실행하세요.";
        }

        return $"""
                [Safe Refactor 상태]
                파일: {snapshot.Path}
                previewId: {snapshot.PreviewId}
                마지막 메시지: {snapshot.LastMessage}
                갱신 시각: {snapshot.UpdatedAtLocal}
                """;
    }

    private string BuildTelegramRefactorReadText(AnchorReadResult readResult, int? startLine, int? endLine)
    {
        var filteredLines = readResult.Lines
            .Where(line => (!startLine.HasValue || line.LineNumber >= startLine.Value)
                        && (!endLine.HasValue || line.LineNumber <= endLine.Value))
            .Take(24)
            .ToArray();
        if (filteredLines.Length == 0)
        {
            return $"""
                    [Safe Refactor 읽기]
                    파일: {readResult.Path}
                    표시 가능한 line을 찾지 못했습니다.
                    먼저 `/refactor read {readResult.Path}` 로 전체 표시 범위를 확인하세요.
                    """;
        }

        var builder = new StringBuilder();
        builder.AppendLine("[Safe Refactor 읽기]");
        builder.AppendLine($"파일: {readResult.Path}");
        builder.AppendLine($"line: {readResult.TotalLines}개{(readResult.Truncated ? " (일부만 표시)" : string.Empty)}");
        foreach (var line in filteredLines)
        {
            builder.AppendLine($"L{line.LineNumber} {ClipHash(line.Hash)} | {ClampTelegramInline(line.Content, 110)}");
        }

        builder.AppendLine();
        builder.AppendLine("미리보기 예시:");
        builder.AppendLine($"/refactor preview {readResult.Path} {filteredLines[0].LineNumber} {filteredLines[^1].LineNumber}");
        builder.AppendLine("바꿀 코드를 다음 줄부터 붙여 넣으세요.");
        return builder.ToString().Trim();
    }

    private static string BuildTelegramRefactorPreviewText(RefactorPreview preview)
    {
        var diffPreview = TrimForOutput(preview.UnifiedDiff, 2200);
        return $"""
                [Safe Refactor 미리보기]
                파일: {preview.Path}
                previewId: {preview.PreviewId}
                safeToApply: {(preview.SafeToApply ? "yes" : "no")}

                diff:
                {diffPreview}

                적용:
                - /refactor apply
                - /refactor apply {preview.PreviewId}
                """;
    }

    private static string BuildTelegramRefactorFailureText(string heading, RefactorActionResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"[{heading}]");
        builder.AppendLine(result.Message);
        foreach (var issue in (result.Issues ?? Array.Empty<AnchorEditIssue>()).Take(4))
        {
            builder.AppendLine($"- L{issue.StartLine}~L{issue.EndLine}: {issue.Reason}");
            if (!string.IsNullOrWhiteSpace(issue.CurrentSnippet))
            {
                builder.AppendLine($"  현재: {ClampTelegramInline(issue.CurrentSnippet, 140)}");
            }
        }

        return builder.ToString().Trim();
    }

    private bool TryParseTelegramRefactorPreviewCommand(
        string text,
        out string path,
        out int startLine,
        out int endLine,
        out string replacement,
        out string error
    )
    {
        path = string.Empty;
        startLine = 0;
        endLine = 0;
        replacement = string.Empty;
        error = "사용법: /refactor preview <path> <start> <end> 다음 줄부터 교체 코드";

        var snapshot = GetTelegramRefactorSession();
        var normalized = (text ?? string.Empty).Replace("\r\n", "\n").Trim();
        var payload = ExtractCommandTail(normalized, "/refactor preview");
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var newlineIndex = payload.IndexOf('\n');
        string header;
        if (newlineIndex >= 0)
        {
            header = payload[..newlineIndex].Trim();
            replacement = payload[(newlineIndex + 1)..].Trim();
        }
        else
        {
            var separatorIndex = payload.IndexOf(":::", StringComparison.Ordinal);
            if (separatorIndex < 0)
            {
                error = "교체 코드는 줄바꿈 다음 줄에 붙여 넣거나 `:::` 뒤에 써 주세요.";
                return false;
            }

            header = payload[..separatorIndex].Trim();
            replacement = payload[(separatorIndex + 3)..].Trim();
        }

        if (string.IsNullOrWhiteSpace(replacement))
        {
            error = "교체 코드가 비어 있습니다.";
            return false;
        }

        var tokens = header.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 3
            && int.TryParse(tokens[1], out startLine)
            && int.TryParse(tokens[2], out endLine))
        {
            path = tokens[0];
        }
        else if (tokens.Length == 2
                 && int.TryParse(tokens[0], out startLine)
                 && int.TryParse(tokens[1], out endLine)
                 && !string.IsNullOrWhiteSpace(snapshot.Path))
        {
            path = snapshot.Path;
        }
        else
        {
            error = string.IsNullOrWhiteSpace(snapshot.Path)
                ? "사용법: /refactor preview <path> <start> <end> 다음 줄부터 교체 코드"
                : $"사용법: /refactor preview <path> <start> <end> 또는 /refactor preview <start> <end> (현재 파일: {snapshot.Path})";
            return false;
        }

        startLine = Math.Max(1, startLine);
        endLine = Math.Max(startLine, endLine);
        return !string.IsNullOrWhiteSpace(path);
    }

    private ConversationThreadView EnsureTelegramLinkedCodingConversation(string mode)
    {
        var normalizedMode = IsCodingModeKey(mode) ? mode.Trim().ToLowerInvariant() : "single";
        var existing = _conversationStore
            .List("coding", normalizedMode)
            .FirstOrDefault(item => item.Tags.Any(tag =>
                string.Equals(tag, "telegram-coding-link", StringComparison.OrdinalIgnoreCase)));
        if (existing != null)
        {
            return _conversationStore.Get(existing.Id)
                   ?? _conversationStore.Ensure("coding", normalizedMode, existing.Id, null, null, null, null);
        }

        return _conversationStore.Create(
            "coding",
            normalizedMode,
            $"Telegram 코딩 ({FormatModeDisplayName(normalizedMode)})",
            "Telegram",
            "코딩",
            new[] { "telegram-coding-link", "shared", "telegram" }
        );
    }

    private ConversationThreadView? GetLatestTelegramCodingConversation()
    {
        var latest = _conversationStore
            .ListAll()
            .Where(item => item.Scope.Equals("coding", StringComparison.OrdinalIgnoreCase)
                && item.Tags.Any(tag => string.Equals(tag, "telegram-coding-link", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(item => item.UpdatedUtc)
            .FirstOrDefault();
        return latest == null ? null : _conversationStore.Get(latest.Id);
    }

    private TelegramCodingPreferences GetTelegramCodingPreferences()
    {
        lock (_telegramCodingLock)
        {
            return _telegramCodingPreferences.Clone();
        }
    }

    private string GetTelegramCodingMode()
    {
        lock (_telegramCodingLock)
        {
            return _telegramCodingPreferences.Mode;
        }
    }

    private void UpdateTelegramRefactorSession(string? path, string? previewId, string? message)
    {
        lock (_telegramRefactorLock)
        {
            if (path != null)
            {
                _telegramRefactorSession.Path = path.Trim();
            }

            if (previewId != null)
            {
                _telegramRefactorSession.PreviewId = previewId.Trim();
            }

            _telegramRefactorSession.LastMessage = (message ?? string.Empty).Trim();
            _telegramRefactorSession.UpdatedAtLocal = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }

    private TelegramRefactorSession GetTelegramRefactorSession()
    {
        lock (_telegramRefactorLock)
        {
            return _telegramRefactorSession.Clone();
        }
    }

    private static bool IsCodingModeKey(string? mode)
    {
        var normalized = (mode ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "single" or "orchestration" or "multi";
    }

    private static string? NormalizeTelegramCodingProviderSetting(string? provider, bool allowAuto)
    {
        var normalized = (provider ?? string.Empty).Trim().ToLowerInvariant();
        if (allowAuto && normalized == "auto")
        {
            return "auto";
        }

        if (normalized is "nvidia-nim" or "nvidia_nim" or "nim")
        {
            normalized = "nvidia";
        }

        return normalized is "groq" or "gemini" or "copilot" or "cerebras" or "nvidia" or "codex"
            ? normalized
            : null;
    }

    private static string NormalizeTelegramCodingLanguageSetting(string? language)
    {
        var normalized = (language ?? string.Empty).Trim().ToLowerInvariant();
        return normalized.Length == 0 || normalized == "auto"
            ? "auto"
            : NormalizeLanguageForCode(normalized);
    }

    private static string FormatCodingWorkerModel(string? model)
    {
        return string.IsNullOrWhiteSpace(model) || model.Equals("none", StringComparison.OrdinalIgnoreCase)
            ? "선택 안함"
            : model.Trim();
    }

    private static string ToTelegramRelativePath(string? runDirectory, string path)
    {
        var fullPath = (path ?? string.Empty).Trim();
        var root = (runDirectory ?? string.Empty).Trim();
        if (fullPath.Length == 0)
        {
            return "(none)";
        }

        if (root.Length > 0 && fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            var relative = fullPath[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return relative.Length == 0 ? Path.GetFileName(fullPath) : relative;
        }

        return fullPath;
    }

    private static string ExtractCommandTail(string text, string prefix)
    {
        var normalizedText = (text ?? string.Empty).Trim();
        var normalizedPrefix = (prefix ?? string.Empty).Trim();
        if (!normalizedText.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return normalizedText[normalizedPrefix.Length..].TrimStart();
    }

    private static string ClipHash(string hash)
    {
        var normalized = (hash ?? string.Empty).Trim();
        return normalized.Length <= 10 ? normalized : normalized[..10];
    }

    private static string ClampTelegramInline(string text, int maxLength)
    {
        var normalized = Regex.Replace((text ?? string.Empty).Trim(), @"\s+", " ");
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..Math.Max(0, maxLength - 3)] + "...";
    }

    private static string TrimTelegramCodePreview(string content, int maxLength)
    {
        var normalized = (content ?? string.Empty).Replace("\r\n", "\n").Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..Math.Max(0, maxLength - 18)] + "\n...(이하 생략)";
    }

    private static bool TryResolveTelegramCodingFilePath(
        ConversationCodingResultSnapshot result,
        string query,
        out string path,
        out string displayPath
    )
    {
        path = string.Empty;
        displayPath = string.Empty;
        var files = result.ChangedFiles?.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray() ?? Array.Empty<string>();
        if (files.Length == 0)
        {
            return false;
        }

        var normalizedQuery = (query ?? string.Empty).Trim();
        if (int.TryParse(normalizedQuery, out var parsedIndex) && parsedIndex >= 1 && parsedIndex <= files.Length)
        {
            path = files[parsedIndex - 1];
            displayPath = ToTelegramRelativePath(result.Execution.RunDirectory, path);
            return true;
        }

        if (normalizedQuery.Length == 0)
        {
            path = files[0];
            displayPath = ToTelegramRelativePath(result.Execution.RunDirectory, path);
            return true;
        }

        var matched = files.FirstOrDefault(item =>
            item.Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase)
            || ToTelegramRelativePath(result.Execution.RunDirectory, item).Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase)
            || item.EndsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase)
            || ToTelegramRelativePath(result.Execution.RunDirectory, item).EndsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase));
        if (matched == null)
        {
            return false;
        }

        path = matched;
        displayPath = ToTelegramRelativePath(result.Execution.RunDirectory, matched);
        return true;
    }
}
