using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    private static readonly Regex PythonModuleNotFoundRegex = new("ModuleNotFoundError:\\s+No module named ['\\\"](?<module>[^'\\\"]+)['\\\"]", RegexOptions.Compiled);
    private static readonly Regex PythonImportErrorRegex = new("ImportError:\\s+No module named ['\\\"]?(?<module>[A-Za-z0-9_.-]+)['\\\"]?", RegexOptions.Compiled);
    private static readonly Regex NodeModuleNotFoundRegex = new("Cannot find module ['\\\"](?<module>[^'\\\"]+)['\\\"]|ERR_MODULE_NOT_FOUND[^'\\\"]*['\\\"](?<module2>[^'\\\"]+)['\\\"]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CommandNotFoundRegex = new("command not found:\\s*(?<cmd>[A-Za-z0-9][A-Za-z0-9+._-]{1,63})|(?<cmd2>[A-Za-z0-9][A-Za-z0-9+._-]{1,63}):\\s*(?:command\\s+not\\s+found|not\\s+found)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PythonImportRegex = new("^\\s*import\\s+(?<mods>[A-Za-z0-9_.,\\s]+)", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex PythonFromImportRegex = new("^\\s*from\\s+(?<mod>[A-Za-z0-9_.]+)\\s+import\\s+", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex NodeImportRegex = new("from\\s+['\\\"](?<mod>[^'\\\"]+)['\\\"]|require\\(\\s*['\\\"](?<mod2>[^'\\\"]+)['\\\"]\\s*\\)|import\\(\\s*['\\\"](?<mod3>[^'\\\"]+)['\\\"]\\s*\\)", RegexOptions.Compiled);
    private static readonly Regex ShellTokenRegex = new("'(?<sq>[^']*)'|\"(?<dq>[^\"]*)\"|(?<bare>[^\\s]+)", RegexOptions.Compiled);
    private static readonly Dictionary<string, string> PythonCliPackageMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = "black",
        ["django-admin"] = "django",
        ["flask"] = "flask",
        ["gradio"] = "gradio",
        ["gunicorn"] = "gunicorn",
        ["jupyter"] = "jupyterlab",
        ["pytest"] = "pytest",
        ["ruff"] = "ruff",
        ["streamlit"] = "streamlit",
        ["uvicorn"] = "uvicorn"
    };
    private static readonly Dictionary<string, string> NodeCliPackageMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["next"] = "next",
        ["nodemon"] = "nodemon",
        ["parcel"] = "parcel",
        ["react-scripts"] = "react-scripts",
        ["serve"] = "serve",
        ["ts-node"] = "ts-node",
        ["tsx"] = "tsx",
        ["vite"] = "vite",
        ["webpack"] = "webpack",
        ["webpack-dev-server"] = "webpack-dev-server"
    };
    private static readonly HashSet<string> PythonCommandPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "black","flask","gradio","gunicorn","jupyter","pip","pip3","py","py.test","pytest","python","python3","ruff","streamlit","uvicorn"
    };
    private static readonly HashSet<string> NodeCommandPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "next","node","nodemon","npm","npx","parcel","pnpm","react-scripts","serve","ts-node","tsx","vite","webpack","webpack-dev-server","yarn"
    };
    private static readonly Dictionary<string, string> PythonModulePackageMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cv2"] = "opencv-python",
        ["PIL"] = "pillow",
        ["yaml"] = "pyyaml",
        ["sklearn"] = "scikit-learn",
        ["bs4"] = "beautifulsoup4",
        ["dotenv"] = "python-dotenv",
        ["Crypto"] = "pycryptodome"
    };
    private static readonly HashSet<string> PythonStdlibModules = new(StringComparer.OrdinalIgnoreCase)
    {
        "abc","argparse","array","asyncio","base64","binascii","bisect","calendar","cmath","collections","concurrent","contextlib",
        "copy","csv","ctypes","curses","dataclasses","datetime","decimal","difflib","dis","email","enum","errno","faulthandler","fnmatch",
        "fractions","functools","gc","getopt","getpass","gettext","glob","gzip","hashlib","heapq","hmac","html","http","imaplib",
        "importlib","inspect","io","ipaddress","itertools","json","linecache","locale","logging","lzma","math","mimetypes","multiprocessing",
        "numbers","operator","os","pathlib","pickle","pkgutil","platform","plistlib","pprint","queue","random","re","sched","secrets","select",
        "selectors","shelve","shlex","shutil","signal","site","socket","sqlite3","ssl","stat","statistics","string","stringprep","struct",
        "subprocess","sys","sysconfig","tarfile","tempfile","textwrap","threading","time","timeit","tkinter","_tkinter","tokenize","traceback","turtle","types","typing",
        "unittest","urllib","uuid","venv","warnings","wave","weakref","webbrowser","xml","xmlrpc","zipfile","zoneinfo","zlib"
    };
    private static readonly HashSet<string> NodeBuiltinModules = new(StringComparer.OrdinalIgnoreCase)
    {
        "assert","buffer","child_process","cluster","console","constants","crypto","dgram","diagnostics_channel","dns","domain","events",
        "fs","http","http2","https","inspector","module","net","os","path","perf_hooks","process","punycode","querystring","readline",
        "repl","stream","string_decoder","timers","tls","tty","url","util","v8","vm","wasi","worker_threads","zlib"
    };
    private static readonly HashSet<string> ProgramInstallDenyList = new(StringComparer.OrdinalIgnoreCase)
    {
        "bash","sh","zsh","sudo","env","python","python3","pip","pip3","node","npm","npx","pnpm","yarn","dotnet","java","javac","kotlinc","cc","c++","gcc","g++","git","ls","cat","echo","pwd"
    };
    private static readonly Regex ExplicitShellExecutionCommandRegex = new(
        "(?<cmd>node|python3?|bash)\\s+(?<path>(?:\\.{0,2}[\\\\/])?(?:[\\w.-]+[\\\\/])*(?:[\\w.-]+\\.)+(?:py|js|mjs|cjs|sh))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly Regex ExplicitExecutionTargetRegex = new(
        "(?<path>(?:\\.{0,2}[\\\\/])?(?:[\\w.-]+[\\\\/])*(?:[\\w.-]+\\.)+(?:py|js|mjs|cjs|sh))\\s*(?:를|을)?\\s*(?:직접\\s*)?(?:실행(?:\\s*시|\\s*해서|\\s*해|\\s*결과|\\s*후)?|run\\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private void RecordEvent(string message)
    {
        lock (_eventLock)
        {
            _recentEvents.Enqueue($"{DateTimeOffset.UtcNow:O} {message}");
            while (_recentEvents.Count > 50)
            {
                _recentEvents.Dequeue();
            }
        }
    }

    private string BuildContextSnapshot(string latestMetrics)
    {
        List<string> events;
        lock (_eventLock)
        {
            events = _recentEvents.ToList();
        }

        var builder = new System.Text.StringBuilder();
        builder.AppendLine("latest_metrics:");
        builder.AppendLine(latestMetrics);
        builder.AppendLine("recent_events:");
        foreach (var item in events)
        {
            builder.AppendLine(item);
        }
        return builder.ToString().Trim();
    }

    private async Task<string> ChatFallbackForUnknownAsync(string text, CancellationToken cancellationToken)
    {
        if (IsCopilotResponseTestPrompt(text))
        {
            return BuildMockCopilotTestResponse(_copilotWrapper.GetSelectedModel());
        }

        if (_llmRouter.HasGeminiApiKey())
        {
            return await _llmRouter.GenerateGeminiChatAsync(text, cancellationToken);
        }

        if (_llmRouter.HasGroqApiKey())
        {
            return await _llmRouter.GenerateGroqChatAsync(text, _llmRouter.GetSelectedGroqModel(), cancellationToken);
        }

        var copilotStatus = await _copilotWrapper.GetStatusAsync(cancellationToken);
        if (copilotStatus.Installed && copilotStatus.Authenticated)
        {
            return await _copilotWrapper.GenerateChatAsync(text, _copilotWrapper.GetSelectedModel(), cancellationToken);
        }

        return "LLM API 키(Groq/Gemini) 또는 Copilot 인증이 없어 일반 대화를 처리할 수 없습니다.";
    }

    private ConversationThreadView PrepareConversation(
        string scope,
        string mode,
        string? conversationId,
        string? conversationTitle,
        string? project,
        string? category,
        IReadOnlyList<string>? tags,
        IReadOnlyList<string>? linkedMemoryNotes
    )
    {
        var safeConversationId = conversationId;
        if (!string.IsNullOrWhiteSpace(safeConversationId))
        {
            var existing = _conversationStore.Get(safeConversationId.Trim());
            if (existing != null
                && (!existing.Scope.Equals(scope, StringComparison.OrdinalIgnoreCase)
                    || !existing.Mode.Equals(mode, StringComparison.OrdinalIgnoreCase)))
            {
                safeConversationId = null;
            }
        }

        var thread = _conversationStore.Ensure(scope, mode, safeConversationId, conversationTitle, project, category, tags);
        var mergedNotes = MergeMemoryNoteNames(thread.LinkedMemoryNotes, linkedMemoryNotes);
        if (mergedNotes.Count != thread.LinkedMemoryNotes.Count
            || mergedNotes.Except(thread.LinkedMemoryNotes, StringComparer.OrdinalIgnoreCase).Any())
        {
            thread = _conversationStore.SetLinkedMemoryNotes(thread.Id, mergedNotes);
        }

        return thread;
    }

    private SessionContext PrepareSessionContext(
        string scope,
        string mode,
        string? conversationId,
        string? conversationTitle,
        string? project,
        string? category,
        IReadOnlyList<string>? tags,
        IReadOnlyList<string>? linkedMemoryNotes,
        string? source
    )
    {
        var thread = PrepareConversation(
            scope,
            mode,
            conversationId,
            conversationTitle,
            project,
            category,
            tags,
            linkedMemoryNotes
        );
        return SessionContext.Create(thread, source);
    }

    private string BuildContextualInput(
        string conversationId,
        string input,
        IReadOnlyList<string>? requestMemoryNotes,
        bool includeLocalTimeHint = false,
        string? contextDecisionInput = null
    )
    {
        var contextDecisionText = contextDecisionInput ?? input;
        var suppressPriorContext = SearchQueryPolicy.LooksLikeStandaloneFreshGreeting(contextDecisionText);
        var includePriorContext = !suppressPriorContext && ShouldUsePriorConversationContext(conversationId, contextDecisionText);
        var explicitRequestNotes = NormalizeExplicitMemoryNoteNames(requestMemoryNotes);
        // linked memory notes는 압축된 대화 맥락이므로 includePriorContext와 무관하게 항상 로드.
        // 그렇지 않으면 압축 직후 첫 질문에서 맥락이 단절됨.
        // 단독 인사는 새 대화 행위라서 이전 주제 메모리도 함께 붙이지 않는다.
        var autoLinkedNotes = suppressPriorContext
            ? Array.Empty<string>()
            : _conversationStore.Get(conversationId)?.LinkedMemoryNotes ?? Array.Empty<string>();
        var notes = MergeMemoryNoteNames(
            autoLinkedNotes,
            explicitRequestNotes
        );
        var noteBlocks = new List<string>();
        foreach (var name in notes.Take(4))
        {
            var read = _memoryNoteStore.Read(name);
            if (read == null)
            {
                continue;
            }

            var content = read.Content.Length > 900 ? read.Content[..900] + "\n...(truncated)" : read.Content;
            noteBlocks.Add($"### {name}\n{content}");
        }

        var history = string.Empty;
        if (includePriorContext)
        {
            var historyRaw = _conversationStore.BuildHistoryText(conversationId, _context.ConversationHistoryMessages);
            history = ConversationHistoryPolicy.BuildBudgetedContextHistory(historyRaw, 5200);
        }

        var builder = new StringBuilder();
        builder.AppendLine("[컨텍스트 사용 규칙]");
        builder.AppendLine("- '새 요청'을 최우선으로 처리하세요.");
        builder.AppendLine("- 새 요청이 '왜/그럼/그래서/그건/이건/예시는/근거는/더 자세히/다시'처럼 짧은 후속 질문이면 [최근 대화]의 바로 직전 주제와 답변을 기준으로 해석하세요.");
        builder.AppendLine("- 새 요청에 자체 주제와 대상이 분명하면 [최근 대화]는 배경으로만 참고하고, 이전 주제를 끌고 오지 마세요.");
        builder.AppendLine("- 제공된 최근 대화/메모리와 새 요청이 충돌하면 새 요청을 따르세요.");
        builder.AppendLine("- 이전 답변 형식(예: 뉴스 N건 목록)을 관성으로 복사하지 마세요.");
        builder.AppendLine("- 절대 답변에 [user], [assistant], [system], [Single ...], [Multi ...], [Project Context], [Active Skill ...], [Think+ ...], [컨텍스트 ...], [최근 대화], [공유 메모리 노트], [새 요청], [로컬 시간] 같은 내부 마커/헤더를 출력하지 마세요. 이 마커들은 LLM 입력 구조용이며 사용자에게 보일 답변이 아닙니다.");
        builder.AppendLine("- '확인.', '준비되었습니다.', '질문해 주세요.' 같이 내용 없는 인사·확인 응답을 답변에 넣지 마세요. 사용자의 질문에 직접 답하세요.");
        builder.AppendLine();
        if (noteBlocks.Count > 0)
        {
            builder.AppendLine("[공유 메모리 노트]");
            builder.AppendLine(string.Join("\n\n", noteBlocks));
            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(history))
        {
            builder.AppendLine("[최근 대화]");
            builder.AppendLine(history);
            builder.AppendLine();
        }

        if (includeLocalTimeHint && SearchQueryPolicy.LooksLikeLocalDateTimeQuestion(contextDecisionText))
        {
            builder.AppendLine("[로컬 시간]");
            builder.AppendLine(BuildLocalNowText());
            builder.AppendLine("- 현재 시각/날짜/요일/타임존 관련 질문은 위 로컬 시간을 기준으로 답하세요.");
            builder.AppendLine();
        }

        builder.AppendLine("[새 요청]");
        builder.AppendLine(input.Trim());
        var contextual = builder.ToString().Trim();
        if (contextual.Length <= 8000)
        {
            return contextual;
        }

        // 8000자 초과 시 꼬리 잘림 대신: 규칙 블록 + 새 요청을 우선 보존하고
        // [최근 대화] 섹션만 축소.
        var requestMarker = "\n[새 요청]";
        var requestIdx = contextual.IndexOf(requestMarker, StringComparison.Ordinal);
        if (requestIdx < 0)
        {
            return $"[context_truncated]\n{contextual[^8000..]}";
        }

        var headerAndHistory = contextual[..requestIdx];
        var requestSection = contextual[requestIdx..];
        var availableForHeaderAndHistory = 8000 - requestSection.Length - 50;

        if (availableForHeaderAndHistory <= 200)
        {
            return $"[context_truncated]\n{requestSection.TrimStart()}";
        }

        if (headerAndHistory.Length <= availableForHeaderAndHistory)
        {
            return $"{headerAndHistory}{requestSection}";
        }

        // 규칙 블록(~400자)은 보존, 히스토리만 축소
        var historyMarker = "\n[최근 대화]";
        var historyIdx = headerAndHistory.IndexOf(historyMarker, StringComparison.Ordinal);
        if (historyIdx < 0)
        {
            return $"{headerAndHistory[^availableForHeaderAndHistory..]}{requestSection}";
        }

        var header = headerAndHistory[..historyIdx];
        var historySection = headerAndHistory[historyIdx..];
        var availableForHistory = availableForHeaderAndHistory - header.Length;

        if (availableForHistory <= 0)
        {
            return $"{header}[context_truncated]\n{requestSection}";
        }

        var trimmedHistory = historySection.Length <= availableForHistory
            ? historySection
            : historySection[^availableForHistory..];
        return $"{header}{trimmedHistory}{requestSection}";
    }

    private static IReadOnlyList<string> NormalizeExplicitMemoryNoteNames(IReadOnlyList<string>? names)
    {
        return MemoryNoteSelectionPolicy.NormalizeExplicitNames(names);
    }

    private bool ShouldUsePriorConversationContext(string conversationId, string input)
    {
        if (SearchQueryPolicy.LooksLikeStandaloneFreshGreeting(input))
        {
            return false;
        }

        if (ConversationContextPolicy.ShouldUsePriorConversationContext(input, out var isAmbiguous))
        {
            var normalized = (input ?? string.Empty).Trim();
            if (ConversationContextPolicy.LooksLikeExplicitStandaloneQuestion(normalized))
            {
                return HasTopicalOverlapWithRecentConversation(conversationId, normalized);
            }

            // 모호 키워드("어때", "괜찮" 등)는 토픽 오버랩이 있어야 history 로드.
            // 단, 초단문(≤15자) 판단/의견 요청("잘 돌아갈까?", "어때?")은
            // 토큰 오버랩이 거의 없어도 직전 대화가 있으면 무조건 history 로드.
            if (isAmbiguous)
            {
                if (normalized.Length <= 15 && HasAnyRecentAssistantMessage(conversationId))
                {
                    return true;
                }
                return HasTopicalOverlapWithRecentConversation(conversationId, input ?? string.Empty);
            }
            return true;
        }

        return HasTopicalOverlapWithRecentConversation(conversationId, input ?? string.Empty);
    }

    private bool HasAnyRecentAssistantMessage(string conversationId)
    {
        var thread = _conversationStore.Get(conversationId);
        return thread != null
               && thread.Messages.Any(m => m.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase));
    }

    private bool HasTopicalOverlapWithRecentConversation(string conversationId, string input)
    {
        var inputTokens = ConversationContextPolicy.ExtractContextTokens(input);
        if (inputTokens.Count == 0)
        {
            return false;
        }

        var thread = _conversationStore.Get(conversationId);
        if (thread == null || thread.Messages.Count == 0)
        {
            return false;
        }

        foreach (var message in thread.Messages
                     .OrderByDescending(item => item.CreatedUtc)
                     .Where(item => item.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
                     .Take(8))
        {
            var messageText = (message.Text ?? string.Empty).Trim();
            if (messageText.Length == 0)
            {
                continue;
            }

            if (messageText.Equals((input ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var messageTokens = ConversationContextPolicy.ExtractContextTokens(messageText);
            if (ConversationContextPolicy.HasMeaningfulTokenOverlap(inputTokens, messageTokens))
            {
                return true;
            }
        }

        return false;
    }

    private async Task EnsureConversationTitleFromFirstTurnAsync(
        string conversationId,
        string preferredProvider,
        string preferredModel,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var thread = _conversationStore.Get(conversationId);
            if (thread == null || !ConversationTitlePolicy.ShouldAutoTitle(thread))
            {
                return;
            }

            var isCodingScope = thread.Scope.Equals("coding", StringComparison.OrdinalIgnoreCase);
            var titleLooksFailed = ConversationTitlePolicy.IsLikelyProviderFailureText(thread.Title);
            var selectedUser = titleLooksFailed
                ? thread.Messages.LastOrDefault(x => x.Role.Equals("user", StringComparison.OrdinalIgnoreCase))?.Text?.Trim()
                : thread.Messages.FirstOrDefault(x => x.Role.Equals("user", StringComparison.OrdinalIgnoreCase))?.Text?.Trim();
            var selectedAssistant = titleLooksFailed
                ? ConversationTitlePolicy.SelectAssistantTextForAutoTitle(thread, preferLatest: true)
                : ConversationTitlePolicy.SelectAssistantTextForAutoTitle(thread, preferLatest: false);

            if (isCodingScope)
            {
                var localTitle = !string.IsNullOrWhiteSpace(selectedUser)
                    ? ConversationTitlePolicy.BuildFallbackConversationTitle(selectedUser)
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(localTitle))
                {
                    localTitle = ConversationTitlePolicy.BuildFallbackConversationTitleFromAssistant(selectedAssistant);
                }

                if (!string.IsNullOrWhiteSpace(localTitle))
                {
                    _conversationStore.UpdateTitle(conversationId, localTitle);
                }

                return;
            }

            if (string.IsNullOrWhiteSpace(selectedUser))
            {
                return;
            }

            var provider = NormalizeProvider(preferredProvider, allowAuto: true);
            if (provider == "auto")
            {
                provider = await ResolveAutoProviderAsync(cancellationToken);
            }

            var title = string.Empty;
            if (provider != "none" && !string.IsNullOrWhiteSpace(selectedAssistant))
            {
                var model = ResolveModel(provider, preferredModel);
                var prompt = $"""
                            아래 대화의 제목을 한국어 한 문장으로 만들어라.
                            조건:
                            - 최대 28자
                            - 불필요한 따옴표/머리말 금지
                            - 제목만 출력

                            [사용자]
                            {selectedUser}

                            [어시스턴트]
                            {ConversationTitlePolicy.TruncateForTitle(selectedAssistant)}
                            """;
                var generated = await GenerateByProviderAsync(provider, model, prompt, cancellationToken);
                if (!ConversationTitlePolicy.IsLikelyProviderFailureText(generated.Text))
                {
                    title = ConversationTitlePolicy.NormalizeConversationTitle(generated.Text);
                }
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                title = ConversationTitlePolicy.BuildFallbackConversationTitleFromAssistant(selectedAssistant);
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                title = ConversationTitlePolicy.BuildFallbackConversationTitle(selectedUser);
            }

            _conversationStore.UpdateTitle(conversationId, title);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[conversation] title auto-rename failed: {ex.Message}");
        }
    }

    private async Task<MemoryNoteSaveResult?> MaybeCompressConversationAsync(
        string conversationId,
        string modeKey,
        string preferredProvider,
        string preferredModel,
        CancellationToken cancellationToken
    )
    {
        var currentChars = _conversationStore.GetTotalCharacters(conversationId);
        if (currentChars < Math.Max(2000, _context.ConversationCompressChars))
        {
            return null;
        }

        var sourceText = _conversationStore.BuildCompressionSourceText(conversationId, _context.ConversationKeepRecentMessages);
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return null;
        }

        var provider = NormalizeProvider(preferredProvider, allowAuto: true);
        if (provider == "auto")
        {
            provider = await ResolveAutoProviderAsync(cancellationToken);
            if (provider == "none")
            {
                provider = "groq";
            }
        }

        var model = ResolveModel(provider, preferredModel);
        var summaryPrompt = $"""
                            다음은 길어진 대화의 이전 구간입니다.
                            이후 대화 이어가기에 필요한 핵심 맥락만 유지해서 한국어로 압축 요약하세요.
                            반드시 포함:
                            1) 사용자 목표
                            2) 결정된 기술 선택/제약
                            3) 아직 남은 작업
                            4) 파일/경로/명령 관련 중요 사실

                            [대화 로그]
                            {sourceText}
                            """;

        var summaryResult = await GenerateByProviderAsync(provider, model, summaryPrompt, cancellationToken);
        var summary = summaryResult.Text.Trim();
        if (string.IsNullOrWhiteSpace(summary))
        {
            summary = sourceText.Length > 2400 ? sourceText[..2400] + "\n...(truncated)" : sourceText;
        }

        var thread = _conversationStore.Get(conversationId);
        if (thread == null)
        {
            return null;
        }

        var saved = _memoryNoteStore.Save(
            modeKey,
            thread.Id,
            thread.Title,
            summaryResult.Provider,
            summaryResult.Model,
            summary
        );
        _conversationStore.AddLinkedMemoryNote(conversationId, saved.Name);
        _conversationStore.CompactWithSummary(
            conversationId,
            _context.ConversationKeepRecentMessages,
            $"자동 압축 완료. 메모리 노트 `{saved.Name}` 를 컨텍스트로 사용합니다."
        );
        return saved;
    }

    private void ScheduleConversationMaintenance(
        string conversationId,
        string modeKey,
        string preferredProvider,
        string preferredModel
    )
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var titleCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await EnsureConversationTitleFromFirstTurnAsync(
                    conversationId,
                    preferredProvider,
                    preferredModel,
                    titleCts.Token
                ).ConfigureAwait(false);

                using var compressCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                _ = await MaybeCompressConversationAsync(
                    conversationId,
                    modeKey,
                    preferredProvider,
                    preferredModel,
                    compressCts.Token
                ).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[conversation] maintenance failed: {ex.Message}");
            }
        });
    }

    private string ResolveModel(string provider, string? modelOverride)
    {
        var normalizedOverride = ProviderModelSelectionPolicy.NormalizePinnedProviderModelSelection(
            provider,
            modelOverride,
            DefaultCopilotModel,
            NormalizeModelSelection
        );
        if (!string.IsNullOrWhiteSpace(normalizedOverride))
        {
            return normalizedOverride;
        }

        return provider switch
        {
            "gemini" => _providers.GeminiModel,
            "cerebras" => _providers.CerebrasModel,
            "nvidia" => _providers.NvidiaModel,
            "copilot" => DefaultCopilotModel,
            "codex" => _providers.CodexModel,
            _ => _llmRouter.GetSelectedGroqModel()
        };
    }

    private static IReadOnlyList<string> MergeMemoryNoteNames(IReadOnlyList<string> baseNames, IReadOnlyList<string>? requestNames)
    {
        return MemoryNoteSelectionPolicy.MergeNames(baseNames, requestNames);
    }

    private async Task<LlmSingleChatResult?> TryFallbackFromGroqRateLimitAsync(string input, CancellationToken cancellationToken)
    {
        if (IsCopilotResponseTestPrompt(input))
        {
            var model = _copilotWrapper.GetSelectedModel();
            return new LlmSingleChatResult("copilot", model, BuildMockCopilotTestResponse(model));
        }

        if (_llmRouter.HasGeminiApiKey())
        {
            var gemini = await _llmRouter.GenerateGeminiChatAsync(input, cancellationToken);
            return new LlmSingleChatResult("gemini", _providers.GeminiModel, gemini);
        }

        var copilotStatus = await _copilotWrapper.GetStatusAsync(cancellationToken);
        if (copilotStatus.Installed && copilotStatus.Authenticated)
        {
            var model = _copilotWrapper.GetSelectedModel();
            var copilot = await _copilotWrapper.GenerateChatAsync(input, model, cancellationToken);
            return new LlmSingleChatResult("copilot", model, copilot);
        }

        return null;
    }

    private static string BuildCodingQualityBrief(string objective, string languageHint)
    {
        return CodingQualityBriefPolicy.Build(objective, languageHint, IsFrontendLikeCodingTask, IsGameLikeCodingTask);
    }

    private async Task<CodingWorkerResult> RunCodingWorkerAsync(
        string provider,
        string model,
        string prompt,
        string languageHint,
        CancellationToken cancellationToken,
        Action<CodingProgressUpdate>? progressCallback = null,
        string progressMode = "worker",
        bool allowRunActions = true,
        string role = "",
        string? workspaceRootOverride = null
    )
    {
        if (!allowRunActions)
        {
            return await RunDraftCodingWorkerAsync(
                provider,
                model,
                prompt,
                languageHint,
                cancellationToken,
                role
            );
        }

        var outcome = await RunAutonomousCodingLoopAsync(
            provider,
            model,
            prompt,
            languageHint,
            "worker",
            cancellationToken,
            progressCallback,
            progressMode,
            allowRunActions,
            workspaceRootOverride: workspaceRootOverride
        );
        return new CodingWorkerResult(
            provider,
            model,
            outcome.Language,
            outcome.Code,
            outcome.RawResponse + "\n\n[loop]\n" + outcome.Summary,
            outcome.Execution,
            outcome.ChangedFiles,
            role,
            outcome.Summary,
            outcome.TokenUsage
        );
    }

    private async Task<CodingWorkerResult> RunDraftCodingWorkerAsync(
        string provider,
        string model,
        string objective,
        string languageHint,
        CancellationToken cancellationToken,
        string role = ""
    )
    {
        var requestedPaths = ExtractRequestedCodingPaths(objective, languageHint);
        var profile = ResolveCodingExecutionProfile(provider, model, objective, languageHint, requestedPaths);
        var draftPrompt = BuildDraftCodingWorkerPrompt(objective, languageHint);
        var generated = await GenerateByProviderSafeAsync(
            provider,
            model,
            draftPrompt,
            cancellationToken,
            ResolveDraftGenerationMaxOutputTokens(profile),
            useRawCodexPrompt: true,
            optimizeCodexForCoding: profile.OptimizeCodexCli,
            timeoutOverrideSeconds: profile.RequestTimeoutSeconds
        );

        var rawResponse = (generated.Text ?? string.Empty).Trim();
        var parsed = ExtractFallbackCode(rawResponse, languageHint, objective);
        var draftTokenUsage = generated.TokenUsage;
        if (string.IsNullOrWhiteSpace(parsed.Code))
        {
            var codeOnlyPrompt = BuildFallbackCodeOnlyPrompt(objective, languageHint);
            var fallback = await GenerateByProviderSafeAsync(
                provider,
                model,
                codeOnlyPrompt,
                cancellationToken,
                ResolveDraftGenerationMaxOutputTokens(profile),
                useRawCodexPrompt: true,
                optimizeCodexForCoding: profile.OptimizeCodexCli,
                timeoutOverrideSeconds: profile.RequestTimeoutSeconds
            );
            var fallbackRaw = (fallback.Text ?? string.Empty).Trim();
            var fallbackParsed = ExtractFallbackCode(fallbackRaw, languageHint, objective);
            if (!string.IsNullOrWhiteSpace(fallbackParsed.Code))
            {
                parsed = fallbackParsed;
                rawResponse = string.IsNullOrWhiteSpace(rawResponse) ? fallbackRaw : $"{rawResponse}\n\n[code-only-fallback]\n{fallbackRaw}";
            }

            draftTokenUsage = TokenUsageEstimator.Combine(draftTokenUsage, fallback.TokenUsage);
        }

        var resolvedLanguage = string.IsNullOrWhiteSpace(parsed.Language)
            ? ResolveInitialCodingLanguage(languageHint, objective)
            : NormalizeLanguageForCode(parsed.Language);
        var execution = new CodeExecutionResult(
            resolvedLanguage,
            "-",
            "-",
            "(draft-only)",
            0,
            string.Empty,
            string.Empty,
            "skipped"
        );

        return new CodingWorkerResult(
            provider,
            model,
            resolvedLanguage,
            parsed.Code,
            string.IsNullOrWhiteSpace(rawResponse) ? "초안 생성 결과가 비어 있습니다." : rawResponse,
            execution,
            Array.Empty<string>(),
            role,
            string.IsNullOrWhiteSpace(rawResponse) ? "초안 생성 결과가 비어 있습니다." : TrimForOutput(rawResponse, 1800),
            draftTokenUsage
        );
    }

    private async Task<AutonomousCodingOutcome> RunAutonomousCodingLoopAsync(
        string provider,
        string model,
        string objective,
        string languageHint,
        string modeLabel,
        CancellationToken cancellationToken,
        Action<CodingProgressUpdate>? progressCallback = null,
        string? progressModeOverride = null,
        bool allowRunActions = true,
        string? workspaceRootOverride = null,
        int repairAttempt = 0
    )
    {
        var workspaceRoot = ResolveCodingWorkspaceRoot(workspaceRootOverride);
        var requestedPaths = ExtractRequestedCodingPaths(objective, languageHint);
        var profile = ResolveCodingExecutionProfile(provider, model, objective, languageHint, requestedPaths);
        var oneShotMode = ShouldUseOneShotMode(profile, objective, languageHint);
        var maxIterations = ResolveMaxIterations(profile, oneShotMode);
        var maxActions = ResolveMaxActions(profile, oneShotMode);
        var iterations = new List<string>();
        TokenUsage? totalTokenUsage = null;
        var progressMode = string.IsNullOrWhiteSpace(progressModeOverride) ? modeLabel : progressModeOverride;

        var currentLanguage = ResolveInitialCodingLanguage(languageHint, objective);
        var expectedOutput = ExtractExpectedConsoleOutput(objective);
        var lastCode = string.Empty;
        var lastWritePath = "-";
        var lastRawResponse = string.Empty;
        var changedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deferredRunCommand = string.Empty;
        var hasDeferredRunAction = false;
        var consecutivePlanParseFailures = 0;
        var consecutiveNoActionPlans = 0;
        var attemptedDirectRecovery = ShouldAttemptEarlyDirectRecovery(profile, objective, languageHint, requestedPaths);
        var lastExecution = new CodeExecutionResult(
            currentLanguage,
            workspaceRoot,
            "-",
            "(none)",
            0,
            "아직 실행 명령이 없습니다.",
            string.Empty,
            "skipped"
        );

        progressCallback?.Invoke(BuildCodingProgressUpdate(
            progressMode,
            provider,
            model,
            "start",
            "요청을 해석하고 작업 범위를 정리합니다.",
            0,
            maxIterations,
            3,
            false,
            "request",
            "요청 분석",
            BuildCodingObjectiveProgressDetail(objective, languageHint),
            1,
            VisibleCodingStageTotal
        ));

        var initialSnapshot = BuildWorkspaceSnapshot(workspaceRoot, profile);
        progressCallback?.Invoke(BuildCodingProgressUpdate(
            progressMode,
            provider,
            model,
            "scanning",
            "현재 작업공간 상태를 확인합니다.",
            0,
            maxIterations,
            10,
            false,
            "workspace",
            "작업공간 점검",
            BuildWorkspaceProgressDetail(initialSnapshot),
            2,
            VisibleCodingStageTotal
        ));

        var directRecovery = attemptedDirectRecovery
            ? await TryApplyProviderDirectRecoveryAsync(
                profile,
                provider,
                model,
                objective,
                languageHint,
                workspaceRoot,
                requestedPaths,
                cancellationToken,
                progressCallback,
                progressMode,
                maxIterations
            )
            : null;
        if (directRecovery != null)
        {
            progressCallback?.Invoke(BuildCodingProgressUpdate(
                progressMode,
                provider,
                model,
                "done",
                "코딩 작업이 완료되었습니다.",
                maxIterations,
                maxIterations,
                100,
                true,
                "verification",
                "최종 실행 및 검증",
                $"직생성 복구 적용 · 상태: {directRecovery.Execution.Status}",
                6,
                VisibleCodingStageTotal
            ));
            return directRecovery;
        }

        if (profile.AllowDeterministicStdoutFastPath
            && ShouldTryDeterministicSingleFileOutputRepair(objective, currentLanguage, requestedPaths, expectedOutput))
        {
            progressCallback?.Invoke(BuildCodingProgressUpdate(
                progressMode,
                provider,
                model,
                "planning",
                "단순 단일 파일 출력 요청이라 빠른 결정론적 경로를 적용합니다.",
                1,
                maxIterations,
                28,
                false,
                "planning",
                "구현 계획",
                "요청 파일을 직접 생성하고 stdout까지 바로 검증합니다.",
                3,
                VisibleCodingStageTotal
            ));

            var deterministicOutcome = await TryApplyDeterministicSingleFileOutputRepairAsync(
                objective,
                currentLanguage,
                workspaceRoot,
                requestedPaths,
                expectedOutput,
                cancellationToken
            );
            if (deterministicOutcome.Applied)
            {
                var changedPaths = new[] { deterministicOutcome.ChangedPath };
                var fastPathSummary = BuildAutonomousCodingSummary(
                    new[] { "deterministic_fast_path=single_file_output" },
                    changedPaths,
                    deterministicOutcome.Execution,
                    maxIterations
                );
                progressCallback?.Invoke(BuildCodingProgressUpdate(
                    progressMode,
                    provider,
                    model,
                    "done",
                    "코딩 작업이 완료되었습니다.",
                    maxIterations,
                    maxIterations,
                    100,
                    true,
                    "verification",
                    "최종 실행 및 검증",
                    $"최종 상태: {deterministicOutcome.Execution.Status} (exit={deterministicOutcome.Execution.ExitCode})",
                    6,
                    VisibleCodingStageTotal
                ));
                return new AutonomousCodingOutcome(
                    currentLanguage,
                    deterministicOutcome.Code,
                    "[deterministic-fast-path]",
                    deterministicOutcome.Execution,
                    changedPaths,
                    fastPathSummary
                );
            }
        }

        for (var i = 1; i <= maxIterations; i++)
        {
            var snapshot = i == 1 ? initialSnapshot : BuildWorkspaceSnapshot(workspaceRoot, profile);
            var recent = BuildRecentLoopLogs(iterations, profile);
            var loopPrompt = BuildCodingLoopPrompt(
                objective,
                languageHint,
                modeLabel,
                workspaceRoot,
                profile,
                oneShotMode,
                i,
                maxIterations,
                maxActions,
                snapshot,
                recent,
                lastExecution
            );

            var generated = await GenerateByProviderSafeAsync(
                provider,
                model,
                loopPrompt,
                cancellationToken,
                GetCodingPlanMaxOutputTokens(profile),
                useRawCodexPrompt: true,
                codexWorkingDirectoryOverride: workspaceRoot,
                optimizeCodexForCoding: profile.OptimizeCodexCli,
                timeoutOverrideSeconds: profile.RequestTimeoutSeconds
            );
            totalTokenUsage = TokenUsageEstimator.Combine(totalTokenUsage, generated.TokenUsage);
            lastRawResponse = generated.Text;
            var plan = ParseCodingLoopPlan(generated.Text);
            if (plan == null)
            {
                consecutivePlanParseFailures++;
                consecutiveNoActionPlans = 0;
                iterations.Add($"iter={i} plan_parse_failed");
                progressCallback?.Invoke(BuildCodingProgressUpdate(
                    progressMode,
                    provider,
                    model,
                    "retry",
                    $"반복 {i}/{maxIterations}: 계획 파싱 실패로 다음 반복에서 복구합니다.",
                    i,
                    maxIterations,
                    Math.Clamp((int)Math.Round((double)i / maxIterations * 55d), 18, 54),
                    false,
                    "planning",
                    "구현 계획",
                    "응답 형식을 다시 정렬한 뒤 계획을 재생성합니다.",
                    3,
                    VisibleCodingStageTotal
                ));
                if (consecutivePlanParseFailures >= 2)
                {
                    iterations.Add($"iter={i} plan_parse_abort");
                    break;
                }

                continue;
            }

            consecutivePlanParseFailures = 0;
            var actionResults = new List<string>();
            var actions = plan.Actions.Take(maxActions).ToArray();
            progressCallback?.Invoke(BuildCodingProgressUpdate(
                progressMode,
                provider,
                model,
                "planning",
                $"반복 {i}/{maxIterations}: 이번 구현 계획을 정리했습니다.",
                i,
                maxIterations,
                Math.Clamp((int)Math.Round((double)i / maxIterations * 45d), 16, 48),
                false,
                "planning",
                "구현 계획",
                BuildCodingPlanProgressDetail(plan, actions, actions.Any(action => string.Equals(action.Type, "run", StringComparison.OrdinalIgnoreCase))),
                3,
                VisibleCodingStageTotal
            ));
            if (actions.Length == 0)
            {
                consecutiveNoActionPlans++;
                actionResults.Add("actions=none");
                iterations.Add($"iter={i} analysis={TrimForOutput(plan.Analysis ?? string.Empty)} | {string.Join(" ; ", actionResults)}");
                progressCallback?.Invoke(BuildCodingProgressUpdate(
                    progressMode,
                    provider,
                    model,
                    "idle",
                    $"반복 {i}/{maxIterations}: 이번 반복에는 실행할 액션이 없습니다.",
                    i,
                    maxIterations,
                    Math.Clamp((int)Math.Round((double)i / maxIterations * 58d), 20, 60),
                    false,
                    "planning",
                    "구현 계획",
                    "생성된 계획에 실질 액션이 없어 다음 반복으로 넘어갑니다.",
                    3,
                    VisibleCodingStageTotal
                ));
                if (plan.Done)
                {
                    break;
                }

                if (consecutiveNoActionPlans >= 2)
                {
                    iterations.Add($"iter={i} actions_none_abort");
                    break;
                }

                continue;
            }

            consecutiveNoActionPlans = 0;
            var iterationChangedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var action in actions)
            {
                if (string.Equals(action.Type, "run", StringComparison.OrdinalIgnoreCase))
                {
                    var candidateCommand = CodingFallbackPolicy.NormalizeGeneratedRunCommand(action.Command);
                    if (string.IsNullOrWhiteSpace(candidateCommand))
                    {
                        actionResults.Add("run_deferred:empty_command");
                    }
                    else if (IsDangerousGeneratedRunCommand(candidateCommand))
                    {
                        actionResults.Add($"run_blocked_unsafe:{TrimForOutput(candidateCommand, 120)}");
                    }
                    else
                    {
                        deferredRunCommand = candidateCommand;
                        hasDeferredRunAction = true;
                        actionResults.Add($"run_deferred:{TrimForOutput(candidateCommand, 120)}");
                    }

                    continue;
                }

                var exec = await ExecuteCodingLoopActionAsync(action, workspaceRoot, requestedPaths, provider, cancellationToken);
                actionResults.Add(exec.Message);
                if (exec.Execution != null)
                {
                    lastExecution = exec.Execution;
                }

                if (!string.IsNullOrWhiteSpace(exec.LastWrittenFile))
                {
                    lastWritePath = exec.LastWrittenFile;
                }

                if (!string.IsNullOrWhiteSpace(exec.CodePreview))
                {
                    lastCode = exec.CodePreview;
                    currentLanguage = GuessLanguageFromPath(exec.LastWrittenFile, currentLanguage);
                }

                if (exec.Changed && !string.IsNullOrWhiteSpace(exec.ChangedPath))
                {
                    changedFiles.Add(exec.ChangedPath);
                    iterationChangedPaths.Add(exec.ChangedPath);
                }
            }

            iterations.Add($"iter={i} analysis={TrimForOutput(plan.Analysis ?? string.Empty)} | {string.Join(" ; ", actionResults)}");
            progressCallback?.Invoke(BuildCodingProgressUpdate(
                progressMode,
                provider,
                model,
                "writing",
                $"반복 {i}/{maxIterations}: 파일 생성/수정을 진행했습니다.",
                i,
                maxIterations,
                Math.Clamp((int)Math.Round((double)i / maxIterations * 78d), 28, 82),
                false,
                "writing",
                "파일 생성 및 수정",
                BuildCodingWriteProgressDetail(iterationChangedPaths, hasDeferredRunAction),
                4,
                VisibleCodingStageTotal
            ));
            if (plan.Done && (lastExecution.Status == "ok" || lastExecution.Status == "skipped"))
            {
                break;
            }
        }

        if (changedFiles.Count == 0)
        {
            progressCallback?.Invoke(BuildCodingProgressUpdate(
                progressMode,
                provider,
                model,
                "fallback",
                "변경 파일이 없어 복구 생성 경로를 시도합니다.",
                maxIterations,
                maxIterations,
                86,
                false,
                "recovery",
                "마무리 및 복구",
                "플랜 결과가 비어 있어 코드 블록 기반 복구 생성을 시도합니다.",
                5,
                VisibleCodingStageTotal
            ));

            var preferBundleFallback = !attemptedDirectRecovery && ShouldPreferFileBundleFallback(profile, objective, requestedPaths);
            var appliedBundleFallback = false;
            if (preferBundleFallback)
            {
                var bundlePrompt = BuildFallbackFileBundlePrompt(objective, currentLanguage, requestedPaths);
                var bundleGenerated = await GenerateByProviderSafeAsync(
                    provider,
                    model,
                    bundlePrompt,
                    cancellationToken,
                    ResolveDirectGenerationMaxOutputTokens(profile, bundleMode: true),
                    useRawCodexPrompt: true,
                    codexWorkingDirectoryOverride: workspaceRoot,
                    optimizeCodexForCoding: profile.OptimizeCodexCli,
                    timeoutOverrideSeconds: profile.RequestTimeoutSeconds
                );
                totalTokenUsage = TokenUsageEstimator.Combine(totalTokenUsage, bundleGenerated.TokenUsage);
                lastRawResponse = bundleGenerated.Text;
                var fallbackBundle = ExtractFallbackFileBundle(bundleGenerated.Text, currentLanguage, objective);
                if (fallbackBundle.Files.Count > 0)
                {
                    currentLanguage = fallbackBundle.Language;
                    foreach (var file in fallbackBundle.Files)
                    {
                        var normalizedContent = NormalizeProviderGeneratedFileContent(provider, file.Path, file.Content);
                        var writeAction = new CodingLoopAction("write_file", file.Path, normalizedContent, string.Empty);
                        var writeResult = await ExecuteCodingLoopActionAsync(writeAction, workspaceRoot, requestedPaths, provider, cancellationToken);
                        if (!string.IsNullOrWhiteSpace(writeResult.LastWrittenFile))
                        {
                            lastWritePath = writeResult.LastWrittenFile;
                        }

                        if (!string.IsNullOrWhiteSpace(writeResult.CodePreview))
                        {
                            lastCode = writeResult.CodePreview;
                        }

                        if (writeResult.Changed && !string.IsNullOrWhiteSpace(writeResult.ChangedPath))
                        {
                            changedFiles.Add(writeResult.ChangedPath);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(fallbackBundle.RunCommand))
                    {
                        deferredRunCommand = fallbackBundle.RunCommand;
                        hasDeferredRunAction = true;
                    }

                    iterations.Add($"fallback=bundle:{fallbackBundle.Files.Count}");
                    appliedBundleFallback = true;
                }
            }

            if (!appliedBundleFallback && !attemptedDirectRecovery)
            {
                var fallbackPrompt = BuildFallbackCodeOnlyPrompt(objective, currentLanguage);
                var fallbackGenerated = await GenerateByProviderSafeAsync(
                    provider,
                    model,
                    fallbackPrompt,
                    cancellationToken,
                    ResolveDirectGenerationMaxOutputTokens(profile, bundleMode: false),
                    useRawCodexPrompt: true,
                    codexWorkingDirectoryOverride: workspaceRoot,
                    optimizeCodexForCoding: profile.OptimizeCodexCli,
                    timeoutOverrideSeconds: profile.RequestTimeoutSeconds
                );
                totalTokenUsage = TokenUsageEstimator.Combine(totalTokenUsage, fallbackGenerated.TokenUsage);
                lastRawResponse = fallbackGenerated.Text;
                var fallbackCode = ExtractFallbackCode(fallbackGenerated.Text, currentLanguage, objective);
                if (!string.IsNullOrWhiteSpace(fallbackCode.Code))
                {
                    var fallbackPath = SuggestFallbackEntryPath(fallbackCode.Language, objective, requestedPaths);
                    var normalizedCode = NormalizeProviderGeneratedFileContent(provider, fallbackPath, fallbackCode.Code);
                    var writeAction = new CodingLoopAction("write_file", fallbackPath, normalizedCode, string.Empty);
                    var writeResult = await ExecuteCodingLoopActionAsync(writeAction, workspaceRoot, requestedPaths, provider, cancellationToken);
                    iterations.Add($"fallback=write:{writeResult.LastWrittenFile}");

                    if (!string.IsNullOrWhiteSpace(writeResult.LastWrittenFile))
                    {
                        lastWritePath = writeResult.LastWrittenFile;
                    }

                    if (!string.IsNullOrWhiteSpace(writeResult.CodePreview))
                    {
                        lastCode = writeResult.CodePreview;
                    }

                    currentLanguage = fallbackCode.Language;
                    if (writeResult.Changed && !string.IsNullOrWhiteSpace(writeResult.ChangedPath))
                    {
                        changedFiles.Add(writeResult.ChangedPath);
                    }
                }
                else if (!preferBundleFallback)
                {
                    var bundlePrompt = BuildFallbackFileBundlePrompt(objective, currentLanguage, requestedPaths);
                    var bundleGenerated = await GenerateByProviderSafeAsync(
                        provider,
                        model,
                        bundlePrompt,
                        cancellationToken,
                        ResolveDirectGenerationMaxOutputTokens(profile, bundleMode: true),
                        useRawCodexPrompt: true,
                        codexWorkingDirectoryOverride: workspaceRoot,
                        optimizeCodexForCoding: profile.OptimizeCodexCli,
                        timeoutOverrideSeconds: profile.RequestTimeoutSeconds
                    );
                    totalTokenUsage = TokenUsageEstimator.Combine(totalTokenUsage, bundleGenerated.TokenUsage);
                    lastRawResponse = string.IsNullOrWhiteSpace(bundleGenerated.Text)
                        ? fallbackGenerated.Text
                        : bundleGenerated.Text;
                    var fallbackBundle = ExtractFallbackFileBundle(bundleGenerated.Text, currentLanguage, objective);
                    if (fallbackBundle.Files.Count > 0)
                    {
                        currentLanguage = fallbackBundle.Language;
                        foreach (var file in fallbackBundle.Files)
                        {
                            var normalizedContent = NormalizeProviderGeneratedFileContent(provider, file.Path, file.Content);
                            var writeAction = new CodingLoopAction("write_file", file.Path, normalizedContent, string.Empty);
                            var writeResult = await ExecuteCodingLoopActionAsync(writeAction, workspaceRoot, requestedPaths, provider, cancellationToken);
                            if (!string.IsNullOrWhiteSpace(writeResult.LastWrittenFile))
                            {
                                lastWritePath = writeResult.LastWrittenFile;
                            }

                            if (!string.IsNullOrWhiteSpace(writeResult.CodePreview))
                            {
                                lastCode = writeResult.CodePreview;
                            }

                            if (writeResult.Changed && !string.IsNullOrWhiteSpace(writeResult.ChangedPath))
                            {
                                changedFiles.Add(writeResult.ChangedPath);
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(fallbackBundle.RunCommand))
                        {
                            deferredRunCommand = fallbackBundle.RunCommand;
                            hasDeferredRunAction = true;
                        }

                        iterations.Add($"fallback=bundle:{fallbackBundle.Files.Count}");
                    }
                    else
                    {
                        iterations.Add("fallback=no_code");
                    }
                }
                else
                {
                    iterations.Add("fallback=no_code");
                }
            }
            else if (!appliedBundleFallback && attemptedDirectRecovery)
            {
                iterations.Add("fallback=direct_recovery_already_attempted");
            }
        }

        if (changedFiles.Count == 0
            && profile.EnableGameScaffoldFallback
            && TryGenerateDeterministicWebShooterScaffold(objective, currentLanguage, out var shooterFiles))
        {
            foreach (var scaffold in shooterFiles)
            {
                var writeAction = new CodingLoopAction("write_file", scaffold.Path, scaffold.Content, string.Empty);
                var writeResult = await ExecuteCodingLoopActionAsync(writeAction, workspaceRoot, requestedPaths, provider, cancellationToken);
                if (writeResult.Changed && !string.IsNullOrWhiteSpace(writeResult.ChangedPath))
                {
                    changedFiles.Add(writeResult.ChangedPath);
                    lastWritePath = writeResult.LastWrittenFile;
                    lastCode = writeResult.CodePreview;
                }
            }

            if (changedFiles.Count > 0)
            {
                iterations.Add("fallback=scaffold:web_shooter");
                currentLanguage = "html";
            }
        }

        if (changedFiles.Count == 0 && TryGenerateDeterministicUiCloneScaffold(objective, workspaceRoot, out var scaffoldFiles))
        {
            foreach (var scaffold in scaffoldFiles)
            {
                var writeAction = new CodingLoopAction("write_file", scaffold.Path, scaffold.Content, string.Empty);
                var writeResult = await ExecuteCodingLoopActionAsync(writeAction, workspaceRoot, requestedPaths, provider, cancellationToken);
                if (writeResult.Changed && !string.IsNullOrWhiteSpace(writeResult.ChangedPath))
                {
                    changedFiles.Add(writeResult.ChangedPath);
                    lastWritePath = writeResult.LastWrittenFile;
                    lastCode = writeResult.CodePreview;
                }
            }

            iterations.Add($"fallback=scaffold:{string.Join(",", scaffoldFiles.Select(x => x.Path))}");
            currentLanguage = "html";
        }

        var workspaceRecoveredCount = MergeWorkspaceMaterializedFiles(workspaceRoot, changedFiles);
        if (workspaceRecoveredCount > 0)
        {
            var preferredRecoveredPath = requestedPaths
                .Select(path => ResolveWorkspacePath(workspaceRoot, path))
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                ?? changedFiles.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                ?? lastWritePath;
            if (!string.IsNullOrWhiteSpace(preferredRecoveredPath))
            {
                lastWritePath = preferredRecoveredPath;
                currentLanguage = GuessLanguageFromPath(lastWritePath, currentLanguage);
            }

            iterations.Add(changedFiles.Count == workspaceRecoveredCount
                ? $"workspace_scan_recovered={workspaceRecoveredCount}"
                : $"workspace_scan_merged={workspaceRecoveredCount}");
        }

        if (changedFiles.Count > 0)
        {
            currentLanguage = ResolveFinalCodingResultLanguage(currentLanguage, languageHint, objective, changedFiles);
            progressCallback?.Invoke(BuildCodingProgressUpdate(
                progressMode,
                provider,
                model,
                "finalizing",
                "최종 실행 전에 변경 사항을 정리합니다.",
                maxIterations,
                maxIterations,
                92,
                false,
                "recovery",
                "마무리 및 복구",
                $"{changedFiles.Count}개 파일 변경을 기준으로 마지막 검증 명령을 준비합니다.",
                5,
                VisibleCodingStageTotal
            ));
        }

        if (allowRunActions)
        {
            var shouldUseDeferredRunCommand = ShouldTrustDeferredVerificationCommand(currentLanguage, objective, deferredRunCommand);
            var expectedOutputLines = CodingExpectedOutputPolicy.ExtractExpectedConsoleOutputLines(objective);
            var finalDisplayCommand = !shouldUseDeferredRunCommand
                ? BuildVerificationDisplayCommand(currentLanguage, changedFiles, workspaceRoot, objective, requestedPaths, expectedOutput)
                : DescribeCommandWithExpectedOutput(CodingFallbackPolicy.NormalizeGeneratedRunCommand(deferredRunCommand), expectedOutput, expectedOutputLines);
            var finalCommand = !shouldUseDeferredRunCommand
                ? BuildVerificationCommand(currentLanguage, changedFiles, workspaceRoot, objective, requestedPaths, expectedOutput)
                : WrapCommandWithExpectedOutputAssertion(deferredRunCommand, expectedOutput, expectedOutputLines);
            if (!string.IsNullOrWhiteSpace(finalCommand))
            {
                progressCallback?.Invoke(BuildCodingProgressUpdate(
                    progressMode,
                    provider,
                    model,
                    "verifying",
                    "최종 실행 및 검증을 1회 수행합니다.",
                    maxIterations,
                    maxIterations,
                    97,
                    false,
                    "verification",
                    "최종 실행 및 검증",
                    $"실행 명령: {TrimForOutput(finalDisplayCommand, 180)}",
                    6,
                    VisibleCodingStageTotal
                ));
                var shell = await RunWorkspaceCommandWithAutoInstallAsync(finalCommand, workspaceRoot, cancellationToken);
                lastExecution = new CodeExecutionResult(
                    "bash",
                    workspaceRoot,
                    "-",
                    finalDisplayCommand,
                    shell.ExitCode,
                    shell.StdOut,
                    shell.StdErr,
                    shell.TimedOut ? "timeout" : (shell.ExitCode == 0 ? "ok" : "error")
                );
                lastExecution = ApplyCodingQualityGateToExecution(
                    objective,
                    currentLanguage,
                    workspaceRoot,
                    changedFiles,
                    lastExecution
                );
            }
        }

        if (allowRunActions
            && (changedFiles.Count == 0
                || !string.Equals(lastExecution.Status, "ok", StringComparison.OrdinalIgnoreCase)))
        {
            var structuredRepair = await TryApplyDeterministicStructuredMultiFileRepairAsync(
                objective,
                currentLanguage,
                workspaceRoot,
                requestedPaths,
                cancellationToken
            );
            if (structuredRepair.Applied)
            {
                foreach (var path in structuredRepair.ChangedPaths)
                {
                    changedFiles.Add(path);
                }

                lastWritePath = structuredRepair.ChangedPaths.FirstOrDefault() ?? lastWritePath;
                lastCode = structuredRepair.Code;
                currentLanguage = structuredRepair.Language;
                lastExecution = structuredRepair.Execution;
                iterations.Add("deterministic_repair=structured_multi_file");
                progressCallback?.Invoke(BuildCodingProgressUpdate(
                    progressMode,
                    provider,
                    model,
                    "repair",
                    string.Equals(lastExecution.Status, "ok", StringComparison.OrdinalIgnoreCase)
                        ? "다중 파일 실패를 결정론적으로 복구했습니다."
                        : changedFiles.Count == 0
                            ? "생성 파일이 없어 다중 파일 결정론적 복구를 시도했지만 아직 실패 상태입니다."
                            : "다중 파일 결정론적 복구를 시도했지만 아직 실패 상태입니다.",
                    maxIterations,
                    maxIterations,
                    99,
                    false,
                    "verification",
                    "최종 실행 및 검증",
                    TrimForOutput($"실행 명령: {lastExecution.Command}", 220),
                    6,
                    VisibleCodingStageTotal
                ));
            }
        }

        if (allowRunActions
            && !string.Equals(lastExecution.Status, "ok", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(lastExecution.Status, "skipped", StringComparison.OrdinalIgnoreCase))
        {
            var deterministicRepair = await TryApplyDeterministicSingleFileOutputRepairAsync(
                objective,
                currentLanguage,
                workspaceRoot,
                requestedPaths,
                expectedOutput,
                cancellationToken
            );
            if (deterministicRepair.Applied)
            {
                changedFiles.Add(deterministicRepair.ChangedPath);
                lastWritePath = deterministicRepair.ChangedPath;
                lastCode = deterministicRepair.Code;
                currentLanguage = GuessLanguageFromPath(deterministicRepair.ChangedPath, currentLanguage);
                lastExecution = deterministicRepair.Execution;
                iterations.Add("deterministic_repair=single_file_output");
                progressCallback?.Invoke(BuildCodingProgressUpdate(
                    progressMode,
                    provider,
                    model,
                    "repair",
                    string.Equals(lastExecution.Status, "ok", StringComparison.OrdinalIgnoreCase)
                        ? "단순 출력 파일 실패를 결정론적으로 복구했습니다."
                        : "단순 출력 파일 결정론적 복구를 시도했지만 아직 실패 상태입니다.",
                    maxIterations,
                    maxIterations,
                    99,
                    false,
                    "verification",
                    "최종 실행 및 검증",
                    TrimForOutput($"실행 명령: {lastExecution.Command}", 220),
                    6,
                    VisibleCodingStageTotal
                ));
            }
        }

        var orderedChangedFiles = changedFiles
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        orderedChangedFiles = CleanupRedundantSingleFileArtifacts(workspaceRoot, objective, requestedPaths, orderedChangedFiles);
        var summary = BuildAutonomousCodingSummary(iterations, orderedChangedFiles, lastExecution, maxIterations);

        if (allowRunActions
            && repairAttempt < ResolveMaxCodingRepairPasses(objective, currentLanguage)
            && orderedChangedFiles.Length > 0
            && !string.Equals(lastExecution.Status, "ok", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(lastExecution.Status, "skipped", StringComparison.OrdinalIgnoreCase))
        {
            progressCallback?.Invoke(BuildCodingProgressUpdate(
                progressMode,
                provider,
                model,
                "repair",
                "최종 검증 실패로 수정 반복을 한 번 더 수행합니다.",
                maxIterations,
                maxIterations,
                98,
                false,
                "verification",
                "최종 실행 및 검증",
                TrimForOutput($"실패 원인: {lastExecution.StdErr}", 220),
                6,
                VisibleCodingStageTotal
            ));

            var repairObjective = BuildCodingRepairObjectivePrompt(objective, currentLanguage, workspaceRoot, lastExecution, orderedChangedFiles);
            var repairOutcome = await RunAutonomousCodingLoopAsync(
                provider,
                model,
                repairObjective,
                currentLanguage,
                modeLabel,
                cancellationToken,
                progressCallback,
                progressModeOverride,
                allowRunActions,
                workspaceRoot,
                repairAttempt + 1
            );
            var mergedChangedFiles = orderedChangedFiles
                .Concat(repairOutcome.ChangedFiles ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            mergedChangedFiles = CleanupRedundantSingleFileArtifacts(workspaceRoot, objective, requestedPaths, mergedChangedFiles);
            var mergedRawResponse = string.IsNullOrWhiteSpace(lastRawResponse)
                ? repairOutcome.RawResponse
                : string.IsNullOrWhiteSpace(repairOutcome.RawResponse)
                    ? lastRawResponse
                    : $"{lastRawResponse}\n\n[repair-pass]\n{repairOutcome.RawResponse}";
            var mergedSummary = string.IsNullOrWhiteSpace(repairOutcome.Summary)
                ? summary
                : string.Equals(repairOutcome.Execution.Status, "ok", StringComparison.OrdinalIgnoreCase)
                    ? $"{repairOutcome.Summary}\n\n[repair-note]\n초기 최종 검증 실패를 1회 복구한 뒤 성공했습니다."
                    : $"{summary}\n\n[repair-pass]\n{repairOutcome.Summary}";
            return new AutonomousCodingOutcome(
                string.IsNullOrWhiteSpace(repairOutcome.Language) ? currentLanguage : repairOutcome.Language,
                string.IsNullOrWhiteSpace(repairOutcome.Code) ? lastCode : repairOutcome.Code,
                mergedRawResponse,
                repairOutcome.Execution,
                mergedChangedFiles,
                mergedSummary,
                TokenUsageEstimator.Combine(totalTokenUsage, repairOutcome.TokenUsage)
            );
        }

        if (File.Exists(lastWritePath))
        {
            try
            {
                lastCode = await File.ReadAllTextAsync(lastWritePath, cancellationToken);
            }
            catch
            {
            }
        }
        progressCallback?.Invoke(BuildCodingProgressUpdate(
            progressMode,
            provider,
            model,
            "done",
            "코딩 작업이 완료되었습니다.",
            maxIterations,
            maxIterations,
            100,
            true,
            "verification",
            "최종 실행 및 검증",
            $"최종 상태: {lastExecution.Status} (exit={lastExecution.ExitCode})",
            6,
            VisibleCodingStageTotal
        ));
        return new AutonomousCodingOutcome(currentLanguage, lastCode, lastRawResponse, lastExecution, orderedChangedFiles, summary, totalTokenUsage);
    }

    private static bool ShouldTrustDeferredVerificationCommand(string language, string objective, string command)
    {
        return CodingExecutionSafetyPolicy.ShouldTrustDeferredVerificationCommand(language, objective, command, IsFrontendLikeCodingTask);
    }

    private static bool IsInteractiveProgramObjective(string objective, string normalizedLanguage)
    {
        return CodingExecutionSafetyPolicy.IsInteractiveProgramObjective(objective, normalizedLanguage, IsFrontendLikeCodingTask);
    }

    private async Task<CodingLoopActionResult> ExecuteCodingLoopActionAsync(
        CodingLoopAction action,
        string workspaceRoot,
        IReadOnlyList<string> requestedPaths,
        string provider,
        CancellationToken cancellationToken
    )
    {
        return await CodingLoopActionExecutor.ExecuteAsync(
            action,
            workspaceRoot,
            requestedPaths,
            provider,
            ResolveActionPathOrFallback,
            ResolveWorkspacePath,
            NormalizeProviderGeneratedFileContent,
            async (command, root, token) =>
            {
                var shell = await RunWorkspaceCommandWithAutoInstallAsync(command, root, token);
                return new CodingLoopShellResult(shell.ExitCode, shell.StdOut, shell.StdErr, shell.TimedOut);
            },
            cancellationToken
        );
    }

    private static CodingLoopPlan? ParseCodingLoopPlan(string rawText)
    {
        return CodingLoopPlanParser.Parse(rawText);
    }

    private static IReadOnlyList<string> BuildCodingPlanTextVariants(string text)
    {
        return CodingLoopPlanParser.BuildTextVariants(text);
    }

    private static string NormalizeJsonCandidate(string candidate)
    {
        return CodingLoopPlanParser.NormalizeJsonCandidate(candidate);
    }

    private static string BuildFallbackCodeOnlyPrompt(string objective, string languageHint)
    {
        var resolvedLanguage = ResolveInitialCodingLanguage(languageHint, objective);
        var projectProfile = ResolveCodingProjectProfile(objective, languageHint);
        return CodingFallbackPolicy.BuildCodeOnlyPrompt(
            objective,
            resolvedLanguage,
            projectProfile.ProjectKind,
            BuildLanguagePromptRuleLines(string.Empty, string.Empty, resolvedLanguage, objective)
        );
    }

    private static string BuildFallbackFileBundlePrompt(
        string objective,
        string languageHint,
        IReadOnlyList<string>? requestedPaths = null
    )
    {
        var resolvedLanguage = ResolveInitialCodingLanguage(languageHint, objective);
        var projectProfile = ResolveCodingProjectProfile(objective, languageHint, requestedPaths);
        return CodingFallbackPolicy.BuildFileBundlePrompt(
            objective,
            resolvedLanguage,
            new CodingFallbackProjectProfile(
                projectProfile.ProjectKind,
                projectProfile.EntryFiles,
                projectProfile.BuildTool,
                projectProfile.TestTool,
                projectProfile.RunCommands
            ),
            BuildLanguagePromptRuleLines(string.Empty, string.Empty, resolvedLanguage, objective, requestedPaths),
            requestedPaths
        );
    }

    private static ParsedCode ExtractFallbackCode(string rawText, string languageHint, string objective)
    {
        return CodingFallbackPolicy.ExtractFallbackCode(rawText, languageHint, objective);
    }

    private static FallbackFileBundle ExtractFallbackFileBundle(string rawText, string languageHint, string objective)
    {
        return CodingFallbackPolicy.ExtractFallbackFileBundle(rawText, languageHint, objective);
    }

    private static string NormalizeGeneratedFileContent(string content)
    {
        return GeneratedCodeTextPolicy.NormalizeGeneratedFileContent(content);
    }

    private static ParsedCode ExtractLanguagePrefixedPlainCode(string text, string fallbackLanguage)
    {
        return GeneratedCodeTextPolicy.ExtractLanguagePrefixedPlainCode(text, fallbackLanguage);
    }

    private static string SuggestFallbackEntryPath(string language, string objective, IReadOnlyList<string>? requestedPaths = null)
    {
        return CodingFallbackPolicy.SuggestFallbackEntryPath(language, objective, requestedPaths);
    }

    private static string BuildCodingRepairObjectivePrompt(
        string objective,
        string languageHint,
        string workspaceRoot,
        CodeExecutionResult lastExecution,
        IReadOnlyCollection<string> changedFiles
    )
    {
        return CodingFallbackPolicy.BuildRepairObjectivePrompt(new CodingRepairObjectivePromptRequest(
            objective,
            languageHint,
            workspaceRoot,
            lastExecution,
            changedFiles,
            BuildLanguagePromptRuleLines(string.Empty, string.Empty, languageHint, objective ?? string.Empty),
            NormalizeLanguageForCode(languageHint) == "python" && IsInteractiveProgramObjective(objective ?? string.Empty, "python")
        ));
    }

    private static IReadOnlyList<string> ExtractRequestedCodingPaths(string objective, string languageHint)
    {
        return CodingFallbackPolicy.ExtractRequestedCodingPaths(objective, languageHint);
    }

    private static string SelectRequestedCodingPath(
        IReadOnlyList<string>? requestedPaths,
        string? languageHint,
        string? content
    )
    {
        return CodingFallbackPolicy.SelectRequestedCodingPath(requestedPaths, languageHint, content);
    }

    private static bool HasSingleFileIntent(string objective)
    {
        return CodingFallbackPolicy.HasSingleFileIntent(objective);
    }

    private static string ResolveFinalCodingResultLanguage(
        string currentLanguage,
        string languageHint,
        string objective,
        IReadOnlyCollection<string> changedFiles
    )
    {
        return CodingLanguagePolicy.ResolveFinalResultLanguage(currentLanguage, languageHint, objective, changedFiles);
    }

    private static string[] CleanupRedundantSingleFileArtifacts(
        string workspaceRoot,
        string objective,
        IReadOnlyList<string> requestedPaths,
        IReadOnlyList<string> changedFiles
    )
    {
        return CodingArtifactCleanupPolicy.CleanupRedundantSingleFileArtifacts(
            workspaceRoot,
            objective,
            requestedPaths,
            changedFiles,
            ResolveWorkspacePath
        );
    }

    private static string ExtractExpectedConsoleOutput(string objective)
    {
        return CodingFallbackPolicy.ExtractExpectedConsoleOutput(objective);
    }

    private static string ExtractLatestCodingRequestText(string objective)
    {
        return CodingLanguagePolicy.ExtractLatestCodingRequestText(objective);
    }

    private static bool ShouldTryDeterministicSingleFileOutputRepair(
        string objective,
        string languageHint,
        IReadOnlyList<string> requestedPaths,
        string expectedOutput
    )
    {
        return CodingDeterministicOutputRepairPolicy.ShouldTrySingleFileOutputRepair(languageHint, requestedPaths, expectedOutput);
    }

    private static bool ShouldPreferFileBundleFallback(string objective, IReadOnlyList<string>? requestedPaths)
    {
        var projectProfile = ResolveCodingProjectProfile(objective, "auto", requestedPaths);
        return CodingFallbackDecisionPolicy.ShouldPreferFileBundleFallback(
            objective,
            requestedPaths,
            IsExplicitSingleFileSimpleTask(objective, "auto", requestedPaths),
            projectProfile.PrefersMultiFile,
            includeExplicitMultiFileTextSignals: true
        );
    }

    private async Task<(bool Applied, string ChangedPath, string Code, CodeExecutionResult Execution)> TryApplyDeterministicSingleFileOutputRepairAsync(
        string objective,
        string languageHint,
        string workspaceRoot,
        IReadOnlyList<string> requestedPaths,
        string expectedOutput,
        CancellationToken cancellationToken
    )
    {
        if (!ShouldTryDeterministicSingleFileOutputRepair(objective, languageHint, requestedPaths, expectedOutput))
        {
            return (false, string.Empty, string.Empty, new CodeExecutionResult("bash", workspaceRoot, "-", "(skipped)", 0, string.Empty, string.Empty, "skipped"));
        }

        var requestedPath = requestedPaths[0];
        string fullPath;
        try
        {
            fullPath = ResolveWorkspacePath(workspaceRoot, requestedPath);
        }
        catch
        {
            return (false, string.Empty, string.Empty, new CodeExecutionResult("bash", workspaceRoot, "-", "(blocked unsafe path)", 1, string.Empty, "workspace 밖 경로는 코딩탭 자동 작업에서 사용할 수 없습니다.", "error"));
        }
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var code = CodingDeterministicOutputRepairPolicy.BuildPythonPrintCode(expectedOutput);
        await File.WriteAllTextAsync(fullPath, code, cancellationToken);

        var baseCommand = $"python3 {EscapeShellArg(requestedPath)}";
        var shell = await RunWorkspaceCommandWithAutoInstallAsync(
            WrapCommandWithVerificationAssertions(baseCommand, expectedOutput, new[] { fullPath }),
            workspaceRoot,
            cancellationToken
        );
        var execution = new CodeExecutionResult(
            "bash",
            workspaceRoot,
            "-",
            DescribeVerificationCommand(baseCommand, expectedOutput, new[] { fullPath }),
            shell.ExitCode,
            shell.StdOut,
            shell.StdErr,
            shell.TimedOut ? "timeout" : (shell.ExitCode == 0 ? "ok" : "error")
        );
        return (true, fullPath, code, execution);
    }

    private static bool TryGenerateDeterministicUiCloneScaffold(
        string objective,
        string workspaceRoot,
        out IReadOnlyList<ScaffoldFileSpec> files
    )
    {
        _ = workspaceRoot;
        return CodingDeterministicScaffoldPolicy.TryGenerateUiCloneScaffold(objective, out files);
    }

    private static bool TryGenerateDeterministicWebShooterScaffold(
        string objective,
        string languageHint,
        out IReadOnlyList<ScaffoldFileSpec> files
    )
    {
        return CodingDeterministicScaffoldPolicy.TryGenerateWebShooterScaffold(objective, languageHint, out files);
    }

    private static string SanitizePathSegment(string raw)
    {
        return CodingExecutionSafetyPolicy.SanitizePathSegment(raw);
    }

    private static bool IsDangerousGeneratedRunCommand(string? command)
    {
        return CodingExecutionSafetyPolicy.IsDangerousGeneratedRunCommand(command);
    }

    private const int VisibleCodingStageTotal = 6;
    private const int MaxCodingRepairPasses = 1;

    private static int ResolveMaxCodingRepairPasses(string objective, string languageHint)
    {
        return CodingLoopTuningPolicy.ResolveMaxRepairPasses(
            objective,
            languageHint,
            IsGameLikeCodingTask,
            IsFrontendLikeCodingTask,
            LooksLikeBrowserAppObjective,
            MaxCodingRepairPasses
        );
    }

    private static CodingProgressUpdate BuildCodingProgressUpdate(
        string progressMode,
        string provider,
        string model,
        string phase,
        string message,
        int iteration,
        int maxIterations,
        int percent,
        bool done,
        string stageKey = "",
        string stageTitle = "",
        string stageDetail = "",
        int stageIndex = 0,
        int stageTotal = 0
    )
    {
        return CodingProgressPolicy.BuildUpdate(
            progressMode,
            provider,
            model,
            phase,
            message,
            iteration,
            maxIterations,
            percent,
            done,
            stageKey,
            stageTitle,
            stageDetail,
            stageIndex,
            stageTotal
        );
    }

    private static string BuildCodingObjectiveProgressDetail(string objective, string languageHint)
    {
        return CodingProgressPolicy.BuildObjectiveDetail(objective, languageHint);
    }

    private static string BuildWorkspaceProgressDetail(string workspaceSnapshot)
    {
        return CodingProgressPolicy.BuildWorkspaceDetail(workspaceSnapshot);
    }

    private static string BuildCodingPlanProgressDetail(CodingLoopPlan plan, IReadOnlyList<CodingLoopAction> actions, bool hasDeferredRunAction)
    {
        return CodingProgressPolicy.BuildPlanDetail(plan, actions, hasDeferredRunAction);
    }

    private static string BuildCodingWriteProgressDetail(IReadOnlyCollection<string> changedPaths, bool hasDeferredRunAction)
    {
        return CodingProgressPolicy.BuildWriteDetail(changedPaths, hasDeferredRunAction);
    }

    private string BuildCodingLoopPrompt(
        string objective,
        string languageHint,
        string modeLabel,
        string workspaceRoot,
        string provider,
        string model,
        bool oneShotMode,
        int iteration,
        int maxIterations,
        int maxActions,
        string workspaceSnapshot,
        string recentLogs,
        CodeExecutionResult lastExecution
    )
    {
        var resolvedLanguage = ResolveInitialCodingLanguage(languageHint, objective);
        return CodingPromptPolicy.BuildLoopPrompt(new CodingLoopPromptPolicyRequest(
            objective,
            resolvedLanguage,
            modeLabel,
            workspaceRoot,
            provider,
            model,
            oneShotMode,
            iteration,
            maxIterations,
            maxActions,
            workspaceSnapshot,
            recentLogs,
            lastExecution,
            BuildCodingQualityBrief(objective, resolvedLanguage),
            BuildProviderModelPromptRuleLines(provider, model),
            BuildLanguagePromptRuleLines(provider, model, resolvedLanguage, objective),
            BuildCodingVerificationRuleLines()
        ));
    }

    private static string BuildCodingAgentObjectivePrompt(string input, string languageHint, string modeLabel)
    {
        var resolvedLanguage = ResolveInitialCodingLanguage(languageHint, input);
        return CodingPromptPolicy.BuildAgentObjectivePrompt(
            input,
            modeLabel,
            resolvedLanguage,
            BuildLanguagePromptRuleLines(string.Empty, string.Empty, resolvedLanguage, input)
        );
    }

    private static string BuildDraftCodingWorkerPrompt(string objective, string languageHint)
    {
        var resolvedLanguage = ResolveInitialCodingLanguage(languageHint, objective);
        return CodingPromptPolicy.BuildDraftWorkerPrompt(
            objective,
            resolvedLanguage,
            BuildLanguagePromptRuleLines(string.Empty, string.Empty, resolvedLanguage, objective ?? string.Empty)
        );
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static string GuessLanguageFromPath(string path, string fallback)
    {
        return CodingLanguagePolicy.GuessLanguageFromPath(path, fallback);
    }

    private static string BuildOrchestrationCodingAggregatePrompt(
        string input,
        IReadOnlyList<CodingWorkerResult> workers,
        string languageHint
    )
    {
        return CodingPromptPolicy.BuildOrchestrationAggregatePrompt(input, workers, languageHint);
    }

    private static string BuildMultiCodingAggregatePrompt(
        string input,
        IReadOnlyList<CodingWorkerResult> workers,
        string languageHint
    )
    {
        return CodingPromptPolicy.BuildMultiAggregatePrompt(input, workers, languageHint);
    }

    private static string BuildMultiCodingSummaryPrompt(string originalInput, IReadOnlyList<CodingWorkerResult> workers)
    {
        var digests = workers
            .Select(worker => new CodingWorkerSummaryDigest(worker.Provider, worker.Model, BuildCodingWorkerDigest(worker)))
            .ToArray();
        return CodingPromptPolicy.BuildMultiSummaryPrompt(originalInput, digests);
    }

    private static string RemoveCodeBlocksFromText(string text)
    {
        return ChatOutputSanitizerPolicy.RemoveCodeBlocksFromText(text);
    }

    private async Task<IReadOnlyList<string>> GetAvailableProvidersAsync(CancellationToken cancellationToken)
    {
        return await _providerRegistry.GetAvailableProvidersAsync(cancellationToken);
    }

    private static string NormalizeLanguageForCode(string? language)
    {
        return CodingLanguagePolicy.NormalizeLanguageForCode(language);
    }

    private static string NormalizeCodingLanguageHintPreservingAuto(string? languageHint)
    {
        return CodingLanguagePolicy.NormalizeCodingLanguageHintPreservingAuto(languageHint);
    }

    private static string ResolveExplicitObjectiveLanguage(string? objective)
    {
        return CodingLanguagePolicy.ResolveExplicitObjectiveLanguage(objective);
    }

    private static string ResolveInitialCodingLanguage(string? languageHint, string objective)
    {
        return CodingLanguagePolicy.ResolveInitialCodingLanguage(languageHint, objective);
    }
}
