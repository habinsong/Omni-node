using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

internal static class TelegramNaturalCommandPolicy
{
    public static string? TryBuildNaturalPseudoCommand(string normalized, string? lowered = null)
    {
        var text = (normalized ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var lower = string.IsNullOrWhiteSpace(lowered)
            ? text.ToLowerInvariant()
            : lowered!;

        var commandLike = Regex.Match(text, @"(?i)^(help|start|talk|code|model|llm|skill|skills|coding|refactor|memory|doctor|plan|task|notebook|handoff|routine|routines|metrics|kill)\b(.*)$");
        if (commandLike.Success)
        {
            var head = commandLike.Groups[1].Value.ToLowerInvariant();
            var tail = commandLike.Groups[2].Value;
            return "/" + head + tail;
        }

        if (ContainsAny(lower, "대화 프리셋", "대화 프로필", "talk 모드", "대화 탭 환경"))
        {
            var thinking = ExtractThinkingLevel(lower);
            return thinking == null ? "/talk" : $"/talk {thinking}";
        }

        if (ContainsAny(lower, "코딩 프리셋", "코딩 프로필", "code 모드", "코딩 탭 환경"))
        {
            var thinking = ExtractThinkingLevel(lower);
            return thinking == null ? "/code" : $"/code {thinking}";
        }

        if (ContainsAny(lower, "llm 단일 모드", "단일 모드로", "single 모드", "single mode"))
        {
            return "/llm mode single";
        }

        if (ContainsAny(lower, "llm 오케스트레이션 모드", "오케스트레이션 모드로", "orchestration mode", "orchestration 모드"))
        {
            return "/llm mode orchestration";
        }

        if (ContainsAny(lower, "llm 다중 모드", "다중 모드로", "멀티 모드로", "multi mode", "multi 모드"))
        {
            return "/llm mode multi";
        }

        if (ContainsAny(lower, "최근 코딩 결과", "마지막 코딩 결과", "코딩 결과 보여", "coding result"))
        {
            return "/coding last";
        }

        if (ContainsAny(lower, "코딩 상태", "코딩 설정", "coding status"))
        {
            return "/coding status";
        }

        if (ContainsAny(lower, "코딩 파일 목록", "최근 코딩 파일", "coding files"))
        {
            return "/coding files";
        }

        var codingFilePreview = Regex.Match(text, @"(?is)(?:코딩 파일|coding file)\s*(?:보여|열어|preview)?\s*[:：]?\s*(.+)$");
        if (codingFilePreview.Success)
        {
            var query = codingFilePreview.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                return $"/coding file {query}";
            }
        }

        var codingLanguage = Regex.Match(text, @"(?is)(?:(단일|single|오케스트레이션|orchestration|다중|multi)\s*)?(?:코딩|coding)\s*(?:언어|language)\s*(?:를)?\s*([a-zA-Z0-9#+._-]+)\s*(?:로|으로)?\s*(?:바꿔|변경|설정)");
        if (codingLanguage.Success)
        {
            var mode = NormalizeMode(codingLanguage.Groups[1].Value);
            var language = codingLanguage.Groups[2].Value.Trim();
            return string.IsNullOrWhiteSpace(mode)
                ? $"/coding language {language}"
                : $"/coding language {mode} {language}";
        }

        var codingMode = Regex.Match(text, @"(?is)(?:코딩|coding)\s*모드.*?(단일|single|오케스트레이션|orchestration|다중|multi).*(?:바꿔|변경|설정)");
        if (codingMode.Success)
        {
            var mode = NormalizeMode(codingMode.Groups[1].Value);
            if (!string.IsNullOrWhiteSpace(mode))
            {
                return $"/coding mode {mode}";
            }
        }

        var codingProvider = Regex.Match(
            text,
            @"(?is)(단일|single|오케스트레이션|orchestration|다중|multi)\s*코딩\s*(?:요약\s*)?(?:제공자|provider)\s*(?:를|을)?\s*(auto|자동|groq|그록|gemini|제미니|copilot|코파일럿|cerebras|세레브라스|세레브라|nvidia|nvidia-nim|nim|엔비디아|codex|코덱스)\s*(?:로|으로)?\s*(?:바꿔|변경|설정)"
        );
        if (codingProvider.Success)
        {
            var mode = NormalizeMode(codingProvider.Groups[1].Value);
            var provider = ExtractProviderAlias(codingProvider.Groups[2].Value, allowAuto: true);
            if (!string.IsNullOrWhiteSpace(mode) && !string.IsNullOrWhiteSpace(provider))
            {
                return $"/coding {mode} provider {provider}";
            }
        }

        var codingModel = Regex.Match(
            text,
            @"(?is)(단일|single|오케스트레이션|orchestration|다중|multi)\s*코딩\s*(?:모델|model)\s*(?:을|를)?\s*([a-zA-Z0-9._/\-]+)\s*(?:로|으로)?\s*(?:바꿔|변경|설정)"
        );
        if (codingModel.Success)
        {
            var mode = NormalizeMode(codingModel.Groups[1].Value);
            var modelId = codingModel.Groups[2].Value.Trim();
            if (!string.IsNullOrWhiteSpace(mode) && !string.IsNullOrWhiteSpace(modelId))
            {
                return $"/coding {mode} model {modelId}";
            }
        }

        var codingWorker = Regex.Match(
            text,
            @"(?is)(오케스트레이션|orchestration|다중|multi)\s*코딩\s*(?:워커|worker)\s*(groq|그록|gemini|제미니|copilot|코파일럿|cerebras|세레브라스|세레브라|nvidia|nvidia-nim|nim|엔비디아|codex|코덱스)\s*(?:모델|model)?\s*(?:을|를)?\s*([a-zA-Z0-9._/\-]+|none|없음|선택안함|선택 안함)\s*(?:로|으로)?\s*(?:바꿔|변경|설정)"
        );
        if (codingWorker.Success)
        {
            var mode = NormalizeMode(codingWorker.Groups[1].Value);
            var provider = ExtractProviderAlias(codingWorker.Groups[2].Value, allowAuto: false);
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

        var codingSingleRun = Regex.Match(text, @"(?is)(?:단일|single)\s*코딩(?:으로)?\s*[:：]?\s*(.+)$");
        if (codingSingleRun.Success)
        {
            var request = codingSingleRun.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(request))
            {
                return $"/coding single run {request}";
            }
        }

        var codingOrchRun = Regex.Match(text, @"(?is)(?:오케스트레이션|orchestration)\s*코딩(?:으로)?\s*[:：]?\s*(.*)$");
        if (codingOrchRun.Success)
        {
            var request = codingOrchRun.Groups[1].Value.Trim();
            return string.IsNullOrWhiteSpace(request)
                ? "/coding orchestration run"
                : $"/coding orchestration run {request}";
        }

        var codingMultiRun = Regex.Match(text, @"(?is)(?:다중|multi)\s*코딩(?:으로)?\s*[:：]?\s*(.+)$");
        if (codingMultiRun.Success)
        {
            var request = codingMultiRun.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(request))
            {
                return $"/coding multi run {request}";
            }
        }

