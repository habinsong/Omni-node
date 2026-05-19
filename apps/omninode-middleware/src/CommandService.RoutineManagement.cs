using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    private async Task<string?> TryHandleRoutineCommandAsync(string text, string source, CancellationToken cancellationToken)
    {
        if (!text.StartsWith("/routine", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("/routines", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 1 || tokens[1].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            return """
                   [루틴 명령]
                   자연어 예시:
                   - "루틴 목록 보여줘"
                   - "루틴 생성: 매일 아침 8시에 뉴스 요약"
                   - "루틴 실행 rt-20260301093000-ab12cd34"

                   정확히 제어할 때:
                   /routine list
                   /routine create <요청>
                   /routine create browser --model <model> [--url <start-url>] [--tool-profile <playwright_only|desktop_control>] <요청>
                   /routine update <routine-id> <요청>
                   /routine update <routine-id> browser --model <model> [--url <start-url>] [--tool-profile <playwright_only|desktop_control>] <요청>
                   /routine run <routine-id>
                   /routine runs <routine-id>
                   /routine detail <routine-id> <ts>
                   /routine resend <routine-id> <ts>
                   /routine on <routine-id>
                   /routine off <routine-id>
                   /routine delete <routine-id>
                   """;
        }

        var action = tokens[1].ToLowerInvariant();
        if (action == "list")
        {
            var list = ListRoutines();
            if (list.Count == 0)
            {
                return "등록된 루틴이 없습니다.";
            }

            var builder = new StringBuilder();
            builder.AppendLine("[루틴 목록]");
            foreach (var item in list.Take(20))
            {
                var modeLabel = FormatRoutineExecutionModeLabel(item.ResolvedExecutionMode);
                builder.AppendLine($"- {item.Id} | {item.Title} | mode={modeLabel} | {(item.Enabled ? "ON" : "OFF")} | next={item.NextRunLocal}");
            }

            return builder.ToString().Trim();
        }

        if (action == "create")
        {
            if (tokens.Length < 3)
            {
                return "사용법: /routine create <요청>";
            }

            RoutineActionResult created;
            if (tokens[2].Equals("browser", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseRoutineBrowserCommand(tokens.Skip(3), out var browserSpec, out var browserError))
                {
                    return browserError;
                }

                created = await CreateRoutineAsync(
                    request: browserSpec.Request,
                    title: null,
                    executionMode: "browser_agent",
                    agentProvider: browserSpec.Provider,
                    agentModel: browserSpec.Model,
                    agentStartUrl: browserSpec.StartUrl,
                    agentTimeoutSeconds: null,
                    agentToolProfile: browserSpec.ToolProfile,
                    agentUsePlaywright: true,
                    scheduleSourceMode: "auto",
                    maxRetries: null,
                    retryDelaySeconds: null,
                    notifyPolicy: null,
                    notifyTelegram: null,
                    scheduleKind: null,
                    scheduleTime: null,
                    weekdays: null,
                    dayOfMonth: null,
                    timezoneId: null,
                    runImmediately: true,
                    source: source,
                    cancellationToken: cancellationToken
                );
            }
            else
            {
                var request = string.Join(' ', tokens.Skip(2)).Trim();
                created = await CreateRoutineAsync(request, source, cancellationToken);
            }

            return FormatRoutineActionResult(created);
        }

        if (action == "update")
        {
            if (tokens.Length < 4)
            {
                return "사용법: /routine update <routine-id> <요청>";
            }

            var routineId = tokens[2];
            RoutineActionResult updated;
            if (tokens[3].Equals("browser", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseRoutineBrowserCommand(tokens.Skip(4), out var browserSpec, out var browserError))
                {
                    return browserError;
                }

                updated = await UpdateRoutineAsync(
                    routineId: routineId,
                    request: browserSpec.Request,
                    title: null,
                    executionMode: "browser_agent",
                    agentProvider: browserSpec.Provider,
                    agentModel: browserSpec.Model,
                    agentStartUrl: browserSpec.StartUrl,
                    agentTimeoutSeconds: null,
                    agentToolProfile: browserSpec.ToolProfile,
                    agentUsePlaywright: true,
                    scheduleSourceMode: "auto",
                    maxRetries: null,
                    retryDelaySeconds: null,
                    notifyPolicy: null,
                    notifyTelegram: null,
                    scheduleKind: null,
                    scheduleTime: null,
                    weekdays: null,
                    dayOfMonth: null,
                    timezoneId: null,
                    cancellationToken: cancellationToken
                );
            }
            else
            {
                var request = string.Join(' ', tokens.Skip(3)).Trim();
                updated = await UpdateRoutineAsync(
                    routineId: routineId,
                    request: request,
                    title: null,
                    executionMode: null,
                    agentProvider: null,
                    agentModel: null,
                    agentStartUrl: null,
                    agentTimeoutSeconds: null,
                    agentToolProfile: null,
                    agentUsePlaywright: null,
                    scheduleSourceMode: "auto",
                    maxRetries: null,
                    retryDelaySeconds: null,
                    notifyPolicy: null,
                    notifyTelegram: null,
                    scheduleKind: null,
                    scheduleTime: null,
                    weekdays: null,
                    dayOfMonth: null,
                    timezoneId: null,
                    cancellationToken: cancellationToken
                );
            }

            return FormatRoutineActionResult(updated);
        }

        if (action == "run")
        {
            if (tokens.Length < 3)
            {
                return "사용법: /routine run <routine-id>";
            }

            var result = await RunRoutineNowAsync(tokens[2], source, cancellationToken);
            return FormatRoutineActionResult(result);
        }

        if (action == "runs")
        {
            if (tokens.Length < 3)
            {
                return "사용법: /routine runs <routine-id>";
            }

            var summary = ListRoutines().FirstOrDefault(item => item.Id.Equals(tokens[2], StringComparison.OrdinalIgnoreCase));
            if (summary == null)
            {
                return "루틴을 찾을 수 없습니다.";
            }

            var runs = (summary.Runs ?? Array.Empty<RoutineRunSummary>()).Take(12).ToArray();
            if (runs.Length == 0)
            {
                return $"루틴 `{summary.Id}` 의 실행 이력이 아직 없습니다.";
            }

            var builder = new StringBuilder();
            builder.AppendLine($"[루틴 실행 이력] {summary.Id}");
            foreach (var run in runs)
            {
                var telegramText = string.IsNullOrWhiteSpace(run.TelegramStatus) ? "-" : run.TelegramStatus;
                var compactSummary = TrimForOutput(run.Summary ?? string.Empty, 120);
                builder.AppendLine($"- ts={run.Ts} | {run.RunAtLocal} | {run.Status} | {run.Source} | telegram={telegramText}");
                if (!string.IsNullOrWhiteSpace(compactSummary))
                {
                    builder.AppendLine($"  {compactSummary}");
                }
            }

            builder.AppendLine();
            builder.AppendLine("상세: /routine detail <routine-id> <ts>");
            builder.AppendLine("재전송: /routine resend <routine-id> <ts>");
            return builder.ToString().Trim();
        }

        if (action == "detail")
        {
            if (tokens.Length < 4 || !long.TryParse(tokens[3], out var detailTs))
            {
                return "사용법: /routine detail <routine-id> <ts>";
            }

            var detail = GetRoutineRunDetail(tokens[2], detailTs);
            if (!detail.Ok)
            {
                return $"루틴 상세 오류: {detail.Error ?? "실행 이력을 찾지 못했습니다."}";
            }

            var content = TrimForOutput(detail.Content ?? string.Empty, 2600);
            return $"""
                    [루틴 실행 상세]
                    id={detail.RoutineId}
                    title={detail.Title}
                    ts={detail.Ts}
                    status={detail.Status}
                    source={detail.Source}
                    telegram={detail.TelegramStatus ?? "-"}
                    artifact={detail.ArtifactPath ?? "-"}

                    {content}
                    """;
        }

        if (action == "resend")
        {
            if (tokens.Length < 4 || !long.TryParse(tokens[3], out var resendTs))
            {
                return "사용법: /routine resend <routine-id> <ts>";
            }

            var result = await ResendRoutineRunToTelegramAsync(tokens[2], resendTs, cancellationToken);
            return FormatRoutineActionResult(result);
        }

        if (action == "on" || action == "off")
        {
            if (tokens.Length < 3)
            {
                return $"사용법: /routine {action} <routine-id>";
            }

            var enabled = action == "on";
            var result = SetRoutineEnabled(tokens[2], enabled);
            return FormatRoutineActionResult(result);
        }

        if (action == "delete")
        {
            if (tokens.Length < 3)
            {
                return "사용법: /routine delete <routine-id>";
            }

            var result = DeleteRoutine(tokens[2]);
            return FormatRoutineActionResult(result);
        }

        return "알 수 없는 /routine 명령입니다. /routine help를 확인하세요.";
    }

    private async Task<string?> TryHandleNaturalRoutineRequestAsync(string text, string source, CancellationToken cancellationToken)
    {
        if (!LooksLikeRoutineRequest(text))
        {
            return null;
        }

        var result = await CreateRoutineAsync(text, source, cancellationToken);
        return FormatRoutineActionResult(result);
    }

    private static string FormatRoutineActionResult(RoutineActionResult result)
    {
        if (!result.Ok)
        {
            return $"루틴 오류: {result.Message}";
        }

        if (result.Routine == null)
        {
            return result.Message;
        }

        return $"""
                {result.Message}
                id={result.Routine.Id}
                title={result.Routine.Title}
                mode={FormatRoutineExecutionModeLabel(result.Routine.ResolvedExecutionMode)}
                schedule={result.Routine.ScheduleText}
                next={result.Routine.NextRunLocal}
                script={result.Routine.ScriptPath}
                model={result.Routine.CoderModel}
                """;
    }

    private sealed record RoutineBrowserCommandSpec(
        string Request,
        string? Provider,
        string Model,
        string? StartUrl,
        string? ToolProfile
    );

    private static bool TryParseRoutineBrowserCommand(
        IEnumerable<string> tokens,
        out RoutineBrowserCommandSpec spec,
        out string error
    )
    {
        var tokenList = tokens.Select(token => token.Trim()).Where(token => token.Length > 0).ToList();
        string? provider = null;
        string? model = null;
        string? startUrl = null;
        string? toolProfile = null;
        var requestTokens = new List<string>();

        for (var i = 0; i < tokenList.Count; i += 1)
        {
            var token = tokenList[i];
            if (token.Equals("--provider", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= tokenList.Count)
                {
                    spec = new RoutineBrowserCommandSpec(string.Empty, null, string.Empty, null, null);
                    error = "usage: /routine create browser --model <model> [--url <start-url>] [--tool-profile <playwright_only|desktop_control>] <요청>";
                    return false;
                }

                provider = tokenList[++i];
                continue;
            }

            if (token.Equals("--model", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= tokenList.Count)
                {
                    spec = new RoutineBrowserCommandSpec(string.Empty, null, string.Empty, null, null);
                    error = "usage: /routine create browser --model <model> [--url <start-url>] [--tool-profile <playwright_only|desktop_control>] <요청>";
                    return false;
                }

                model = tokenList[++i];
                continue;
            }

            if (token.Equals("--url", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= tokenList.Count)
                {
                    spec = new RoutineBrowserCommandSpec(string.Empty, null, string.Empty, null, null);
                    error = "usage: /routine create browser --model <model> [--url <start-url>] [--tool-profile <playwright_only|desktop_control>] <요청>";
                    return false;
                }

                startUrl = tokenList[++i];
                continue;
            }

            if (token.Equals("--tool-profile", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= tokenList.Count)
                {
                    spec = new RoutineBrowserCommandSpec(string.Empty, null, string.Empty, null, null);
                    error = "usage: /routine create browser --model <model> [--url <start-url>] [--tool-profile <playwright_only|desktop_control>] <요청>";
                    return false;
                }

                toolProfile = tokenList[++i];
                continue;
            }

            requestTokens.Add(token);
        }

        var request = string.Join(' ', requestTokens).Trim();
        if (string.IsNullOrWhiteSpace(model))
        {
            spec = new RoutineBrowserCommandSpec(string.Empty, null, string.Empty, null, null);
            error = "브라우저 루틴은 --model <model> 이 필요합니다.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request))
        {
            spec = new RoutineBrowserCommandSpec(string.Empty, null, string.Empty, null, null);
            error = "브라우저 루틴 요청을 입력하세요.";
            return false;
        }

        spec = new RoutineBrowserCommandSpec(request, provider, model.Trim(), startUrl, toolProfile);
        error = string.Empty;
        return true;
    }

    private static string FormatRoutineExecutionModeLabel(string? executionMode)
    {
        return NormalizeRoutineExecutionMode(executionMode) switch
        {
            "browser_agent" => "browser_agent",
            LogicGraphExecutionMode => LogicGraphExecutionMode,
            "url" => "url",
            "web" => "web",
            _ => "script"
        };
    }

    private static bool LooksLikeRoutineRequest(string text)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var hasRepeat = ContainsAny(normalized, "매일", "매주", "반복", "루틴", "정기", "매달", "every day", "schedule");
        var hasIntent = ContainsAny(normalized, "해줘", "만들어", "자동화", "추가", "등록", "생성", "set up", "create");
        return hasRepeat && hasIntent;
    }

    private async Task RoutineSchedulerLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var dueIds = _routineRegistry.ReadAll(routines =>
            {
                var now = DateTimeOffset.UtcNow;
                return routines
                    .Where(x => x.Enabled && !x.Running && x.NextRunUtc <= now)
                    .Select(x => x.Id)
                    .ToList();
            });

            foreach (var id in dueIds)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _routineSchedulerDispatchGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                        var result = await RunRoutineNowAsync(id, "scheduler", CancellationToken.None);
                        if (!result.Ok)
                        {
                            _routineSchedulerLastError = $"scheduler run skipped ({id}): {result.Message}";
                            Console.Error.WriteLine($"[routine] {_routineSchedulerLastError}");
                        }
                        else
                        {
                            _routineSchedulerLastError = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        _routineSchedulerLastError = $"scheduler run failed ({id}): {ex.Message}";
                        Console.Error.WriteLine($"[routine] {_routineSchedulerLastError}");
                    }
                    finally
                    {
                        try
                        {
                            _routineSchedulerDispatchGate.Release();
                        }
                        catch
                        {
                        }
                    }
                }, CancellationToken.None);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void EnsureRoutinePromptFiles()
    {
        try
        {
            Directory.CreateDirectory(_routinePromptDir);
            var systemPromptPath = Path.Combine(_routinePromptDir, "system_prompt.md");
            var baseConfigPath = Path.Combine(_routinePromptDir, "기본 구성.md");

            if (!File.Exists(systemPromptPath))
            {
                File.WriteAllText(
                    systemPromptPath,
                    """
                    # Routine System Prompt
                    - 목적: 반복 작업 자동화를 위한 루틴을 계획하고 실행 가능한 코드로 생성한다.
                    - 출력: 반드시 PLAN 섹션 + LANGUAGE 선언 + 단일 코드블록.
                    - 가장 중요: 스케줄은 루틴 엔진이 이미 처리한다. 코드 안에서 현재 시각/요일/날짜를 다시 확인하거나 대기하지 마라.
                    - 금지: cron 등록, while true, sleep 기반 무한 대기, CURRENT_HOUR/CURRENT_MINUTE/DAY_OF_WEEK 계산, datetime.now()로 실행 여부 판단.
                    - 실행 방식: 코드가 호출되면 즉시 작업을 수행하고 결과를 stdout에 완결된 한국어 텍스트로 남긴다.
                    - 결과 품질: 표준 출력은 비워두지 말고, 핵심 결과/요약/실패 원인을 사람이 읽을 수 있게 출력한다.
                    - 제약: macOS/Linux 모두 동작 가능한 방식 우선, 외부 의존 최소화.
                    - 보안: 파괴적 명령 금지, 사용자 경로 외 쓰기 금지, 민감정보 출력 금지.
                    """,
                    new UTF8Encoding(false)
                );
            }

            if (!File.Exists(baseConfigPath))
            {
                File.WriteAllText(
                    baseConfigPath,
                    """
                    # 기본 구성
                    1. 스케줄은 엔진이 담당하므로 코드에서 시간/요일 조건문을 두지 않는다.
                    2. 실행 환경은 bash 또는 python 중 하나만 사용하고, 한 번 실행될 때 즉시 끝나야 한다.
                    3. 출력은 항상 stdout에 남긴다. 결과가 없더라도 실제 수행 결과나 실패 원인을 설명한다.
                    4. 네트워크/외부 사이트 접근이 실패하면 stderr 또는 stdout에 원인과 대체 안내를 짧게 남긴다.
                    5. 사용자가 적은 요청 원문에 스케줄 표현이 섞여 있어도, 구현은 순수 작업 내용만 수행한다.
                    """,
                    new UTF8Encoding(false)
                );
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[routine] prompt init failed: {ex.Message}");
        }
    }

    private async Task<RoutineGenerationResult> GenerateRoutineImplementationAsync(
        string request,
        RoutineSchedule schedule,
        CancellationToken cancellationToken,
        Action<RoutineProgressUpdate>? progressCallback = null
    )
    {
        var systemPromptPath = Path.Combine(_routinePromptDir, "system_prompt.md");
        var baseConfigPath = Path.Combine(_routinePromptDir, "기본 구성.md");
        var systemPrompt = File.Exists(systemPromptPath) ? File.ReadAllText(systemPromptPath, Encoding.UTF8) : string.Empty;
        var baseConfig = File.Exists(baseConfigPath) ? File.ReadAllText(baseConfigPath, Encoding.UTF8) : string.Empty;
        var objective = BuildRoutineGenerationPrompt(request, schedule.Display, systemPrompt, baseConfig);
        ReportRoutineCreateProgress(
            progressCallback,
            "실행 코드를 만들 생성 전략을 고르는 중입니다.",
            34,
            "planning",
            "생성 전략 준비",
            "모델 가용성과 예산을 기준으로 최적 경로를 선택합니다.",
            2
        );
        var strategy = await SelectRoutineCodingStrategyAsync(objective, cancellationToken);
        ReportRoutineCreateProgress(
            progressCallback,
            "실행 구성을 생성하는 중입니다.",
            52,
            "implementation",
            "실행 구성 생성",
            $"선택 전략: {strategy.Mode} / 모델: {string.Join(", ", strategy.Models)}",
            3
        );

        if (strategy.Mode == "split")
        {
            var chunks = new List<string>();
            var partLabels = new[] { "파트 1/3", "파트 2/3", "파트 3/3" };
            for (var i = 0; i < strategy.Models.Count; i++)
            {
                var model = strategy.Models[i];
                var prompt = objective + $"\n\n[{partLabels[Math.Min(i, partLabels.Length - 1)]}] 관점으로 설계/코드 초안을 작성하세요.";
                var generated = await GenerateByProviderSafeAsync("groq", model, prompt, cancellationToken, Math.Min(_context.CodingMaxOutputTokens, 2800));
                chunks.Add($"[{model}]\n{generated.Text}");
            }

            var merged = string.Join("\n\n", chunks);
            var parsed = ParseCodeCandidate(merged, "bash");
            var language = parsed.Language is "bash" or "python" ? parsed.Language : "bash";
            var code = string.IsNullOrWhiteSpace(parsed.Code)
                ? string.Empty
                : EnsureRoutineShebang(parsed.Code, language);
            if (!string.IsNullOrWhiteSpace(parsed.Code) && RoutineCodeNeedsRepair(language, code))
            {
                ReportRoutineCreateProgress(
                    progressCallback,
                    "생성 결과를 보정하는 중입니다.",
                    68,
                    "implementation",
                    "실행 구성 생성",
                    "초안을 점검한 뒤 실행 가능하도록 보정합니다.",
                    3
                );
                var repaired = await TryRepairRoutineCodeAsync(objective, merged, strategy.Models[0], request, schedule, cancellationToken);
                language = repaired.Language;
                code = repaired.Code;
                merged = repaired.RawText;
            }

            var quality = ValidateRoutineGeneratedCode(language, code, request);
            return new RoutineGenerationResult(
                PlannerProvider: "groq",
                PlannerModel: "split",
                CoderModel: string.Join(",", strategy.Models),
                Plan: ExtractPlanText(merged),
                Language: language,
                Code: code,
                QualityStatus: quality.Ok ? "ok" : "quality_failed",
                QualityWarnings: quality.Warnings
            );
        }

        var single = await GenerateByProviderSafeAsync("groq", strategy.Models[0], objective, cancellationToken, Math.Min(_context.CodingMaxOutputTokens, 4200));
        var singleParsed = ParseCodeCandidate(single.Text, "bash");
        var singleLanguage = singleParsed.Language is "bash" or "python" ? singleParsed.Language : "bash";
        var singleCode = string.IsNullOrWhiteSpace(singleParsed.Code)
            ? string.Empty
            : EnsureRoutineShebang(singleParsed.Code, singleLanguage);
        if (!string.IsNullOrWhiteSpace(singleParsed.Code) && RoutineCodeNeedsRepair(singleLanguage, singleCode))
        {
            ReportRoutineCreateProgress(
                progressCallback,
                "생성 결과를 보정하는 중입니다.",
                68,
                "implementation",
                "실행 구성 생성",
                "초안을 점검한 뒤 실행 가능하도록 보정합니다.",
                3
            );
            var repaired = await TryRepairRoutineCodeAsync(objective, single.Text, strategy.Models[0], request, schedule, cancellationToken);
            singleLanguage = repaired.Language;
            singleCode = repaired.Code;
            single = single with { Text = repaired.RawText };
        }
        var singleQuality = ValidateRoutineGeneratedCode(singleLanguage, singleCode, request);
        return new RoutineGenerationResult(
            PlannerProvider: "groq",
            PlannerModel: strategy.Models[0],
            CoderModel: strategy.Models[0],
            Plan: ExtractPlanText(single.Text),
            Language: singleLanguage,
            Code: singleCode,
            QualityStatus: singleQuality.Ok ? "ok" : "quality_failed",
            QualityWarnings: singleQuality.Warnings
        );
    }

    private async Task<RoutineModelStrategy> SelectRoutineCodingStrategyAsync(string objective, CancellationToken cancellationToken)
    {
        static bool Has(IReadOnlySet<string> set, string modelId) => set.Contains(modelId);

        var availableModels = await _groqModelCatalog.GetModelsAsync(cancellationToken);
        var modelSet = availableModels.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var estimatedTokens = EstimatePromptTokens(objective);

        var maverickReady = Has(modelSet, RoutineModelMaverick) && !IsGroqRateLimitImminent(RoutineModelMaverick, 2200);
        var gptOssReady = Has(modelSet, RoutineModelGptOss) && !IsGroqRateLimitImminent(RoutineModelGptOss, 2200);
        var kimiReady = Has(modelSet, RoutineModelKimi) && !IsGroqRateLimitImminent(RoutineModelKimi, 2200);

        if (estimatedTokens <= 6000 && maverickReady)
        {
            return new RoutineModelStrategy("single", new[] { RoutineModelMaverick }, $"estimated_tpm={estimatedTokens}");
        }

        if (gptOssReady)
        {
            return new RoutineModelStrategy("single", new[] { RoutineModelGptOss }, $"fallback_from_maverick estimated_tpm={estimatedTokens}");
        }

        if (kimiReady)
        {
            return new RoutineModelStrategy("single", new[] { RoutineModelKimi }, "fallback_from_gptoss");
        }

        var split = new List<string>();
        if (Has(modelSet, RoutineModelMaverick))
        {
            split.Add(RoutineModelMaverick);
        }

        if (Has(modelSet, RoutineModelGptOss))
        {
            split.Add(RoutineModelGptOss);
        }

        if (Has(modelSet, RoutineModelKimi))
        {
            split.Add(RoutineModelKimi);
        }

        if (split.Count == 0)
        {
            split.Add(_llmRouter.GetSelectedGroqModel());
        }

        while (split.Count < 3)
        {
            split.Add(split[^1]);
        }

        return new RoutineModelStrategy("split", split.Take(3).ToArray(), "all_models_budget_limited");
    }

    private static string BuildRoutineGenerationPrompt(string request, string schedule, string systemPrompt, string baseConfig)
    {
        return $"""
                {systemPrompt}

                {baseConfig}

                [사용자 루틴 요청]
                {request}

                [정규화 스케줄]
                {schedule}

                요구사항:
                1) 이 코드는 스케줄러가 이미 실행할 시점에 호출한다. 실행 여부를 코드 안에서 다시 판단하지 마라.
                2) 현재 시간/요일/날짜 확인, sleep, while true, cron/crontab 등록, 백그라운드 daemon화 금지
                3) 요청 원문에 스케줄 표현이 있어도 정규화 스케줄을 우선으로 보고, 구현은 작업 자체만 수행
                4) 한 번 실행되면 즉시 작업을 수행하고 종료
                5) macOS/Linux 모두 동작 가능한 루틴 코드
                6) 외부 의존 최소화
                7) 실행 결과를 stdout 텍스트로 요약 출력
                8) stdout은 비워두지 말 것. 사람이 읽는 최종 결과를 3문장 이상 또는 구조화된 목록으로 출력
                9) 민감정보 노출 금지
                10) Linux 전용 옵션(top -bn1, free -m 등)을 그대로 쓰지 말고 macOS/Linux 공통 또는 분기 가능한 방식으로 작성

                금지 예시:
                - CURRENT_HOUR=$(date +%H)
                - DAY_OF_WEEK=$(date +%u)
                - if now.hour == 8:
                - schedule.every(...)
                - while true:
                - sleep 60
                - top -bn1

                출력 형식:
                PLAN:
                - 단계1
                - 단계2

                LANGUAGE=<bash 또는 python>
                ```bash
                # 실행 가능한 전체 코드
                ```
                """;
    }

    private static int EstimatePromptTokens(string text)
    {
        var length = (text ?? string.Empty).Length;
        return Math.Max(1, length / 3);
    }

    private static string ExtractPlanText(string raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return "계획 텍스트 없음";
        }

        var fenceIndex = text.IndexOf("```", StringComparison.Ordinal);
        if (fenceIndex <= 0)
        {
            return text.Length <= 1500 ? text : text[..1500] + "...";
        }

        var plan = text[..fenceIndex].Trim();
        return string.IsNullOrWhiteSpace(plan) ? "계획 텍스트 없음" : (plan.Length <= 1500 ? plan : plan[..1500] + "...");
    }

    private static string EnsureRoutineShebang(string code, string language)
    {
        var normalized = (code ?? string.Empty).Trim().TrimStart('\uFEFF');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        if (language == "bash")
        {
            var body = normalized;
            if (body.StartsWith("#!/usr/bin/env bash", StringComparison.Ordinal))
            {
                body = body["#!/usr/bin/env bash".Length..].TrimStart();
            }
            else if (body.StartsWith("#!/bin/bash", StringComparison.Ordinal))
            {
                body = body["#!/bin/bash".Length..].TrimStart();
            }

            if (body.StartsWith("set -euo pipefail", StringComparison.Ordinal))
            {
                body = body["set -euo pipefail".Length..].TrimStart();
            }

            return """
                   #!/usr/bin/env bash
                   set -euo pipefail

                   # Omni-node portability shim (macOS/Linux)
                   if ! command -v free >/dev/null 2>&1; then
                     free() {
                       echo "              total        used        free"
                       echo "Mem:           n/a         n/a         n/a"
                       if command -v vm_stat >/dev/null 2>&1; then
                         echo ""
                         vm_stat | head -n 6
                       fi
                       return 0
                     }
                   fi

                   if [ "$(uname -s)" = "Darwin" ]; then
                     top() {
                       local remapped=()
                       local replaced=0
                       for arg in "$@"; do
                         if [ "$arg" = "-bn1" ]; then
                           remapped+=("-l" "1")
                           replaced=1
                         else
                           remapped+=("$arg")
                         fi
                       done
                       if [ "$replaced" -eq 1 ]; then
                         command top "${remapped[@]}"
                         return $?
                       fi
                       command top "$@"
                     }
                   fi

                   """ + body;
        }

        if (language == "python" && !normalized.StartsWith("#!/usr/bin/env python3", StringComparison.Ordinal))
        {
            return "#!/usr/bin/env python3\n" + normalized;
        }

        return normalized;
    }

    private static string BuildFallbackRoutineCode(string request, RoutineSchedule schedule)
    {
        var escaped = EscapeForSingleQuotes(request);
        return $"""
                #!/usr/bin/env bash
                set -euo pipefail

                echo "[Routine] 요청: '{escaped}'"
                echo "[Routine] 스케줄: {schedule.Display}"
                echo "[Routine] 실행시각: $(date '+%Y-%m-%d %H:%M:%S')"
                echo "[Routine] 자동 생성 코드가 유효하지 않아 기본 템플릿으로 실행했습니다."
                echo "[Routine] 실제 작업 로직은 루틴 수정 저장으로 재생성하세요."
                """;
    }

    private static string EscapeForSingleQuotes(string text)
    {
        return (text ?? string.Empty).Replace("'", "'\"'\"'", StringComparison.Ordinal);
    }

    private static string BuildRoutineExecutionText(RoutineDefinition routine, CodeExecutionResult exec)
    {
        var stdout = (exec.StdOut ?? string.Empty).Trim();
        var stderr = (exec.StdErr ?? string.Empty).Trim();
        var summary = new StringBuilder();
        summary.AppendLine($"[Routine:{routine.Id}] {routine.Title}");
        summary.AppendLine($"status={exec.Status} exit={exec.ExitCode}");
        summary.AppendLine($"model={routine.CoderModel}");
        summary.AppendLine($"script={routine.ScriptPath}");
        summary.AppendLine($"run_dir={exec.RunDirectory}");
        if (!string.IsNullOrWhiteSpace(stdout))
        {
            summary.AppendLine();
            summary.AppendLine("[stdout]");
            summary.AppendLine(stdout.Length <= 1600 ? stdout : stdout[..1600] + "...");
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            summary.AppendLine();
            summary.AppendLine("[stderr]");
            summary.AppendLine(stderr.Length <= 1200 ? stderr : stderr[..1200] + "...");
        }
        else if (string.IsNullOrWhiteSpace(stdout))
        {
            summary.AppendLine();
            summary.AppendLine("[stdout]");
            summary.AppendLine("(출력 없음)");
        }

        return summary.ToString().Trim();
    }

    private static string ResolveCronRunEntryStatus(CodeExecutionResult exec)
    {
        var normalized = NormalizeCronRunStatus(exec.Status);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        return exec.ExitCode == 0 ? "ok" : "error";
    }

    private static string? BuildCronRunEntryError(CodeExecutionResult exec, string output, string status)
    {
        if (!string.Equals(status, "error", StringComparison.Ordinal))
        {
            return null;
        }

        var stderr = (exec.StdErr ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            return TrimForCronError(stderr);
        }

        return TrimForCronError(output);
    }

    private static string? BuildCronRunEntrySummary(string output)
    {
        var normalized = (output ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        const int maxChars = 800;
        if (normalized.Length <= maxChars)
        {
            return normalized;
        }

        return normalized[..maxChars] + "...";
    }

    private static bool ShouldRunCronAgentTurnBridge(RoutineDefinition routine)
    {
        if (!string.Equals(
                NormalizeCronSessionTargetOrDefault(routine.CronSessionTarget),
                "isolated",
                StringComparison.Ordinal
            ))
        {
            return false;
        }

        return string.Equals(
            NormalizeCronPayloadKindOrDefault(routine.CronPayloadKind),
            "agentTurn",
            StringComparison.Ordinal
        );
    }

    private CodeExecutionResult ExecuteCronAgentTurnBridge(
        RoutineDefinition routine,
        CancellationToken cancellationToken
    )
    {
        var command = "sessions_spawn runtime=acp mode=run";
        if (cancellationToken.IsCancellationRequested)
        {
            var canceledStdOut = $"""
                                  [cron.agentTurn.bridge]
                                  routineId={routine.Id}
                                  status=error
                                  reason=canceled
                                  """;
            return new CodeExecutionResult(
                "cron-agentturn",
                ResolveWorkspaceRoot(),
                "-",
                command,
                1,
                canceledStdOut,
                "cancellation requested",
                "error"
            );
        }

        var payloadText = ResolveRoutineExecutionRequestText(routine.Request, routine.Title, routine.ScheduleSourceMode);
        var payloadModel = NormalizeOptionalCronPayloadString(routine.CronPayloadModel);
        var payloadThinking = NormalizeOptionalCronPayloadString(routine.CronPayloadThinking);
        var payloadTimeoutSeconds = routine.CronPayloadTimeoutSeconds;
        var payloadLightContext = routine.CronPayloadLightContext;

        var spawnTask = BuildCronAgentTurnSpawnTask(
            payloadText,
            payloadModel,
            payloadThinking,
            payloadTimeoutSeconds,
            payloadLightContext
        );
        var spawnResult = _sessionSpawnTool.Spawn(
            task: spawnTask,
            label: $"cron-{routine.Title}",
            runtime: "acp",
            runTimeoutSeconds: payloadTimeoutSeconds,
            timeoutSeconds: payloadTimeoutSeconds,
            thread: false,
            mode: "run",
            acpModel: payloadModel,
            acpThinking: payloadThinking,
            acpLightContext: payloadLightContext
        );

        if (string.Equals(spawnResult.Status, "accepted", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(spawnResult.ChildSessionKey))
        {
            var optionNote = BuildCronAgentTurnOptionsBlock(
                payloadModel,
                payloadThinking,
                payloadTimeoutSeconds,
                payloadLightContext
            );
            if (!string.IsNullOrWhiteSpace(optionNote))
            {
                _ = _conversationStore.AppendMessage(
                    spawnResult.ChildSessionKey,
                    "system",
                    optionNote,
                    "cron_agentturn_options"
                );
            }
        }

        var accepted = string.Equals(spawnResult.Status, "accepted", StringComparison.OrdinalIgnoreCase);
        var resolvedCommand = $"{command} timeoutSeconds={(spawnResult.RunTimeoutSeconds).ToString(CultureInfo.InvariantCulture)}";
        var stdout = BuildCronAgentTurnBridgeStdOut(
            routine,
            spawnResult,
            payloadModel,
            payloadThinking,
            payloadTimeoutSeconds,
            payloadLightContext
        );
        var stderr = accepted
            ? string.Empty
            : string.IsNullOrWhiteSpace(spawnResult.Error)
                ? "sessions_spawn returned error"
                : spawnResult.Error!;

        return new CodeExecutionResult(
            "cron-agentturn",
            ResolveWorkspaceRoot(),
            "-",
            resolvedCommand,
            accepted ? 0 : 1,
            stdout,
            stderr,
            accepted ? "ok" : "error"
        );
    }

    private static string BuildCronAgentTurnSpawnTask(
        string message,
        string? model,
        string? thinking,
        int? timeoutSeconds,
        bool? lightContext
    )
    {
        var optionBlock = BuildCronAgentTurnOptionsBlock(model, thinking, timeoutSeconds, lightContext);
        if (string.IsNullOrWhiteSpace(optionBlock))
        {
            return message;
        }

        return $"{optionBlock}\n\n{message}";
    }

    private static string BuildCronAgentTurnBridgeStdOut(
        RoutineDefinition routine,
        SessionSpawnToolResult spawnResult,
        string? model,
        string? thinking,
        int? timeoutSeconds,
        bool? lightContext
    )
    {
        var builder = new StringBuilder();
        builder.AppendLine("[cron.agentTurn.bridge]");
        builder.AppendLine($"routineId={routine.Id}");
        builder.AppendLine($"spawnStatus={spawnResult.Status}");
        builder.AppendLine($"runtime={spawnResult.Runtime}");
        builder.AppendLine($"mode={spawnResult.Mode}");
        builder.AppendLine($"runId={spawnResult.RunId}");
        if (!string.IsNullOrWhiteSpace(spawnResult.ChildSessionKey))
        {
            builder.AppendLine($"childSessionKey={spawnResult.ChildSessionKey}");
        }
        if (!string.IsNullOrWhiteSpace(spawnResult.BackendSessionId))
        {
            builder.AppendLine($"backendSessionId={spawnResult.BackendSessionId}");
        }
        if (!string.IsNullOrWhiteSpace(spawnResult.ThreadBindingKey))
        {
            builder.AppendLine($"threadBindingKey={spawnResult.ThreadBindingKey}");
        }

        var optionBlock = BuildCronAgentTurnOptionsBlock(model, thinking, timeoutSeconds, lightContext);
        if (!string.IsNullOrWhiteSpace(optionBlock))
        {
            builder.AppendLine();
            builder.AppendLine(optionBlock);
        }

        return builder.ToString().Trim();
    }

    private static string? BuildCronAgentTurnOptionsBlock(
        string? model,
        string? thinking,
        int? timeoutSeconds,
        bool? lightContext
    )
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(model))
        {
            lines.Add($"- model: {model}");
        }

        if (!string.IsNullOrWhiteSpace(thinking))
        {
            lines.Add($"- thinking: {thinking}");
        }

        if (timeoutSeconds.HasValue)
        {
            lines.Add($"- timeoutSeconds: {timeoutSeconds.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        if (lightContext.HasValue)
        {
            lines.Add($"- lightContext: {(lightContext.Value ? "true" : "false")}");
        }

        if (lines.Count == 0)
        {
            return null;
        }

        return "[cron.agentTurn.options]\n" + string.Join("\n", lines);
    }

    private static void AppendRoutineRunLogEntry(RoutineDefinition routine, RoutineRunLogEntry entry)
    {
        routine.CronRunLog ??= new List<RoutineRunLogEntry>();
        routine.CronRunLog.Add(entry);
        const int maxEntries = 200;
        if (routine.CronRunLog.Count <= maxEntries)
        {
            return;
        }

        var removeCount = routine.CronRunLog.Count - maxEntries;
        routine.CronRunLog.RemoveRange(0, removeCount);
    }

    private static RoutineSummary ToRoutineSummary(RoutineDefinition routine)
    {
        var localNext = routine.NextRunUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        var localLast = routine.LastRunUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
        var scheduleSourceMode = NormalizeRoutineScheduleSourceMode(routine.ScheduleSourceMode, routine.Request);
        var executionRequest = ResolveRoutineExecutionRequestText(routine.Request, routine.Title, routine.ScheduleSourceMode);
        var explicitExecutionMode = NormalizeRoutineExecutionMode(routine.ExecutionMode);
        var resolvedExecutionMode = ResolveRoutineExecutionMode(executionRequest, explicitExecutionMode);
        var scheduleKind = "daily";
        var timeOfDay = $"{routine.Hour:D2}:{routine.Minute:D2}";
        int? dayOfMonth = null;
        var weekdays = Array.Empty<int>();
        if (TryParseSupportedRoutineCronExpression(
                routine.CronScheduleExpr,
                out var parsedKind,
                out var parsedHour,
                out var parsedMinute,
                out var parsedDayOfMonth,
                out var parsedWeekdays,
                out _,
                out _
            ))
        {
            scheduleKind = parsedKind;
            timeOfDay = $"{parsedHour:D2}:{parsedMinute:D2}";
            dayOfMonth = parsedDayOfMonth;
            weekdays = parsedWeekdays;
        }

        return new RoutineSummary(
            routine.Id,
            routine.Title,
            routine.Request,
            explicitExecutionMode,
            resolvedExecutionMode,
            resolvedExecutionMode == "browser_agent" ? NormalizeRoutineAgentProvider(routine.AgentProvider, routine.AgentModel) : null,
            resolvedExecutionMode == "browser_agent" ? NormalizeRoutineAgentModel(routine.AgentModel) : null,
            resolvedExecutionMode == "browser_agent" ? NormalizeOptionalAgentMetaValue(routine.AgentStartUrl) : null,
            resolvedExecutionMode == "browser_agent" ? NormalizeRoutineAgentTimeoutSeconds(routine.AgentTimeoutSeconds) : null,
            resolvedExecutionMode == "browser_agent" ? NormalizeRoutineAgentToolProfile(routine.AgentToolProfile, routine.AgentUsePlaywright) : null,
            resolvedExecutionMode == "browser_agent" && NormalizeRoutineAgentUsePlaywright(routine.AgentUsePlaywright),
            routine.ScheduleText,
            scheduleSourceMode,
            RoutineSchedulePolicy.NormalizeRetryCount(routine.MaxRetries),
            RoutineSchedulePolicy.NormalizeRetryDelaySeconds(routine.RetryDelaySeconds),
            RoutineSchedulePolicy.NormalizeNotifyPolicy(routine.NotifyPolicy),
            routine.NotifyTelegram,
            routine.Enabled,
            localNext,
            localLast,
            routine.LastStatus,
            routine.LastOutput,
            routine.ScriptPath,
            routine.Language,
            routine.CoderModel,
            scheduleKind,
            routine.CronScheduleExpr,
            routine.TimezoneId,
            timeOfDay,
            dayOfMonth,
            weekdays,
            string.IsNullOrWhiteSpace(routine.QualityStatus) ? "unknown" : routine.QualityStatus,
            routine.QualityWarnings ?? new List<string>(),
            BuildRoutineRunCommand(routine),
            BuildRoutineRunSummaries(routine)
        );
    }

    private static string BuildRoutineRunCommand(RoutineDefinition routine)
    {
        var resolvedMode = ResolveRoutineExecutionMode(
            ResolveRoutineExecutionRequestText(routine.Request, routine.Title, routine.ScheduleSourceMode),
            routine.ExecutionMode
        );
        if (resolvedMode == "browser_agent")
        {
            return "브라우저 에이전트 테스트 버튼으로 실행";
        }

        if (resolvedMode == "web" || resolvedMode == "url")
        {
            return "웹 테스트 버튼으로 실행";
        }

        if (ShouldRunCronAgentTurnBridge(routine))
        {
            return "cron agentTurn bridge";
        }

        if (string.IsNullOrWhiteSpace(routine.ScriptPath))
        {
            return "실행 파일 없음";
        }

        var quoted = routine.ScriptPath.Contains(' ')
            ? $"\"{routine.ScriptPath}\""
            : routine.ScriptPath;
        return NormalizeRoutineScriptLanguage(routine.Language) == "python"
            ? $"python3 {quoted}"
            : $"bash {quoted}";
    }

    private static bool TryResolveRoutineScheduleConfig(
        string request,
        string scheduleSourceMode,
        string? scheduleKind,
        string? scheduleTime,
        IReadOnlyList<int>? weekdays,
        int? dayOfMonth,
        string? timezoneId,
        out RoutineScheduleConfig config,
        out string error
    )
    {
        if (string.Equals(scheduleSourceMode, "manual", StringComparison.Ordinal))
        {
            return RoutineSchedulePolicy.TryBuildConfig(
                scheduleKind,
                scheduleTime,
                weekdays,
                dayOfMonth,
                timezoneId,
                out config,
                out error
            );
        }

        config = ResolveRoutineScheduleConfigFromRequest(request, timezoneId);
        error = string.Empty;
        return true;
    }

    private static RoutineScheduleConfig ResolveRoutineScheduleConfigFromRequest(string request, string? timezoneId)
    {
        if (TryParseRoutineScheduleConfigFromRequest(request, timezoneId, out var parsed))
        {
            return parsed;
        }

        var fallback = RoutineSchedulePolicy.ParseDailySchedule(request);
        return RoutineSchedulePolicy.BuildDailyConfig(fallback.Hour, fallback.Minute, timezoneId ?? TimeZoneInfo.Local.Id);
    }

    private static bool TryParseRoutineScheduleConfigFromRequest(
        string? request,
        string? timezoneId,
        out RoutineScheduleConfig config
    )
    {
        config = RoutineSchedulePolicy.BuildDailyConfig(8, 0, timezoneId ?? TimeZoneInfo.Local.Id);
        var text = (request ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return false;
        }

        var resolvedTimezoneId = RoutineSchedulePolicy.ResolveTimeZone(timezoneId).Id;
        var hour = 8;
        var minute = 0;
        var hasExplicitTime = RoutineSchedulePolicy.TryExtractNaturalTime(text, out hour, out minute);

        if (RoutineSchedulePolicy.TryExtractMonthlyDay(text, out var dayOfMonth))
        {
            config = new RoutineScheduleConfig(
                "monthly",
                hour,
                minute,
                RoutineSchedulePolicy.BuildScheduleDisplay("monthly", hour, minute, resolvedTimezoneId, dayOfMonth, Array.Empty<int>()),
                resolvedTimezoneId,
                $"{minute} {hour} {dayOfMonth} * *",
                dayOfMonth,
                Array.Empty<int>()
            );
            return true;
        }

        if (RoutineSchedulePolicy.TryExtractWeekdays(text, out var weekdays))
        {
            config = new RoutineScheduleConfig(
                "weekly",
                hour,
                minute,
                RoutineSchedulePolicy.BuildScheduleDisplay("weekly", hour, minute, resolvedTimezoneId, null, weekdays),
                resolvedTimezoneId,
                $"{minute} {hour} * * {string.Join(",", weekdays.Select(static x => x.ToString(CultureInfo.InvariantCulture)))}",
                null,
                weekdays
            );
            return true;
        }

        if (ContainsRoutineScheduleExpression(text) || hasExplicitTime)
        {
            config = RoutineSchedulePolicy.BuildDailyConfig(hour, minute, resolvedTimezoneId);
            return true;
        }

        return false;
    }

    private static bool RoutineMatchesSchedule(RoutineDefinition routine, RoutineScheduleConfig config)
    {
        return string.Equals(routine.ScheduleText, config.Display, StringComparison.Ordinal)
            && string.Equals(routine.TimezoneId, config.TimezoneId, StringComparison.Ordinal)
            && routine.Hour == config.Hour
            && routine.Minute == config.Minute
            && string.Equals(routine.CronScheduleExpr ?? string.Empty, config.CronExpr, StringComparison.Ordinal)
            && string.Equals(NormalizeCronScheduleKind(routine.CronScheduleKind), "cron", StringComparison.Ordinal);
    }

    private static bool TryParseSupportedRoutineCronExpression(
        string? expr,
        out string kind,
        out int hour,
        out int minute,
        out int? dayOfMonth,
        out int[] weekdays,
        out string normalizedExpr,
        out string error
    )
    {
        kind = "daily";
        hour = 0;
        minute = 0;
        dayOfMonth = null;
        weekdays = Array.Empty<int>();
        normalizedExpr = "0 8 * * *";
        error = "지원되지 않는 cron 식입니다.";

        var tokens = (expr ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length != 5)
        {
            error = "cron 식은 5개 필드(m h dom mon dow)여야 합니다.";
            return false;
        }

        if (!int.TryParse(tokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out minute)
            || minute < 0 || minute > 59)
        {
            error = "cron 분은 0-59여야 합니다.";
            return false;
        }

        if (!int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out hour)
            || hour < 0 || hour > 23)
        {
            error = "cron 시는 0-23이어야 합니다.";
            return false;
        }

        if (!string.Equals(tokens[3], "*", StringComparison.Ordinal))
        {
            error = "cron month 필드는 현재 '*'만 지원합니다.";
            return false;
        }

        if (string.Equals(tokens[2], "*", StringComparison.Ordinal)
            && string.Equals(tokens[4], "*", StringComparison.Ordinal))
        {
            kind = "daily";
            normalizedExpr = $"{minute} {hour} * * *";
            return true;
        }

        if (string.Equals(tokens[2], "*", StringComparison.Ordinal))
        {
            var parsedWeekdays = new List<int>();
            foreach (var rawPart in tokens[4].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!int.TryParse(rawPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    error = "주간 cron 요일은 0-6 또는 7(일요일)만 지원합니다.";
                    return false;
                }

                if (parsed == 7)
                {
                    parsed = 0;
                }

                if (parsed < 0 || parsed > 6)
                {
                    error = "주간 cron 요일은 0-6 또는 7(일요일)만 지원합니다.";
                    return false;
                }

                parsedWeekdays.Add(parsed);
            }

            weekdays = RoutineSchedulePolicy.NormalizeWeekdays(parsedWeekdays);
            if (weekdays.Length == 0)
            {
                error = "주간 cron 요일은 하나 이상 필요합니다.";
                return false;
            }

            kind = "weekly";
            normalizedExpr = $"{minute} {hour} * * {string.Join(",", weekdays.Select(static x => x.ToString(CultureInfo.InvariantCulture)))}";
            return true;
        }

        if (string.Equals(tokens[4], "*", StringComparison.Ordinal)
            && int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDayOfMonth)
            && parsedDayOfMonth >= 1
            && parsedDayOfMonth <= 31)
        {
            kind = "monthly";
            dayOfMonth = parsedDayOfMonth;
            normalizedExpr = $"{minute} {hour} {parsedDayOfMonth} * *";
            return true;
        }

        error = "지원되는 cron 형식은 daily/weekly/monthly 뿐입니다.";
        return false;
    }

    private static IReadOnlyList<RoutineRunSummary> BuildRoutineRunSummaries(RoutineDefinition routine)
    {
        return (routine.CronRunLog ?? new List<RoutineRunLogEntry>())
            .OrderByDescending(static entry => entry.Ts)
            .Take(20)
            .Select(entry => new RoutineRunSummary(
                entry.Ts,
                entry.RunAtMs.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(entry.RunAtMs.Value).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                    : "-",
                string.IsNullOrWhiteSpace(entry.Status) ? "-" : entry.Status!,
                string.IsNullOrWhiteSpace(entry.Source) ? (entry.Action ?? "-") : entry.Source!,
                Math.Max(1, entry.AttemptCount),
                entry.Summary ?? string.Empty,
                string.IsNullOrWhiteSpace(entry.Error) ? null : entry.Error,
                string.IsNullOrWhiteSpace(entry.TelegramStatus) ? null : entry.TelegramStatus,
                string.IsNullOrWhiteSpace(entry.ArtifactPath) ? null : entry.ArtifactPath,
                string.IsNullOrWhiteSpace(entry.AgentSessionId) ? null : entry.AgentSessionId,
                string.IsNullOrWhiteSpace(entry.AgentRunId) ? null : entry.AgentRunId,
                string.IsNullOrWhiteSpace(entry.AgentProvider) ? null : entry.AgentProvider,
                string.IsNullOrWhiteSpace(entry.AgentModel) ? null : entry.AgentModel,
                string.IsNullOrWhiteSpace(entry.ToolProfile) ? null : entry.ToolProfile,
                string.IsNullOrWhiteSpace(entry.StartUrl) ? null : entry.StartUrl,
                string.IsNullOrWhiteSpace(entry.FinalUrl) ? null : entry.FinalUrl,
                string.IsNullOrWhiteSpace(entry.PageTitle) ? null : entry.PageTitle,
                string.IsNullOrWhiteSpace(entry.ScreenshotPath) ? null : entry.ScreenshotPath,
                (entry.DownloadPaths ?? new List<string>())
                    .Where(static path => !string.IsNullOrWhiteSpace(path))
                    .ToArray(),
                entry.DurationMs,
                FormatRoutineDuration(entry.DurationMs),
                entry.NextRunAtMs.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(entry.NextRunAtMs.Value).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                    : null
            ))
            .ToArray();
    }

    private static string FormatRoutineDuration(long? durationMs)
    {
        if (!durationMs.HasValue || durationMs.Value < 0)
        {
            return "-";
        }

        if (durationMs.Value < 1000)
        {
            return $"{durationMs.Value}ms";
        }

        var seconds = durationMs.Value / 1000d;
        if (seconds < 60d)
        {
            return $"{seconds:0.0}s";
        }

        var minutes = seconds / 60d;
        return $"{minutes:0.0}m";
    }

    private async Task<(string Language, string Code, string RawText)> TryRepairRoutineCodeAsync(
        string objective,
        string rawText,
        string model,
        string request,
        RoutineSchedule schedule,
        CancellationToken cancellationToken
    )
    {
        var truncatedRaw = (rawText ?? string.Empty).Trim();
        if (truncatedRaw.Length > 5000)
        {
            truncatedRaw = truncatedRaw[..5000] + "\n...(truncated)...";
        }

        var repairPrompt = $"""
                           {objective}

                           [검토 결과]
                           이전 초안은 잘못되었습니다.
                           - 코드 내부에서 실행 시각/요일/날짜를 다시 판단하거나 대기 로직을 넣었습니다.
                           - 루틴 엔진이 이미 스케줄을 처리하므로, 코드는 호출 즉시 작업을 수행해야 합니다.

                           [수정 지시]
                           - 시간/요일/date/datetime/weekday/sleep/while true/cron 관련 실행 조건을 모두 제거하세요.
                           - 한 번 실행되면 즉시 작업을 수행하고 종료하세요.
                           - stdout에 실제 결과를 남기세요.
                           - 이전 초안을 참조하되, 잘못된 스케줄 제어 로직은 버리세요.

                           [이전 초안]
                           {truncatedRaw}
                           """;

        var regenerated = await GenerateByProviderSafeAsync(
            "groq",
            model,
            repairPrompt,
            cancellationToken,
            Math.Min(_context.CodingMaxOutputTokens, 4200)
        );
        var reparsed = ParseCodeCandidate(regenerated.Text, "bash");
        var repairedLanguage = reparsed.Language is "bash" or "python" ? reparsed.Language : "bash";
        var repairedCode = string.IsNullOrWhiteSpace(reparsed.Code)
            ? string.Empty
            : EnsureRoutineShebang(reparsed.Code, repairedLanguage);

        if (string.IsNullOrWhiteSpace(reparsed.Code) || RoutineCodeNeedsRepair(repairedLanguage, repairedCode))
        {
            return (repairedLanguage, repairedCode, regenerated.Text);
        }

        return (repairedLanguage, repairedCode, regenerated.Text);
    }

    private sealed record RoutineCodeValidation(bool Ok, IReadOnlyList<string> Warnings);

    private static RoutineCodeValidation ValidateRoutineGeneratedCode(string language, string code, string request)
    {
        var warnings = new List<string>();
        var normalizedLanguage = NormalizeRoutineScriptLanguage(language);
        var normalizedCode = (code ?? string.Empty).Trim();
        var loweredCode = normalizedCode.ToLowerInvariant();
        var loweredRequest = (request ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            warnings.Add("생성된 실행 코드가 비어 있습니다.");
        }

        if (RoutineCodeNeedsRepair(normalizedLanguage, normalizedCode))
        {
            warnings.Add("루틴 스케줄러가 담당해야 할 시간 판단/대기 로직이 있거나 출력이 부족합니다.");
        }

        if (loweredCode.Contains("실제 작업 로직은 루틴 수정 저장으로 재생성", StringComparison.Ordinal)
            || loweredCode.Contains("자동 생성 코드가 유효하지 않아 기본 템플릿", StringComparison.Ordinal)
            || loweredCode.Contains("todo", StringComparison.Ordinal)
            || loweredCode.Contains("pass  #", StringComparison.Ordinal)
            || loweredCode.Contains("not implemented", StringComparison.Ordinal))
        {
            warnings.Add("실제 작업 대신 템플릿/TODO/미구현 코드가 포함되어 있습니다.");
        }

        if (ContainsAny(loweredRequest, "http", "url", "뉴스", "news", "api", "웹", "사이트")
            && !ContainsAny(loweredCode, "curl", "http", "requests", "urllib", "fetch", "invoke-webrequest"))
        {
            warnings.Add("요청은 웹/URL/API 작업처럼 보이지만 코드에 네트워크 접근 로직이 없습니다.");
        }

        if (ContainsAny(loweredRequest, "파일", "저장", "csv", "json", "다운로드")
            && !ContainsAny(loweredCode, "open(", "write", "cat >", "tee ", "download", "curl -o", "out-file"))
        {
            warnings.Add("요청은 파일 생성/저장 작업처럼 보이지만 파일 출력 로직이 부족합니다.");
        }

        return new RoutineCodeValidation(warnings.Count == 0, warnings);
    }

    private static bool RoutineCodeNeedsRepair(string language, string code)
    {
        var normalizedLanguage = (language ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedCode = (code ?? string.Empty).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            return true;
        }

        var hasScheduleGate =
            normalizedCode.Contains("current_hour", StringComparison.Ordinal)
            || normalizedCode.Contains("current_minute", StringComparison.Ordinal)
            || normalizedCode.Contains("day_of_week", StringComparison.Ordinal)
            || normalizedCode.Contains("schedule.every", StringComparison.Ordinal)
            || normalizedCode.Contains("crontab", StringComparison.Ordinal)
            || normalizedCode.Contains("apscheduler", StringComparison.Ordinal)
            || normalizedCode.Contains("while true", StringComparison.Ordinal)
            || normalizedCode.Contains("sleep 60", StringComparison.Ordinal)
            || normalizedCode.Contains("time.sleep(", StringComparison.Ordinal)
            || normalizedCode.Contains("datetime.now()", StringComparison.Ordinal)
            || normalizedCode.Contains("weekday()", StringComparison.Ordinal)
            || normalizedCode.Contains("date +%u", StringComparison.Ordinal)
            || normalizedCode.Contains("date +%w", StringComparison.Ordinal)
            || normalizedCode.Contains("top -bn1", StringComparison.Ordinal);

        if (hasScheduleGate)
        {
            return true;
        }

        if (normalizedLanguage == "bash")
        {
            return !normalizedCode.Contains("echo ", StringComparison.Ordinal)
                && !normalizedCode.Contains("printf ", StringComparison.Ordinal)
                && !normalizedCode.Contains("cat <<", StringComparison.Ordinal);
        }

        if (normalizedLanguage == "python")
        {
            return !normalizedCode.Contains("print(", StringComparison.Ordinal);
        }

        return false;
    }

    private static string WriteRoutineScript(string runDir, string language, string code)
    {
        var scriptFileName = string.Equals(language, "python", StringComparison.OrdinalIgnoreCase) ? "run.py" : "run.sh";
        var scriptPath = Path.Combine(runDir, scriptFileName);
        File.WriteAllText(scriptPath, code, new UTF8Encoding(false));
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(scriptPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            catch
            {
            }
        }

        return scriptPath;
    }

    private static string BuildRoutineTitle(string request)
    {
        var text = (request ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return "새 루틴";
        }

        var title = text.Length <= 26 ? text : text[..26].TrimEnd() + "...";
        return title;
    }

    private static DateTimeOffset ComputeNextDailyRunUtc(int hour, int minute, string timezoneId, DateTimeOffset nowUtc)
    {
        var tz = RoutineSchedulePolicy.ResolveTimeZone(timezoneId);
        var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, tz);
        var nextLocal = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, hour, minute, 0, DateTimeKind.Unspecified);
        if (nextLocal <= nowLocal.DateTime)
        {
            nextLocal = nextLocal.AddDays(1);
        }

        var offset = tz.GetUtcOffset(nextLocal);
        return new DateTimeOffset(nextLocal, offset).ToUniversalTime();
    }


}