        var codingRun = Regex.Match(text, @"(?is)(?:코딩|coding)\s*(?:실행|run)\s*[:：]?\s*(.*)$");
        if (codingRun.Success)
        {
            var request = codingRun.Groups[1].Value.Trim();
            return string.IsNullOrWhiteSpace(request)
                ? "/coding run"
                : $"/coding run {request}";
        }

        if (ContainsAny(lower, "safe refactor 상태", "refactor 상태", "리팩터 상태"))
        {
            return "/refactor status";
        }

        if (ContainsAny(lower, "safe refactor 적용", "refactor 적용", "리팩터 적용"))
        {
            return "/refactor apply";
        }

        var refactorRead = Regex.Match(text, @"(?is)(?:safe refactor|refactor|리팩터)\s*(?:읽기|read)\s*([^\s]+)(?:\s+([0-9]+))?(?:\s+([0-9]+))?$");
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

        if (ContainsAny(lower, "메모리 초기화", "메모리 비우기", "메모리 삭제", "메모리 지워"))
        {
            return "/memory clear";
        }

        if (ContainsAny(lower, "메모리 노트", "메모리 저장", "메모리 생성", "메모리 만들어"))
        {
            return ContainsAny(lower, "compact", "압축", "짧게")
                ? "/memory create compact"
                : "/memory create";
        }

        if (ContainsAny(lower, "doctor 실행", "doctor 결과", "doctor 보여", "환경 진단", "상태 점검", "시스템 점검", "진단 실행", "최근 진단", "마지막 진단"))
        {
            var parts = new List<string> { "/doctor" };
            if (ContainsAny(lower, "last", "latest", "최근", "마지막"))
            {
                parts.Add("last");
            }

            if (ContainsAny(lower, "json"))
            {
                parts.Add("json");
            }

            return string.Join(' ', parts);
        }

        if (ContainsAny(lower, "단일 제공자", "single provider", "single 제공자", "단일 provider"))
        {
            var provider = ExtractProviderAlias(lower, allowAuto: false);
            if (!string.IsNullOrWhiteSpace(provider))
            {
                return $"/llm single provider {provider}";
            }
        }

        if (ContainsAny(lower, "오케스트레이션 제공자", "orchestration provider", "집계 제공자", "집계 provider"))
        {
            var provider = ExtractProviderAlias(lower, allowAuto: true);
            if (!string.IsNullOrWhiteSpace(provider))
            {
                return $"/llm orchestration provider {provider}";
            }
        }

        if (ContainsAny(lower, "다중 요약 제공자", "multi summary provider", "요약 제공자", "summary provider"))
        {
            var provider = ExtractProviderAlias(lower, allowAuto: true);
            if (!string.IsNullOrWhiteSpace(provider))
            {
                return $"/llm multi summary {provider}";
            }
        }

        var singleProviderSwitch = Regex.Match(lower, @"(?:단일|single).*(groq|그록|gemini|제미니|copilot|코파일럿|cerebras|세레브라스|세레브라|nvidia|nvidia-nim|nim|엔비디아|codex|코덱스).*(바꿔|변경|설정)");
        if (singleProviderSwitch.Success)
        {
            var provider = ExtractProviderAlias(singleProviderSwitch.Groups[1].Value, allowAuto: false);
            if (!string.IsNullOrWhiteSpace(provider))
            {
                return $"/llm single provider {provider}";
            }
        }

        var orchestrationProviderSwitch = Regex.Match(lower, @"(?:오케스트레이션|orchestration|집계).*(auto|자동|groq|그록|gemini|제미니|copilot|코파일럿|cerebras|세레브라스|세레브라|nvidia|nvidia-nim|nim|엔비디아|codex|코덱스).*(바꿔|변경|설정)");
        if (orchestrationProviderSwitch.Success)
        {
            var provider = ExtractProviderAlias(orchestrationProviderSwitch.Groups[1].Value, allowAuto: true);
            if (!string.IsNullOrWhiteSpace(provider))
            {
                return $"/llm orchestration provider {provider}";
            }
        }

        var quickProviderSwitch = Regex.Match(lower, @"(groq|그록|gemini|제미니|copilot|코파일럿|cerebras|세레브라스|세레브라|nvidia|nvidia-nim|nim|엔비디아|codex|코덱스).*(바꿔|변경|설정)");
        if (quickProviderSwitch.Success
            && !ContainsAny(lower, "단일", "single", "오케스트레이션", "요약", "summary", "다중", "multi"))
        {
            var provider = ExtractProviderAlias(quickProviderSwitch.Groups[1].Value, allowAuto: false);
            if (!string.IsNullOrWhiteSpace(provider))
            {
                return $"/model {provider}";
            }
        }

        var singleModel = Regex.Match(text, @"(?i)(?:단일|single)\s*(?:모델|model)\s*([a-zA-Z0-9._/\-]+)");
        if (singleModel.Success)
        {
            return $"/llm single model {singleModel.Groups[1].Value}";
        }

        var orchestrationModel = Regex.Match(text, @"(?i)(?:오케스트레이션|orchestration)\s*(?:모델|model)\s*([a-zA-Z0-9._/\-]+)");
        if (orchestrationModel.Success)
        {
            return $"/llm orchestration model {orchestrationModel.Groups[1].Value}";
        }

        var multiGroqModel = Regex.Match(text, @"(?i)(?:다중|multi)\s*groq\s*(?:모델|model)\s*([a-zA-Z0-9._/\-]+)");
        if (multiGroqModel.Success)
        {
            return $"/llm multi groq {multiGroqModel.Groups[1].Value}";
        }

        var multiGeminiModel = Regex.Match(text, @"(?i)(?:다중|multi)\s*(?:gemini|제미니)\s*(?:모델|model)\s*([a-zA-Z0-9._/\-]+)");
        if (multiGeminiModel.Success)
        {
            return $"/llm multi gemini {multiGeminiModel.Groups[1].Value}";
        }

        var multiCopilotModel = Regex.Match(text, @"(?i)(?:다중|multi)\s*(?:copilot|코파일럿)\s*(?:모델|model)\s*([a-zA-Z0-9._/\-]+)");
        if (multiCopilotModel.Success)
        {
            return $"/llm multi copilot {multiCopilotModel.Groups[1].Value}";
        }

        var multiCerebrasModel = Regex.Match(text, @"(?i)(?:다중|multi)\s*(?:cerebras|세레브라스|세레브라)\s*(?:모델|model)\s*([a-zA-Z0-9._/\-]+)");
        if (multiCerebrasModel.Success)
        {
            return $"/llm multi cerebras {multiCerebrasModel.Groups[1].Value}";
        }

        var multiNvidiaModel = Regex.Match(text, @"(?i)(?:다중|multi)\s*(?:nvidia|nvidia-nim|nim|엔비디아)\s*(?:모델|model)\s*([a-zA-Z0-9._/\-]+)");
        if (multiNvidiaModel.Success)
        {
            return $"/llm multi nvidia {multiNvidiaModel.Groups[1].Value}";
        }

        var multiCodexModel = Regex.Match(text, @"(?i)(?:다중|multi)\s*(?:codex|코덱스)\s*(?:모델|model)\s*([a-zA-Z0-9._/\-]+)");
        if (multiCodexModel.Success)
        {
            return $"/llm multi codex {multiCodexModel.Groups[1].Value}";
        }

        if (ContainsAny(lower, "계획 목록", "plan 목록", "플랜 목록"))
        {
            return "/plan list";
        }

        var planGet = Regex.Match(text, @"(?i)(?:계획|plan)\s*(?:상세|보기|get)\s*([a-z0-9_\-]+)");
        if (planGet.Success)
        {
            return $"/plan get {planGet.Groups[1].Value}";
        }

        var planReview = Regex.Match(text, @"(?i)(?:계획|plan)\s*(?:리뷰|검토|review)\s*([a-z0-9_\-]+)");
        if (planReview.Success)
        {
            return $"/plan review {planReview.Groups[1].Value}";
        }

        var planApprove = Regex.Match(text, @"(?i)(?:계획|plan)\s*(?:승인|approve)\s*([a-z0-9_\-]+)");
        if (planApprove.Success)
        {
            return $"/plan approve {planApprove.Groups[1].Value}";
        }

        var planRun = Regex.Match(text, @"(?i)(?:계획|plan)\s*(?:실행|run)\s*([a-z0-9_\-]+)");
        if (planRun.Success)
        {
            return $"/plan run {planRun.Groups[1].Value}";
        }

        var planCreate = Regex.Match(text, @"(?i)(?:계획|plan)\s*(?:생성|만들|추가)\s*[:：]?\s*(.+)$");
        if (planCreate.Success)
        {
            var request = planCreate.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(request))
            {
                return $"/plan create {request}";
            }
        }

        if (ContainsAny(lower, "작업 목록", "task 목록", "태스크 목록"))
        {
            return "/task list";
        }

        var taskCreate = Regex.Match(text, @"(?i)(?:작업|task)\s*(?:생성|create)\s*([a-z0-9_\-]+)");
        if (taskCreate.Success)
        {
            return $"/task create {taskCreate.Groups[1].Value}";
        }

        var taskStatus = Regex.Match(text, @"(?i)(?:작업|task)\s*(?:상태|status|get)\s*([a-z0-9_\-]+)");
        if (taskStatus.Success)
        {
            return $"/task status {taskStatus.Groups[1].Value}";
        }

        var taskRun = Regex.Match(text, @"(?i)(?:작업|task)\s*(?:실행|run)\s*([a-z0-9_\-]+)");
        if (taskRun.Success)
        {
            return $"/task run {taskRun.Groups[1].Value}";
        }

        var taskCancel = Regex.Match(text, @"(?i)(?:작업|task)\s*(?:취소|중지|cancel)\s*([a-z0-9_\-]+)\s+([a-z0-9_\-]+)");
        if (taskCancel.Success)
        {
            return $"/task cancel {taskCancel.Groups[1].Value} {taskCancel.Groups[2].Value}";
        }

        var taskOutput = Regex.Match(text, @"(?i)(?:작업|task)\s*(?:결과|output)\s*([a-z0-9_\-]+)\s+([a-z0-9_\-]+)");
        if (taskOutput.Success)
        {
            return $"/task output {taskOutput.Groups[1].Value} {taskOutput.Groups[2].Value}";
        }

        if (ContainsAny(lower, "노트북 보여", "노트북 열어", "notebook show"))
        {
            return "/notebook show";
        }

        var notebookAppend = Regex.Match(text, @"(?i)(?:노트북|notebook)\s*(?:append|추가|기록)\s*(learning|decision|verification|학습|결정|검증)\s*[:：]?\s*(.+)$");
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

        if (ContainsAny(lower, "handoff", "인수인계"))
        {
            return "/handoff";
        }

        if (ContainsAny(lower, "루틴 목록", "루틴 리스트", "routines 목록"))
        {
            return "/routine list";
        }

        var routineRuns = Regex.Match(text, @"(?is)루틴\s*(?:실행\s*이력|이력|runs)\s*([a-z0-9\-]+)");
        if (routineRuns.Success)
        {
            return $"/routine runs {routineRuns.Groups[1].Value}";
        }

        var routineDetail = Regex.Match(text, @"(?is)루틴\s*(?:상세|detail)\s*([a-z0-9\-]+)\s+([0-9]{6,})");
        if (routineDetail.Success)
        {
            return $"/routine detail {routineDetail.Groups[1].Value} {routineDetail.Groups[2].Value}";
        }

        var routineResend = Regex.Match(text, @"(?is)루틴\s*(?:재전송|텔레그램\s*재전송|resend)\s*([a-z0-9\-]+)\s+([0-9]{6,})");
        if (routineResend.Success)
        {
            return $"/routine resend {routineResend.Groups[1].Value} {routineResend.Groups[2].Value}";
        }

        var routineRun = Regex.Match(text, @"(?i)루틴\s*(?:즉시\s*)?(?:실행|run)\s*([a-z0-9\-]+)");
        if (routineRun.Success)
        {
            return $"/routine run {routineRun.Groups[1].Value}";
        }

        var routineOn = Regex.Match(text, @"(?i)루틴\s*(?:켜|활성화|on)\s*([a-z0-9\-]+)");
        if (routineOn.Success)
        {
            return $"/routine on {routineOn.Groups[1].Value}";
        }

        var routineOff = Regex.Match(text, @"(?i)루틴\s*(?:꺼|비활성화|off|중지)\s*([a-z0-9\-]+)");
        if (routineOff.Success)
        {
            return $"/routine off {routineOff.Groups[1].Value}";
        }

        var routineDelete = Regex.Match(text, @"(?i)루틴\s*(?:삭제|제거|지워)\s*([a-z0-9\-]+)");
        if (routineDelete.Success)
        {
            return $"/routine delete {routineDelete.Groups[1].Value}";
        }

        var routineCreate = Regex.Match(text, @"(?i)루틴\s*(?:생성|등록|추가)\s*[:：]?\s*(.+)$");
        if (routineCreate.Success)
        {
            var request = routineCreate.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(request))
            {
                return $"/routine create {request}";
            }
        }

        var routineUpdate = Regex.Match(text, @"(?is)루틴\s*(?:수정|업데이트|update)\s*([a-z0-9\-]+)\s*[:：]?\s*(.+)$");
        if (routineUpdate.Success)
        {
            var routineId = routineUpdate.Groups[1].Value.Trim();
            var request = routineUpdate.Groups[2].Value.Trim();
            if (!string.IsNullOrWhiteSpace(routineId) && !string.IsNullOrWhiteSpace(request))
            {
                return $"/routine update {routineId} {request}";
            }
        }

        if (ContainsAny(lower, "메트릭 보여", "메트릭 조회", "시스템 메트릭", "metrics 보여"))
        {
            return "/metrics";
        }

        return null;
    }

    public static string? ExtractProviderAlias(string text, bool allowAuto)
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

        if (ContainsAny(lowered, "nvidia", "nvidia-nim", "nim", "엔비디아"))
        {
            return "nvidia";
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

    public static string? ExtractThinkingLevel(string lowered)
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

    public static string? ExtractHelpTopic(string lowered)
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

    private static string NormalizeMode(string text)
    {
        return (text ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "단일" => "single",
            "single" => "single",
            "오케스트레이션" => "orchestration",
            "orchestration" => "orchestration",
            "다중" => "multi",
            "multi" => "multi",
            _ => string.Empty
        };
    }

    private static bool ContainsAny(string text, params string[] patterns)
    {
        return patterns.Any(pattern => text.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }
}
