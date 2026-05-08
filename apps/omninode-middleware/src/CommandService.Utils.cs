using System.Diagnostics;
using System.Globalization;
using System.Net;
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
    private static readonly Regex MarkdownTableSeparatorCandidateRegex = new(
        @"^\|\s*[:\-\u2014\u2013\u2011\u2212\u2500\u2012]+\s*(\|\s*[:\-\u2014\u2013\u2011\u2212\u2500\u2012]+\s*)+\|$",
        RegexOptions.Compiled
    );
    private static readonly Regex MarkdownLooseSeparatorCellRegex = new(
        @"^:?-+:?$",
        RegexOptions.Compiled
    );
    private static readonly Regex RequestedCodingPathRegex = new(
        @"(?<path>(?:(?:[A-Za-z]:)?[\\/])?(?:[\w.-]+[\\/])*(?:[\w.-]+\.)+(?:json|html|java|tsx|jsx|mjs|cjs|cpp|cxx|hpp|htm|css|txt|md|py|ts|js|cs|kt|sh|cc|hh|h|c))(?![\w.-])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly Regex ExpectedOutputAfterQuotedRegex = new(
        "['\\\"`](?<value>[^'\\\"`\\r\\n]{1,160})['\\\"`]\\s*(?:를|을)?\\s*(?:출력|print|echo|표시)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly Regex ExpectedOutputBeforeQuotedRegex = new(
        "(?:출력|print|echo|표시)[^'\\\"`\\r\\n]{0,32}['\\\"`](?<value>[^'\\\"`\\r\\n]{1,160})['\\\"`]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly Regex GenericQuotedTextRegex = new(
        "['\\\"`](?<value>[^'\\\"`\\r\\n]{1,160})['\\\"`]",
        RegexOptions.Compiled
    );
    private static readonly Regex ExplicitShellExecutionCommandRegex = new(
        "(?<cmd>node|python3?|bash)\\s+(?<path>(?:\\.{0,2}[\\\\/])?(?:[\\w.-]+[\\\\/])*(?:[\\w.-]+\\.)+(?:py|js|mjs|cjs|sh))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly Regex ExplicitExecutionTargetRegex = new(
        "(?<path>(?:\\.{0,2}[\\\\/])?(?:[\\w.-]+[\\\\/])*(?:[\\w.-]+\\.)+(?:py|js|mjs|cjs|sh))\\s*(?:를|을)?\\s*(?:직접\\s*)?(?:실행(?:\\s*시|\\s*해서|\\s*해|\\s*결과|\\s*후)?|run\\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly HashSet<string> GenericCodingFallbackFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "main.py",
        "main.js",
        "main.c",
        "main.cpp",
        "Program.cs",
        "Main.java",
        "Main.kt",
        "run.sh",
        "index.html"
    };

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
        bool includeLocalTimeHint = false
    )
    {
        var includePriorContext = ShouldUsePriorConversationContext(conversationId, input);
        var explicitRequestNotes = NormalizeExplicitMemoryNoteNames(requestMemoryNotes);
        var autoLinkedNotes = includePriorContext
            ? _conversationStore.Get(conversationId)?.LinkedMemoryNotes ?? Array.Empty<string>()
            : Array.Empty<string>();
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
            var historyRaw = _conversationStore.BuildHistoryText(conversationId, _config.ConversationHistoryMessages);
            history = BuildBudgetedContextHistory(historyRaw, 5200);
        }

        var builder = new StringBuilder();
        builder.AppendLine("[컨텍스트 사용 규칙]");
        builder.AppendLine("- '새 요청'을 최우선으로 처리하세요.");
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

        if (includeLocalTimeHint && LooksLikeLocalDateTimeQuestion(input))
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

        var tail = contextual[^8000..];
        return $"[context_truncated]\n{tail}";
    }

    private static IReadOnlyList<string> NormalizeExplicitMemoryNoteNames(IReadOnlyList<string>? names)
    {
        if (names == null || names.Count == 0)
        {
            return Array.Empty<string>();
        }

        return names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private bool ShouldUsePriorConversationContext(string conversationId, string input)
    {
        if (ShouldUsePriorConversationContext(input))
        {
            return true;
        }

        return HasTopicalOverlapWithRecentConversation(conversationId, input);
    }

    private static bool ShouldUsePriorConversationContext(string input)
    {
        var normalized = Regex.Replace((input ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ");
        if (normalized.Length == 0)
        {
            return false;
        }

        if (ContainsAny(normalized, "아니", "그게 아니라", "그 말고", "말고", "라고", "라니까"))
        {
            return true;
        }

        if (LooksLikeCasualOrIdentityQuestion(normalized))
        {
            return false;
        }

        if (ContainsAny(
                normalized,
                "이전",
                "앞서",
                "아까",
                "방금",
                "위에서",
                "위 내용",
                "최근 대화",
                "대화 내용",
                "메모리",
                "기억",
                "그 답변",
                "그 내용",
                "그거",
                "그것",
                "그걸",
                "해당",
                "이어서",
                "계속",
                "다시",
                "더 자세히",
                "웹검색해서 찾아",
                "웹 검색해서 찾아",
                "찾아봐",
                "검색해봐",
                "search it",
                "look it up"))
        {
            return true;
        }

        return normalized.Length <= 80
            && ContainsAny(normalized, "그 ", "이 ", "저 ", "위 ", "앞 ", "방금", "아까", "이거", "저거");
    }

    private bool HasTopicalOverlapWithRecentConversation(string conversationId, string input)
    {
        var inputTokens = ExtractContextTokens(input);
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

            var messageTokens = ExtractContextTokens(messageText);
            if (HasMeaningfulTokenOverlap(inputTokens, messageTokens))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasMeaningfulTokenOverlap(
        IReadOnlySet<string> left,
        IReadOnlySet<string> right,
        int minimumShared = 2
    )
    {
        if (left.Count == 0 || right.Count == 0)
        {
            return false;
        }

        var shared = left.Where(right.Contains).ToArray();
        if (shared.Length == 0)
        {
            return false;
        }

        if (shared.Any(token => token.Length >= 5 || token.Any(char.IsDigit) || token.Contains('-', StringComparison.Ordinal)))
        {
            return true;
        }

        return shared.Length >= minimumShared;
    }

    private static IReadOnlySet<string> ExtractContextTokens(string text)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches((text ?? string.Empty).ToLowerInvariant(), @"[\p{L}\p{N}][\p{L}\p{N}._/-]*"))
        {
            var token = NormalizeContextToken(match.Value);
            if (IsContextStopToken(token))
            {
                continue;
            }

            tokens.Add(token);
        }

        return tokens;
    }

    private static string NormalizeContextToken(string token)
    {
        return (token ?? string.Empty)
            .Trim()
            .Trim('.', ',', ':', ';', '!', '?', '\'', '"', '`', '(', ')', '[', ']', '{', '}');
    }

    private static bool IsContextStopToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length <= 1)
        {
            return true;
        }

        return token is
            "이" or "그" or "저" or "것" or "거" or "수" or "좀" or "왜" or "뭐" or "무엇" or
            "모델" or "설명" or "대해" or "대한" or "관련" or "알려줘" or "해줘" or "줘" or
            "답변" or "질문" or "요청" or "다음" or "현재" or "오늘" or "최신" or "정보" or
            "the" or "a" or "an" or "and" or "or" or "to" or "of" or "for" or "with" or "in" or
            "on" or "about" or "what" or "why" or "how" or "is" or "are" or "it" or "this" or "that";
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
            if (thread == null || !ShouldAutoTitle(thread))
            {
                return;
            }

            var isCodingScope = thread.Scope.Equals("coding", StringComparison.OrdinalIgnoreCase);
            var titleLooksFailed = IsLikelyProviderFailureText(thread.Title);
            var selectedUser = titleLooksFailed
                ? thread.Messages.LastOrDefault(x => x.Role.Equals("user", StringComparison.OrdinalIgnoreCase))?.Text?.Trim()
                : thread.Messages.FirstOrDefault(x => x.Role.Equals("user", StringComparison.OrdinalIgnoreCase))?.Text?.Trim();
            var selectedAssistant = titleLooksFailed
                ? SelectAssistantTextForAutoTitle(thread, preferLatest: true)
                : SelectAssistantTextForAutoTitle(thread, preferLatest: false);

            if (isCodingScope)
            {
                var localTitle = !string.IsNullOrWhiteSpace(selectedUser)
                    ? BuildFallbackConversationTitle(selectedUser)
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(localTitle))
                {
                    localTitle = BuildFallbackConversationTitleFromAssistant(selectedAssistant);
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
                            {TruncateForTitle(selectedAssistant)}
                            """;
                var generated = await GenerateByProviderAsync(provider, model, prompt, cancellationToken);
                if (!IsLikelyProviderFailureText(generated.Text))
                {
                    title = NormalizeConversationTitle(generated.Text);
                }
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                title = BuildFallbackConversationTitleFromAssistant(selectedAssistant);
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                title = BuildFallbackConversationTitle(selectedUser);
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
        if (currentChars < Math.Max(2000, _config.ConversationCompressChars))
        {
            return null;
        }

        var sourceText = _conversationStore.BuildCompressionSourceText(conversationId, _config.ConversationKeepRecentMessages);
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
            _config.ConversationKeepRecentMessages,
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

                using var compressCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
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

    private static bool IsPinnedCopilotProvider(string? provider)
    {
        return string.Equals((provider ?? string.Empty).Trim(), "copilot", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizePinnedProviderModelSelection(string provider, string? modelOverride)
    {
        if (IsPinnedCopilotProvider(provider))
        {
            return DefaultCopilotModel;
        }

        return NormalizeModelSelection(modelOverride);
    }

    private static bool IsPinnedCopilotModel(string provider, string model)
    {
        return IsPinnedCopilotProvider(provider)
            && string.Equals((model ?? string.Empty).Trim(), DefaultCopilotModel, StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveModel(string provider, string? modelOverride)
    {
        var normalizedOverride = NormalizePinnedProviderModelSelection(provider, modelOverride);
        if (!string.IsNullOrWhiteSpace(normalizedOverride))
        {
            return normalizedOverride;
        }

        return provider switch
        {
            "gemini" => _config.GeminiModel,
            "cerebras" => _config.CerebrasModel,
            "nvidia" => _config.NvidiaModel,
            "copilot" => DefaultCopilotModel,
            "codex" => _config.CodexModel,
            _ => _llmRouter.GetSelectedGroqModel()
        };
    }

    private static IReadOnlyList<string> MergeMemoryNoteNames(IReadOnlyList<string> baseNames, IReadOnlyList<string>? requestNames)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in baseNames)
        {
            if (!string.IsNullOrWhiteSpace(item))
            {
                set.Add(item.Trim());
            }
        }

        if (requestNames != null)
        {
            foreach (var item in requestNames)
            {
                if (!string.IsNullOrWhiteSpace(item))
                {
                    set.Add(item.Trim());
                }
            }
        }

        return set.ToArray();
    }

    private static bool ShouldAutoTitle(ConversationThreadView thread)
    {
        var userCount = thread.Messages.Count(x => x.Role.Equals("user", StringComparison.OrdinalIgnoreCase));
        var assistantCount = thread.Messages.Count(x => x.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase));
        if (userCount < 1 || assistantCount < 1)
        {
            return false;
        }

        var title = (thread.Title ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            return true;
        }

        if (IsLikelyProviderFailureText(title))
        {
            return true;
        }

        if (userCount != 1)
        {
            return false;
        }

        var defaultPrefix = $"{thread.Scope}-{thread.Mode}";
        if (title.StartsWith(defaultPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (title.StartsWith("새 대화", StringComparison.OrdinalIgnoreCase)
            || title.StartsWith("new chat", StringComparison.OrdinalIgnoreCase)
            || title.StartsWith("chat-", StringComparison.OrdinalIgnoreCase)
            || title.StartsWith("coding-", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string NormalizeConversationTitle(string raw)
    {
        var value = (raw ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var line = value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? value;
        line = LeadingTitleNoiseRegex.Replace(line, string.Empty).Trim();
        line = line.Trim('"', '\'', '`', '“', '”', '‘', '’');
        if (line.Length > 38)
        {
            line = line[..38].TrimEnd();
        }

        return line;
    }

    private static string BuildFallbackConversationTitle(string firstUser)
    {
        var normalized = (firstUser ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "새 대화";
        }

        return normalized.Length <= 32 ? normalized : normalized[..32].TrimEnd() + "...";
    }

    private static string BuildFallbackConversationTitleFromAssistant(string assistantText)
    {
        var normalized = (assistantText ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        if (string.IsNullOrWhiteSpace(normalized) || IsLikelyProviderFailureText(normalized))
        {
            return string.Empty;
        }

        normalized = Regex.Replace(normalized, @"\[(?<text>[^\]]+)\]\((?<url>https?://[^)]+)\)", "${text}");
        normalized = Regex.Replace(normalized, @"https?://\S+", string.Empty);
        normalized = normalized.Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("__", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal);
        normalized = Regex.Replace(normalized, @"^(요약|핵심|출처)\s*:\s*", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"^(?:[-•▪*]\s+|\d+[.)]\s+)", string.Empty);
        normalized = Regex.Replace(normalized, @"\s{2,}", " ").Trim().Trim('"', '\'', '`', '“', '”', '‘', '’');
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var sentenceBoundary = normalized.IndexOfAny(new[] { '.', '!', '?', ':', ';' });
        if (sentenceBoundary > 8)
        {
            normalized = normalized[..sentenceBoundary].Trim();
        }

        return normalized.Length <= 32 ? normalized : normalized[..32].TrimEnd() + "...";
    }

    private static string TruncateForTitle(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return normalized.Length <= 260 ? normalized : normalized[..260] + "...";
    }

    private static string SelectAssistantTextForAutoTitle(ConversationThreadView thread, bool preferLatest)
    {
        var assistantMessages = thread.Messages
            .Where(x => x.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
            .Select(x => (x.Text ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        if (assistantMessages.Count == 0)
        {
            return string.Empty;
        }

        var candidatePool = preferLatest
            ? assistantMessages.AsEnumerable().Reverse()
            : assistantMessages.AsEnumerable();
        var usable = candidatePool.FirstOrDefault(x => !IsLikelyProviderFailureText(x));
        if (!string.IsNullOrWhiteSpace(usable))
        {
            return usable;
        }

        return preferLatest ? assistantMessages[^1] : assistantMessages[0];
    }

    private static bool IsLikelyProviderFailureText(string? text)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        var lowered = normalized.ToLowerInvariant();
        if (lowered.Contains("the operation was canceled", StringComparison.Ordinal)
            || lowered.Contains("the operation was cancelled", StringComparison.Ordinal))
        {
            return true;
        }

        if (lowered.EndsWith("api 키가 설정되지 않았습니다.", StringComparison.Ordinal)
            || lowered.EndsWith("인증이 필요합니다.", StringComparison.Ordinal)
            || lowered.EndsWith("응답이 비어 있습니다.", StringComparison.Ordinal)
            || lowered.EndsWith("응답이 비어 있습니다. 다시 질문해 주세요.", StringComparison.Ordinal)
            || lowered.Contains("응답 시간이 초과되었습니다.", StringComparison.Ordinal))
        {
            return true;
        }

        return lowered.StartsWith("groq 호출 오류:", StringComparison.Ordinal)
            || lowered.StartsWith("gemini 호출 오류:", StringComparison.Ordinal)
            || lowered.StartsWith("cerebras 호출 오류:", StringComparison.Ordinal)
            || lowered.StartsWith("nvidia 호출 오류:", StringComparison.Ordinal)
            || lowered.StartsWith("nvidia nim 호출 오류:", StringComparison.Ordinal)
            || lowered.StartsWith("copilot 호출 오류:", StringComparison.Ordinal)
            || lowered.StartsWith("groq 요청 실패:", StringComparison.Ordinal)
            || lowered.StartsWith("gemini 요청 실패:", StringComparison.Ordinal)
            || lowered.StartsWith("cerebras 요청 실패:", StringComparison.Ordinal)
            || lowered.StartsWith("nvidia 요청 실패:", StringComparison.Ordinal)
            || lowered.StartsWith("nvidia nim 요청 실패:", StringComparison.Ordinal)
            || lowered.StartsWith("copilot 요청 실패:", StringComparison.Ordinal)
            || lowered.StartsWith("gemini 웹검색 호출 오류:", StringComparison.Ordinal)
            || lowered.StartsWith("gemini 웹검색 요청 실패:", StringComparison.Ordinal)
            || lowered.StartsWith("gemini 웹검색 응답 시간이 초과되었습니다.", StringComparison.Ordinal)
            || lowered.StartsWith("현재 groq 요청 한도를 초과했습니다.", StringComparison.Ordinal);
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
            return new LlmSingleChatResult("gemini", _config.GeminiModel, gemini);
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

    private static bool IsGroqRateLimitResponse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Contains("429", StringComparison.Ordinal)
               || text.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
               || text.Contains("too many requests", StringComparison.OrdinalIgnoreCase)
               || text.Contains("요청 한도", StringComparison.Ordinal)
               || text.Contains("한도", StringComparison.Ordinal);
    }

    private static bool IsGroqMaxTokensResponse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var lowered = text.ToLowerInvariant();
        return lowered.Contains("max_tokens", StringComparison.Ordinal)
               && (lowered.Contains("less than or equal", StringComparison.Ordinal)
                   || lowered.Contains("maximum value", StringComparison.Ordinal));
    }

    private static string TrimContextHistory(string history, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(history))
        {
            return string.Empty;
        }

        var lines = history
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line =>
            {
                var normalized = line.Replace("\r", " ", StringComparison.Ordinal).Trim();
                return normalized.Length <= 360 ? normalized : normalized[..360] + "...";
            })
            .ToArray();
        var combined = string.Join('\n', lines);
        if (combined.Length <= maxChars)
        {
            return combined;
        }

        return combined[^maxChars..];
    }

    private static string BuildCodingQualityBrief(string objective, string languageHint)
    {
        var normalizedObjective = (objective ?? string.Empty).Trim();
        var language = ResolveInitialCodingLanguage(languageHint, normalizedObjective);
        var requestedPaths = ExtractRequestedCodingPaths(normalizedObjective, language);
        var expectedOutput = ExtractExpectedConsoleOutput(normalizedObjective);
        var builder = new StringBuilder();
        builder.AppendLine($"- language={language}");
        if (requestedPaths.Count > 0)
        {
            builder.AppendLine($"- required_files={string.Join(", ", requestedPaths.Take(8))}");
        }
        else
        {
            builder.AppendLine("- required_files=요청에서 명시한 파일이 없으면 표준 엔트리 파일을 만들 것");
        }

        if (!string.IsNullOrWhiteSpace(expectedOutput))
        {
            builder.AppendLine($"- expected_stdout={expectedOutput}");
        }

        if (IsFrontendLikeCodingTask(normalizedObjective, language))
        {
            builder.AppendLine("- ui_acceptance=첫 화면에 실제 DOM 콘텐츠가 보이고 주요 조작이 동작해야 함");
            builder.AppendLine("- file_boundary=index.html/styles.css/app.js 같은 실행 가능한 브라우저 파일 구조 우선");
        }
        else if (IsGameLikeCodingTask(normalizedObjective, language))
        {
            builder.AppendLine("- game_acceptance=입력 처리, 상태 갱신, 실패/승리 조건이 있는 실제 루프 필요");
        }
        else
        {
            builder.AppendLine("- acceptance=실행 가능, 요청 출력/동작 충족, 불필요한 더미/TODO 없음");
        }

        builder.AppendLine("- verification=마지막 액션은 가능한 실제 실행/검증이어야 하며 실패하면 원인을 수정할 것");
        return builder.ToString().Trim();
    }

    private static string BuildBudgetedContextHistory(string history, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(history))
        {
            return string.Empty;
        }

        var lines = history
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line =>
            {
                var normalized = line.Replace("\r", " ", StringComparison.Ordinal).Trim();
                return normalized.Length <= 360 ? normalized : normalized[..360] + "...";
            })
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        if (lines.Length == 0)
        {
            return string.Empty;
        }

        var recentCount = Math.Min(lines.Length, 8);
        var olderCount = Math.Max(0, lines.Length - recentCount);
        var recent = string.Join('\n', lines.Skip(olderCount));
        if (olderCount == 0)
        {
            return TrimContextHistory(recent, maxChars);
        }

        var olderSummary = BuildExtractiveHistorySummary(lines.Take(olderCount).ToArray(), 1200);
        var combined = string.IsNullOrWhiteSpace(olderSummary)
            ? recent
            : $"[이전 대화 압축]\n{olderSummary}\n\n[최근 턴]\n{recent}";
        return combined.Length <= maxChars ? combined : TrimContextHistory(combined, maxChars);
    }

    private static string BuildExtractiveHistorySummary(IReadOnlyList<string> olderLines, int maxChars)
    {
        if (olderLines.Count == 0)
        {
            return string.Empty;
        }

        var selected = new List<string>();
        foreach (var line in olderLines)
        {
            if (!IsHighSignalHistoryLine(line))
            {
                continue;
            }

            selected.Add(line.Length <= 220 ? line : line[..220] + "...");
            if (selected.Count >= 6)
            {
                break;
            }
        }

        if (selected.Count == 0)
        {
            selected.AddRange(olderLines.TakeLast(Math.Min(4, olderLines.Count)).Select(line => line.Length <= 220 ? line : line[..220] + "..."));
        }

        var summary = string.Join('\n', selected);
        return summary.Length <= maxChars ? summary : summary[..maxChars].TrimEnd() + "...";
    }

    private static bool IsHighSignalHistoryLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var lowered = line.ToLowerInvariant();
        return lowered.Contains("요구", StringComparison.Ordinal)
               || lowered.Contains("수정", StringComparison.Ordinal)
               || lowered.Contains("오류", StringComparison.Ordinal)
               || lowered.Contains("결정", StringComparison.Ordinal)
               || lowered.Contains("파일", StringComparison.Ordinal)
               || lowered.Contains("설정", StringComparison.Ordinal)
               || lowered.Contains("스킬", StringComparison.Ordinal)
               || lowered.Contains("think+", StringComparison.Ordinal)
               || lowered.Contains("nvidia", StringComparison.Ordinal)
               || lowered.Contains("error", StringComparison.Ordinal)
               || lowered.Contains("fix", StringComparison.Ordinal)
               || lowered.Contains("bug", StringComparison.Ordinal)
               || lowered.Contains("todo", StringComparison.Ordinal)
               || lowered.Contains("requirement", StringComparison.Ordinal);
    }

    private static string SanitizeChatOutput(string text, bool keepMarkdownTables = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "응답이 비어 있습니다. 다시 질문해 주세요.";
        }

        var normalized = WebUtility.HtmlDecode(text ?? string.Empty);
        normalized = normalized.Replace('\u00A0', ' ');
        normalized = normalized.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal).Trim();
        normalized = ThinkTagBlockRegex.Replace(normalized, string.Empty);
        normalized = RemoveCopilotMetaPreamble(normalized);
        var lines = normalized.Split('\n');
        var compact = new List<string>(lines.Length);
        string? previous = null;
        var repeatCount = 0;
        var inThinkBlock = false;
        var inCodeFence = false;
        var hadBlankLine = false;
        foreach (var raw in lines)
        {
            var rawLine = raw ?? string.Empty;
            if (rawLine.Contains("<think", StringComparison.OrdinalIgnoreCase))
            {
                inThinkBlock = true;
            }

            if (inThinkBlock)
            {
                if (rawLine.Contains("</think>", StringComparison.OrdinalIgnoreCase))
                {
                    inThinkBlock = false;
                }

                continue;
            }

            var line = ThinkTagInlineRegex.Replace(rawLine, string.Empty);
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith("```", StringComparison.Ordinal))
            {
                inCodeFence = !inCodeFence;
                if (compact.Count > 0 && !string.IsNullOrWhiteSpace(compact[^1]) && hadBlankLine)
                {
                    compact.Add(string.Empty);
                }

                compact.Add(trimmedLine);
                previous = null;
                repeatCount = 0;
                hadBlankLine = false;
                continue;
            }

            if (!inCodeFence)
            {
                line = NormalizeDisplayLine(line);
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                hadBlankLine = true;
                continue;
            }

            if (ShouldDropMetaLine(line))
            {
                continue;
            }

            if (hadBlankLine && compact.Count > 0 && !string.IsNullOrWhiteSpace(compact[^1]))
            {
                compact.Add(string.Empty);
            }
            hadBlankLine = false;

            if (previous != null && line.Equals(previous, StringComparison.Ordinal))
            {
                repeatCount += 1;
                if (repeatCount >= 2)
                {
                    continue;
                }
            }
            else
            {
                previous = line;
                repeatCount = 0;
            }

            compact.Add(line);
        }

        var merged = string.Join('\n', compact);
        if (merged.Length == 0)
        {
            merged = normalized;
        }

        merged = NormalizeMarkdownTableSeparators(merged);
        merged = CollapseMarkdownTableBlankLines(merged);
        merged = UnwrapMarkdownTableCodeFences(merged);
        if (!keepMarkdownTables)
        {
            merged = ConvertMarkdownTableRowsToList(merged);
        }
        var markdownLike = IsLikelyMarkdownText(merged);
        if (!markdownLike)
        {
            merged = ImprovePlainTextLineBreaksForChat(merged);
            merged = RepeatedChunkRegex.Replace(merged, "$1 ...");
            merged = CollapseRepeatedCharacters(merged);
        }
        else
        {
            merged = Regex.Replace(merged, @"\n{3,}", "\n\n");
        }

        merged = NormalizeSourceBlockToSingleLine(merged);
        merged = NormalizeStructuredLabelBlocks(merged);
        merged = RemoveDanglingMarkdownBoldMarkers(merged);
        return merged;
    }

    private static string RemoveDanglingMarkdownBoldMarkers(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n', StringSplitOptions.None);
        for (var i = 0; i < lines.Length; i += 1)
        {
            var line = lines[i] ?? string.Empty;
            var trimmed = line.Trim();
            if (trimmed.Length == 0
                || trimmed.StartsWith("```", StringComparison.Ordinal)
                || IsMarkdownTableRow(trimmed))
            {
                continue;
            }

            if (Regex.Matches(line, Regex.Escape("**"), RegexOptions.CultureInvariant).Count % 2 != 0)
            {
                line = line.Replace("**", string.Empty, StringComparison.Ordinal);
            }

            if (Regex.Matches(line, Regex.Escape("__"), RegexOptions.CultureInvariant).Count % 2 != 0)
            {
                line = line.Replace("__", string.Empty, StringComparison.Ordinal);
            }

            lines[i] = line;
        }

        return string.Join('\n', lines).Trim();
    }

    private static string NormalizeSourceBlockToSingleLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n', StringSplitOptions.None);
        var output = new List<string>(lines.Length);

        for (var index = 0; index < lines.Length; index += 1)
        {
            var current = lines[index] ?? string.Empty;
            var currentTrimmed = current.Trim();
            if (!Regex.IsMatch(currentTrimmed, @"^(출처|sources?)\s*:?\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
            {
                output.Add(current);
                continue;
            }

            var sourceNames = new List<string>(8);
            var cursor = index + 1;
            while (cursor < lines.Length)
            {
                var nextTrimmed = (lines[cursor] ?? string.Empty).Trim();
                if (nextTrimmed.Length == 0)
                {
                    // 출처 블록 내 공백 줄은 허용하되, 다음 비어있지 않은 줄이 출처 후보가 아닐 때 종료한다.
                    var lookahead = cursor + 1;
                    while (lookahead < lines.Length && string.IsNullOrWhiteSpace(lines[lookahead]))
                    {
                        lookahead += 1;
                    }

                    if (lookahead >= lines.Length || !TryExtractSourceNameCandidateLine(lines[lookahead], out _))
                    {
                        break;
                    }

                    cursor += 1;
                    continue;
                }

                if (!TryExtractSourceNameCandidateLine(lines[cursor], out var sourceName))
                {
                    break;
                }

                sourceNames.Add(sourceName);
                cursor += 1;
            }

            if (sourceNames.Count == 0)
            {
                output.Add(currentTrimmed);
                continue;
            }

            var distinctNames = FilterVisibleDisplaySources(sourceNames)
                .ToArray();
            if (distinctNames.Length == 0)
            {
                index = cursor - 1;
                continue;
            }

            output.Add($"출처: {string.Join(", ", distinctNames)}");
            index = cursor - 1;
        }

        return Regex.Replace(string.Join('\n', output).Trim(), @"\n{3,}", "\n\n");
    }

    private static string NormalizeStructuredLabelBlocks(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        normalized = Regex.Replace(
            normalized,
            @"(?<=[.!?]|…)\s*(?=(?:[-•▪]\s*)?(?:(?:No\.\d+|\d+[.)])\s*)?[A-Za-z가-힣0-9(][A-Za-z가-힣0-9().&+_/\-·\s]{1,80}[:：](?:\s|$))",
            "\n\n",
            RegexOptions.CultureInvariant
        );
        normalized = Regex.Replace(
            normalized,
            @"(?im)^(?<head>(?:[-•▪]\s*)?(?:(?:No\.\d+|\d+[.)])\s*)?출처\s*링크\s*[:：]\s*[^\n]*?)\s+(?<url>https?://\S+)\s*$",
            "${head}\n${url}",
            RegexOptions.CultureInvariant
        );

        var lines = normalized.Split('\n', StringSplitOptions.None);
        var output = new List<string>(lines.Length + 8);
        foreach (var raw in lines)
        {
            var line = (raw ?? string.Empty).Trim();
            if (line.Length == 0)
            {
                if (output.Count > 0 && !string.IsNullOrWhiteSpace(output[^1]))
                {
                    output.Add(string.Empty);
                }
                continue;
            }

            if (line.StartsWith("```", StringComparison.Ordinal) || IsMarkdownTableRow(line))
            {
                output.Add(line);
                continue;
            }

            if (TryFormatStandaloneNumberedHeadlineLine(line, out var formattedHeadline))
            {
                if (output.Count > 0 && !string.IsNullOrWhiteSpace(output[^1]))
                {
                    output.Add(string.Empty);
                }

                output.Add(formattedHeadline);
                continue;
            }

            if (TryNormalizeExistingStructuredMarkdownLabelLine(line, out var normalizedExistingLabel))
            {
                if (output.Count > 0 && !string.IsNullOrWhiteSpace(output[^1]))
                {
                    output.Add(string.Empty);
                }

                output.Add(normalizedExistingLabel);
                continue;
            }

            if (TryFormatStructuredMarkdownLabelLine(line, out var formatted))
            {
                if (output.Count > 0 && !string.IsNullOrWhiteSpace(output[^1]))
                {
                    output.Add(string.Empty);
                }

                output.Add(formatted);
                continue;
            }

            output.Add(line);
        }

        return Regex.Replace(string.Join('\n', output).Trim(), @"\n{3,}", "\n\n");
    }

    private static bool TryFormatStandaloneNumberedHeadlineLine(string line, out string formatted)
    {
        formatted = string.Empty;
        var normalized = (line ?? string.Empty).Trim();
        if (normalized.Length == 0 || normalized.Contains("**", StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryExtractStandaloneNumberedHeadlineParts(
                normalized,
                allowWrappedBold: false,
                out var lead,
                out var body,
                out _))
        {
            return false;
        }

        formatted = lead.Length == 0
            ? $"**{body}**"
            : $"{lead}**{body}**";
        return true;
    }

    private static bool IsStandaloneNumberedHeadlineLine(string line)
    {
        return TryExtractStandaloneNumberedHeadlineParts(
            line,
            allowWrappedBold: true,
            out _,
            out _,
            out _);
    }

    private static bool TryExtractStandaloneNumberedHeadlineParts(
        string line,
        bool allowWrappedBold,
        out string lead,
        out string body,
        out string headline)
    {
        lead = string.Empty;
        body = string.Empty;
        headline = string.Empty;

        var normalized = (line ?? string.Empty).Trim();
        if (normalized.Length == 0
            || normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("[", StringComparison.Ordinal)
            || normalized.StartsWith("```", StringComparison.Ordinal)
            || IsMarkdownTableRow(normalized))
        {
            return false;
        }

        var pattern = allowWrappedBold
            ? @"^(?<lead>(?:[-•▪]\s*)?)(?:\*\*(?<bodyBold>\d+[.)]\s*[^\n]+)\*\*|(?<bodyPlain>\d+[.)]\s*[^\n]+))$"
            : @"^(?<lead>(?:[-•▪]\s*)?)(?<bodyPlain>\d+[.)]\s*[^\n]+)$";
        var match = Regex.Match(normalized, pattern, RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return false;
        }

        lead = match.Groups["lead"].Value;
        body = match.Groups["bodyBold"].Success
            ? match.Groups["bodyBold"].Value.Trim()
            : match.Groups["bodyPlain"].Value.Trim();
        if (body.Length == 0)
        {
            return false;
        }

        var headlineMatch = Regex.Match(body, @"^\d+[.)]\s*(?<headline>.+)$", RegexOptions.CultureInvariant);
        if (!headlineMatch.Success)
        {
            return false;
        }

        headline = Regex.Replace(headlineMatch.Groups["headline"].Value, @"\s{2,}", " ").Trim();
        return LooksLikeStandaloneNumberedHeadlineText(headline);
    }

    private static bool LooksLikeStandaloneNumberedHeadlineText(string headline)
    {
        var normalized = (headline ?? string.Empty).Trim();
        if (normalized.Length < 2 || normalized.Length > 140)
        {
            return false;
        }

        if (normalized.Contains(':', StringComparison.Ordinal)
            || normalized.Contains('：', StringComparison.Ordinal)
            || normalized.Contains('|', StringComparison.Ordinal)
            || normalized.Contains("http://", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("https://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (normalized.StartsWith("출처", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("요약", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("핵심", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (normalized.EndsWith(".", StringComparison.Ordinal)
            || normalized.EndsWith("?", StringComparison.Ordinal)
            || normalized.EndsWith("!", StringComparison.Ordinal)
            || normalized.EndsWith("다.", StringComparison.Ordinal)
            || normalized.EndsWith("요.", StringComparison.Ordinal)
            || normalized.EndsWith("니다.", StringComparison.Ordinal)
            || normalized.EndsWith("습니다.", StringComparison.Ordinal))
        {
            return false;
        }

        return Regex.IsMatch(normalized, @"[A-Za-z가-힣0-9]", RegexOptions.CultureInvariant);
    }

    private static bool TryNormalizeExistingStructuredMarkdownLabelLine(string line, out string formatted)
    {
        formatted = string.Empty;
        var normalized = (line ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        var match = Regex.Match(
            normalized,
            @"^(?<lead>(?:[-•▪]\s*)?(?:(?:No\.\d+|\d+[.)])\s*)?)\*\*(?<label>[A-Za-z가-힣0-9('‘’][A-Za-z가-힣0-9()'‘’,.&+_/\-·\s]{0,80}?)\s*[:：]\*\*\s*(?<value>.*)$",
            RegexOptions.CultureInvariant
        );
        if (!match.Success)
        {
            return false;
        }

        var lead = match.Groups["lead"].Value;
        var label = Regex.Replace(match.Groups["label"].Value, @"\s{2,}", " ").Trim();
        if (!LooksLikeStructuredLabel(label))
        {
            return false;
        }

        var value = NormalizeStructuredLabelValueText(match.Groups["value"].Value);
        formatted = value.Length == 0
            ? $"{lead}**{label}:**"
            : $"{lead}**{label}:** {value}";
        return true;
    }

    private static bool TryFormatStructuredMarkdownLabelLine(string line, out string formatted)
    {
        formatted = string.Empty;
        var normalized = (line ?? string.Empty).Trim();
        if (normalized.Length == 0
            || normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("[", StringComparison.Ordinal))
        {
            return false;
        }

        if (Regex.IsMatch(
                normalized,
                @"^(?:[-•▪]\s*)?(?:(?:No\.\d+|\d+[.)])\s*)?\*\*.+?:\*\*(?:\s|$)",
                RegexOptions.CultureInvariant))
        {
            return false;
        }

        var match = Regex.Match(
            normalized,
            @"^(?<lead>(?:[-•▪]\s*)?(?:(?:No\.\d+|\d+[.)])\s*)?)(?<label>[A-Za-z가-힣0-9('‘’][A-Za-z가-힣0-9()'‘’,.&+_/\-·\s]{0,80}?)\s*[:：]\s*(?<value>.*)$",
            RegexOptions.CultureInvariant
        );
        if (!match.Success)
        {
            return false;
        }

        var lead = match.Groups["lead"].Value;
        var label = Regex.Replace(match.Groups["label"].Value, @"\s{2,}", " ").Trim();
        var value = NormalizeStructuredLabelValueText(match.Groups["value"].Value);
        if (!LooksLikeStructuredLabel(label))
        {
            return false;
        }

        formatted = value.Length == 0
            ? $"{lead}**{label}:**"
            : $"{lead}**{label}:** {value}";
        return true;
    }

    private static string NormalizeStructuredLabelValueText(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        normalized = Regex.Replace(normalized, @"^\*\*\s+", string.Empty, RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"\s+\*\*$", string.Empty, RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"^\*\*$", string.Empty, RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"\s{2,}", " ").Trim();
        return normalized;
    }

    private static bool LooksLikeStructuredLabel(string label)
    {
        var normalized = (label ?? string.Empty).Trim();
        if (normalized.Length == 0 || normalized.Length > 80)
        {
            return false;
        }

        if (normalized.Contains("://", StringComparison.Ordinal)
            || normalized.Contains('[', StringComparison.Ordinal)
            || normalized.Contains(']', StringComparison.Ordinal)
            || normalized.Contains('{', StringComparison.Ordinal)
            || normalized.Contains('}', StringComparison.Ordinal))
        {
            return false;
        }

        if (Regex.IsMatch(normalized, @"^(?:오전|오후)\s*\d{1,2}$", RegexOptions.CultureInvariant))
        {
            return false;
        }

        return Regex.IsMatch(normalized, @"[A-Za-z가-힣0-9]", RegexOptions.CultureInvariant);
    }

    private static bool TryExtractSourceNameCandidateLine(string line, out string sourceName)
    {
        sourceName = string.Empty;
        var trimmed = (line ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        trimmed = Regex.Replace(trimmed, @"^[-*•]\s+", string.Empty);
        trimmed = Regex.Replace(trimmed, @"^\d+[.)]\s+", string.Empty);
        trimmed = trimmed.Trim();
        if (!IsSourceNameCandidateLine(trimmed))
        {
            return false;
        }

        sourceName = trimmed;
        return true;
    }

    private static bool IsSourceNameCandidateLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmed = line.Trim();
        if (trimmed.Length > 60)
        {
            return false;
        }

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (trimmed.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        if (Regex.IsMatch(trimmed, @"^(No\.\d+|\d+[.)])\s+", RegexOptions.CultureInvariant))
        {
            return false;
        }

        if (trimmed.StartsWith("제목", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("내용", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("출처", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("요약", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (trimmed.StartsWith("[", StringComparison.Ordinal)
            || trimmed.StartsWith("{", StringComparison.Ordinal)
            || trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static string NormalizeMarkdownTableSeparators(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n', StringSplitOptions.None);
        var changed = false;

        for (var i = 0; i < lines.Length; i += 1)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.IndexOf('|') < 0)
            {
                continue;
            }

            var trimmed = line.Trim();
            var candidate = trimmed;
            if (!candidate.StartsWith("|", StringComparison.Ordinal))
            {
                candidate = "|" + candidate;
            }

            if (!candidate.EndsWith("|", StringComparison.Ordinal))
            {
                candidate += "|";
            }

            if (!MarkdownTableSeparatorCandidateRegex.IsMatch(candidate))
            {
                continue;
            }

            var cellsRaw = candidate.Trim('|').Split('|', StringSplitOptions.TrimEntries);
            if (cellsRaw.Length < 2)
            {
                continue;
            }

            var cells = new List<string>(cellsRaw.Length);
            var valid = true;
            foreach (var cellRaw in cellsRaw)
            {
                var compact = Regex.Replace(cellRaw, @"\s+", string.Empty);
                compact = NormalizeDashVariants(compact);
                if (!MarkdownLooseSeparatorCellRegex.IsMatch(compact))
                {
                    valid = false;
                    break;
                }

                var leadingColon = compact.StartsWith(':');
                var trailingColon = compact.EndsWith(':');
                var dashCount = compact.Count(ch => ch == '-');
                dashCount = Math.Max(3, dashCount);

                var cell = string.Concat(
                    leadingColon ? ":" : string.Empty,
                    new string('-', dashCount),
                    trailingColon ? ":" : string.Empty
                );
                cells.Add(cell);
            }

            if (!valid)
            {
                continue;
            }

            var leadingWhitespace = line[..(line.Length - line.TrimStart().Length)];
            var rebuilt = leadingWhitespace + "| " + string.Join(" | ", cells) + " |";
            if (!line.Equals(rebuilt, StringComparison.Ordinal))
            {
                lines[i] = rebuilt;
                changed = true;
            }
        }

        return changed ? string.Join('\n', lines) : normalized;
    }

    private static string CollapseMarkdownTableBlankLines(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n', StringSplitOptions.None);
        if (lines.Length < 3)
        {
            return normalized;
        }

        var compact = new List<string>(lines.Length);
        for (var i = 0; i < lines.Length; i += 1)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                var previous = compact.Count > 0 ? compact[^1] : string.Empty;
                var next = FindNextNonEmptyLine(lines, i + 1);
                if (IsMarkdownTableRow(previous) && IsMarkdownTableRow(next))
                {
                    continue;
                }
            }

            compact.Add(line);
        }

        return string.Join('\n', compact);
    }

    private static string UnwrapMarkdownTableCodeFences(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return Regex.Replace(
            text,
            @"```(?:markdown|md|table)?\s*\n(?<body>[\s\S]*?)```",
            match =>
            {
                var body = match.Groups["body"].Value
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace("\r", "\n", StringComparison.Ordinal)
                    .Trim();
                if (body.Length == 0)
                {
                    return match.Value;
                }

                var lines = body
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToArray();
                if (lines.Length < 2)
                {
                    return match.Value;
                }

                for (var i = 0; i + 1 < lines.Length; i += 1)
                {
                    if (!IsMarkdownTableRow(lines[i]))
                    {
                        continue;
                    }

                    if (!MarkdownTableSeparatorCandidateRegex.IsMatch(lines[i + 1].Trim()))
                    {
                        continue;
                    }

                    return body;
                }

                return match.Value;
            },
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
        );
    }

    private static string ConvertMarkdownTableRowsToList(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n', StringSplitOptions.None);
        var converted = new List<string>(lines.Length);
        var inCodeFence = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                inCodeFence = !inCodeFence;
                converted.Add(line);
                continue;
            }

            if (inCodeFence)
            {
                converted.Add(line);
                continue;
            }

            if (!trimmed.StartsWith("|", StringComparison.Ordinal)
                || !trimmed.EndsWith("|", StringComparison.Ordinal))
            {
                converted.Add(line);
                continue;
            }

            if (MarkdownTableSeparatorCandidateRegex.IsMatch(trimmed))
            {
                continue;
            }

            var cells = trimmed
                .Trim('|')
                .Split('|', StringSplitOptions.TrimEntries)
                .Where(cell => !string.IsNullOrWhiteSpace(cell))
                .ToArray();
            if (cells.Length < 2)
            {
                converted.Add(line);
                continue;
            }

            converted.Add("- " + string.Join(" / ", cells));
        }

        var merged = string.Join('\n', converted).Trim();
        return Regex.Replace(merged, @"\n{3,}", "\n\n");
    }

    private static string FindNextNonEmptyLine(string[] lines, int startIndex)
    {
        for (var i = Math.Max(0, startIndex); i < lines.Length; i += 1)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
            {
                return lines[i];
            }
        }

        return string.Empty;
    }

    private static bool IsMarkdownTableRow(string? line)
    {
        var trimmed = (line ?? string.Empty).Trim();
        if (trimmed.Length < 3)
        {
            return false;
        }

        if (!trimmed.StartsWith("|", StringComparison.Ordinal)
            || !trimmed.EndsWith("|", StringComparison.Ordinal))
        {
            return false;
        }

        return trimmed.Count(ch => ch == '|') >= 2;
    }

    private static string NormalizeDashVariants(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch is '\u2014' or '\u2013' or '\u2011' or '\u2212' or '\u2500' or '\u2012')
            {
                builder.Append('-');
            }
            else
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private static string RemoveCopilotMetaPreamble(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var cleaned = WebUtility.HtmlDecode(text ?? string.Empty);
        var hasWrapperTags = cleaned.Contains("<p", StringComparison.OrdinalIgnoreCase)
            || cleaned.Contains("</p>", StringComparison.OrdinalIgnoreCase)
            || cleaned.Contains("<pre", StringComparison.OrdinalIgnoreCase)
            || cleaned.Contains("</pre>", StringComparison.OrdinalIgnoreCase)
            || cleaned.Contains("<code", StringComparison.OrdinalIgnoreCase)
            || cleaned.Contains("</code>", StringComparison.OrdinalIgnoreCase);
        if (hasWrapperTags)
        {
            cleaned = HtmlBreakTagRegex.Replace(cleaned, "\n");
            cleaned = HtmlTagRegex.Replace(cleaned, string.Empty);
        }

        cleaned = CopilotFetchParagraphRegex.Replace(cleaned, string.Empty);
        cleaned = CopilotFetchSentenceRegex.Replace(cleaned, string.Empty);
        var lines = cleaned
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeDisplayLine)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !ShouldDropMetaLine(line))
            .ToArray();
        return lines.Length == 0 ? string.Empty : string.Join('\n', lines).Trim();
    }

    private static string NormalizeDisplayLine(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var line = raw.Trim();
        line = line.Replace("<p>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("</p>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("<pre>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("</pre>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("<code>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("</code>", string.Empty, StringComparison.OrdinalIgnoreCase);
        line = WebUtility.HtmlDecode(line);
        line = LeadingBulletSymbolRegex.Replace(line, string.Empty);
        line = Regex.Replace(line, @"[ \t]{2,}", " ").Trim();
        return line;
    }

    private static bool IsLikelyMarkdownText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (text.Contains("```", StringComparison.Ordinal)
            || text.Contains("| ---", StringComparison.Ordinal)
            || text.Contains("](", StringComparison.Ordinal)
            || Regex.IsMatch(text, @"\*\*.+?\*\*")
            || Regex.IsMatch(text, @"(^|\n)\s*(#{1,6}\s|[-*+]\s|\d+\.\s|>\s)", RegexOptions.Multiline))
        {
            return true;
        }

        return false;
    }

    private static string ImprovePlainTextLineBreaksForChat(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Trim();

        if (!normalized.Contains('\n'))
        {
            normalized = Regex.Replace(normalized, @"(?<!\n)(\d+[.)]\s+)", "\n$1");
            normalized = Regex.Replace(
                normalized,
                @"([.!?]|…|\.{3})\s+(?!(?:md|js|cs|ts|jsx|tsx|txt|py|json|html|css|xml|yml|yaml|sh|ps1|bat|cmd|log|csv|exe|dll|zip|tar|gz)\b)(?=[^\n])",
                "$1\n",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
            );
        }

        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
        return normalized;
    }

    private static bool ShouldDropMetaLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return true;
        }

        var trimmed = line.Trim();
        if (trimmed.Equals("copy", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("복사", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("", StringComparison.Ordinal)
            || trimmed.Equals("", StringComparison.Ordinal))
        {
            return true;
        }

        if (CopilotMetaLineRegex.IsMatch(line))
        {
            return true;
        }

        if (line.StartsWith("현재 작업:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (line.StartsWith("[자동 전환 모델:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string CollapseRepeatedCharacters(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var prev = '\0';
        var count = 0;
        foreach (var ch in text)
        {
            if (ch == prev)
            {
                count += 1;
            }
            else
            {
                prev = ch;
                count = 1;
            }

            if (count <= 8)
            {
                builder.Append(ch);
            }
            else if (count == 9)
            {
                builder.Append("...");
            }
        }

        return builder.ToString();
    }

    private static string CollapseRepeatedSentenceRuns(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var segments = Regex.Split(text, @"(?<=[\.\!\?]|다\.)\s+")
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
        if (segments.Length <= 1)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        string? prev = null;
        var repeats = 0;
        foreach (var segment in segments)
        {
            var normalized = Regex.Replace(segment, @"\s+", " ").Trim();
            if (prev != null && string.Equals(prev, normalized, StringComparison.OrdinalIgnoreCase))
            {
                repeats += 1;
                if (repeats >= 2)
                {
                    continue;
                }
            }
            else
            {
                prev = normalized;
                repeats = 0;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(normalized);
        }

        return builder.Length == 0 ? text : builder.ToString();
    }

    private static bool ShouldIncludeMultiComparisonEntry(string model, string text)
    {
        var normalizedModel = (model ?? string.Empty).Trim();
        var normalizedText = (text ?? string.Empty).Trim();
        if (normalizedModel.Equals("none", StringComparison.OrdinalIgnoreCase)
            && (normalizedText.Length == 0 || normalizedText.Equals("선택 안함", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return normalizedModel.Length > 0 || normalizedText.Length > 0;
    }

    private static string BuildMultiComparisonAssistantText(LlmMultiChatResult result)
    {
        var entries = new List<(string Provider, string Model, string Text)>();

        void AddEntry(string provider, string model, string text)
        {
            if (!ShouldIncludeMultiComparisonEntry(model, text))
            {
                return;
            }

            entries.Add((provider, model ?? string.Empty, text ?? string.Empty));
        }

        AddEntry("groq", result.GroqModel, result.GroqText);
        AddEntry("gemini", result.GeminiModel, result.GeminiText);
        AddEntry("cerebras", result.CerebrasModel, result.CerebrasText);
        AddEntry("nvidia", result.NvidiaModel, result.NvidiaText);
        AddEntry("copilot", result.CopilotModel, result.CopilotText);
        AddEntry("codex", result.CodexModel, result.CodexText);

        var builder = new StringBuilder();
        builder.Append("[[OMNI_MULTI_COMPARE_JSON]]{\"entries\":[");
        for (var i = 0; i < entries.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(",");
            }

            var entry = entries[i];
            builder.Append("{");
            builder.Append($"\"provider\":\"{WebSocketGateway.EscapeJson(entry.Provider)}\",");
            builder.Append($"\"model\":\"{WebSocketGateway.EscapeJson(entry.Model)}\",");
            builder.Append($"\"text\":\"{WebSocketGateway.EscapeJson(entry.Text)}\"");
            builder.Append("}");
        }

        builder.Append("]}");
        return builder.ToString();
    }

    private static (string CommonSummary, string CommonPoints, string Differences, string Recommendation) ParseComparisonSummarySections(string text)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        if (normalized.Length == 0)
        {
            return ("공통 요약을 생성하지 못했습니다.", "공통점 없음", "부분 차이 정리가 없습니다.", "추천 정리가 없습니다.");
        }

        var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string currentKey = "summary";

        static string DetectSectionKey(string line)
        {
            var normalized = (line ?? string.Empty).Trim();
            if (normalized.Length == 0)
            {
                return string.Empty;
            }

            normalized = normalized.TrimStart('#', '-', '*', ' ').Trim();
            if (normalized.StartsWith("[", StringComparison.Ordinal) && normalized.EndsWith("]", StringComparison.Ordinal))
            {
                normalized = normalized[1..^1].Trim();
            }

            if (normalized.EndsWith(":", StringComparison.Ordinal))
            {
                normalized = normalized[..^1].Trim();
            }

            return normalized switch
            {
                "공통 요약" => "summary",
                "요약" => "summary",
                "공통점" => "common_points",
                "공통 핵심" => "core",
                "핵심" => "core",
                "공통" => "common_points",
                "추천" => "recommendation",
                "부분 차이" => "differences",
                "차이" => "differences",
                _ => string.Empty
            };
        }

        foreach (var rawLine in normalized.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                if (!sections.ContainsKey(currentKey))
                {
                    sections[currentKey] = new List<string>();
                }

                if (sections[currentKey].Count > 0 && sections[currentKey][^1].Length > 0)
                {
                    sections[currentKey].Add(string.Empty);
                }

                continue;
            }

            var sectionKey = DetectSectionKey(trimmed);
            if (sectionKey.Length > 0)
            {
                currentKey = sectionKey;
                sections.TryAdd(currentKey, new List<string>());
                continue;
            }

            if (!sections.ContainsKey(currentKey))
            {
                sections[currentKey] = new List<string>();
            }

            sections[currentKey].Add(line);
        }

        static string JoinSection(Dictionary<string, List<string>> source, string key, string fallback)
        {
            if (!source.TryGetValue(key, out var lines))
            {
                return fallback;
            }

            var text = string.Join("\n", lines).Trim();
            return text.Length == 0 ? fallback : text;
        }

        var commonSummary = JoinSection(sections, "summary", normalized);
        var commonPoints = JoinSection(
            sections,
            sections.ContainsKey("common_points") ? "common_points" : "core",
            "공통점 없음"
        );
        var differences = JoinSection(sections, "differences", "부분 차이 정리가 없습니다.");
        var recommendation = JoinSection(sections, "recommendation", "추천 정리가 없습니다.");
        return (commonSummary, commonPoints, differences, recommendation);
    }

    private static (string CommonSummary, string CommonCore, string Differences) ParseMultiSummarySections(string text)
    {
        var parsed = ParseComparisonSummarySections(text);
        return (parsed.CommonSummary, parsed.CommonPoints, parsed.Differences);
    }

    private static (string CommonSummary, string CommonPoints, string Differences, string Recommendation) ParseCodingMultiSummarySections(string text)
    {
        return ParseComparisonSummarySections(text);
    }

    private static string BuildMultiSummaryAssistantText(string commonSummary, string commonCore, string differences)
    {
        var summary = string.IsNullOrWhiteSpace(commonSummary) ? "공통 요약을 생성하지 못했습니다." : commonSummary.Trim();
        var core = string.IsNullOrWhiteSpace(commonCore) ? "공통점 없음" : commonCore.Trim();
        var diff = string.IsNullOrWhiteSpace(differences) ? "부분 차이 정리가 없습니다." : differences.Trim();
        return $"""
                ### 공통 요약
                {summary}

                ### 공통 핵심
                {core}

                ### 부분 차이
                {diff}
                """;
    }

    private static string BuildCodeGenerationPrompt(string input, string languageHint, string modeLabel)
    {
        return $"""
                너는 로컬 실행 가능한 코드를 생성하는 엔지니어다.
                모드: {modeLabel}
                언어 힌트: {languageHint}

                아래 형식을 반드시 지켜라:
                1) 첫 줄에 LANGUAGE=<실행언어> (예: python, javascript, c, cpp, csharp, java, kotlin, html, css, bash)
                2) 다음에 단 하나의 코드블록만 출력
                3) 코드블록 안에는 순수 코드만 포함 (설명 금지)

                요청:
                {input}
                """;
    }

    private static ParsedCode ParseCodeCandidate(string text, string languageHint)
    {
        var raw = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new ParsedCode(NormalizeLanguageForCode(languageHint), string.Empty);
        }

        var firstLine = raw.Split('\n').FirstOrDefault() ?? string.Empty;
        var explicitLanguage = string.Empty;
        if (firstLine.StartsWith("LANGUAGE=", StringComparison.OrdinalIgnoreCase))
        {
            explicitLanguage = firstLine["LANGUAGE=".Length..].Trim();
        }

        var detectedLanguage = NormalizeLanguageForCode(string.IsNullOrWhiteSpace(explicitLanguage) ? languageHint : explicitLanguage);
        var match = CodeFenceRegex.Match(raw);
        if (match.Success)
        {
            var fenceLanguage = match.Groups[1].Value.Trim();
            var fenceCode = match.Groups[2].Value;
            if (!string.IsNullOrWhiteSpace(fenceLanguage))
            {
                detectedLanguage = NormalizeLanguageForCode(fenceLanguage);
            }

            return new ParsedCode(detectedLanguage, fenceCode.Trim());
        }

        var jsonMatch = JsonObjectRegex.Match(raw);
        if (jsonMatch.Success)
        {
            var jsonCode = jsonMatch.Value.Trim();
            return new ParsedCode(detectedLanguage, jsonCode);
        }

        var cleaned = raw
            .Replace("LANGUAGE=", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
        return new ParsedCode(detectedLanguage, cleaned);
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
            outcome.Summary
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
            string.IsNullOrWhiteSpace(rawResponse) ? "초안 생성 결과가 비어 있습니다." : TrimForOutput(rawResponse, 1800)
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
                    var candidateCommand = NormalizeGeneratedRunCommand(action.Command);
                    if (!string.IsNullOrWhiteSpace(candidateCommand))
                    {
                        deferredRunCommand = candidateCommand;
                        hasDeferredRunAction = true;
                        actionResults.Add($"run_deferred:{TrimForOutput(candidateCommand, 120)}");
                    }
                    else
                    {
                        actionResults.Add("run_deferred:empty_command");
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
            var expectedOutputLines = ExtractExpectedConsoleOutputLines(objective);
            var finalDisplayCommand = !shouldUseDeferredRunCommand
                ? BuildVerificationDisplayCommand(currentLanguage, changedFiles, workspaceRoot, objective, requestedPaths, expectedOutput)
                : DescribeCommandWithExpectedOutput(NormalizeGeneratedRunCommand(deferredRunCommand), expectedOutput, expectedOutputLines);
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
            && repairAttempt < MaxCodingRepairPasses
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
                mergedSummary
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
        return new AutonomousCodingOutcome(currentLanguage, lastCode, lastRawResponse, lastExecution, orderedChangedFiles, summary);
    }

    private static bool ShouldTrustDeferredVerificationCommand(string language, string objective, string command)
    {
        var normalizedLanguage = NormalizeLanguageForCode(language);
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        if (normalizedLanguage is "html" or "css" or "java" or "c" or "cpp")
        {
            return false;
        }

        if (normalizedLanguage == "javascript" && IsFrontendLikeCodingTask(objective, normalizedLanguage))
        {
            return false;
        }

        if (IsInteractiveProgramObjective(objective, normalizedLanguage))
        {
            return false;
        }

        return normalizedLanguage is "python" or "javascript" or "bash";
    }

    private static bool IsInteractiveProgramObjective(string objective, string normalizedLanguage)
    {
        var text = ExtractLatestCodingRequestText(WebUtility.HtmlDecode(objective ?? string.Empty)).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (normalizedLanguage == "python")
        {
            return ContainsAny(
                text,
                "게임",
                "game",
                "슈팅",
                "shooter",
                "shooting",
                "tetris",
                "pong",
                "snake",
                "비행기",
                "tkinter",
                "pygame",
                "arcade",
                "sprite",
                "animation",
                "애니메이션",
                "그래픽",
                "graphic",
                "gui",
                "window",
                "창",
                "mainloop",
                "canvas",
                "keyboard",
                "키보드",
                "마우스"
            );
        }

        if (normalizedLanguage == "javascript")
        {
            return IsFrontendLikeCodingTask(objective ?? string.Empty, normalizedLanguage)
                   || ContainsAny(text, "canvas", "animation", "sprite", "dom", "browser", "브라우저");
        }

        if (normalizedLanguage == "bash")
        {
            return ContainsAny(text, "watch", "tail -f", "server", "serve", "dev server", "실시간", "대기");
        }

        return false;
    }

    private static bool ShouldRequireDependencyFreePythonGame(string objective, string languageHint)
    {
        var normalizedLanguage = NormalizeLanguageForCode(languageHint);
        if (normalizedLanguage != "python")
        {
            return false;
        }

        if (!IsInteractiveProgramObjective(objective, normalizedLanguage))
        {
            return false;
        }

        var text = ExtractLatestCodingRequestText(WebUtility.HtmlDecode(objective ?? string.Empty)).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return !ContainsAny(
            text,
            "pygame",
            "pyglet",
            "arcade",
            "panda3d",
            "kivy",
            "sdl",
            "외부 패키지 사용",
            "외부 패키지 허용",
            "third-party",
            "third party",
            "requirements.txt",
            "pip install"
        );
    }

    private async Task<CodingLoopActionResult> ExecuteCodingLoopActionAsync(
        CodingLoopAction action,
        string workspaceRoot,
        IReadOnlyList<string> requestedPaths,
        string provider,
        CancellationToken cancellationToken
    )
    {
        var type = NormalizeCodingActionType(action.Type, action.Path, action.Content, action.Command);
        var resolvedPath = ResolveActionPathOrFallback(type, action.Path, action.Content, requestedPaths, workspaceRoot);
        if (type != "run" && string.IsNullOrWhiteSpace(resolvedPath))
        {
            return new CodingLoopActionResult($"{type}:missing_path", null, string.Empty, string.Empty, string.Empty, false);
        }

        if (type == "mkdir")
        {
            if (LooksLikeFilePathForDirectoryAction(resolvedPath, requestedPaths))
            {
                return new CodingLoopActionResult($"mkdir_skipped_file_like:{resolvedPath}", null, string.Empty, resolvedPath ?? string.Empty, resolvedPath ?? string.Empty, false);
            }

            var dir = ResolveWorkspacePath(workspaceRoot, resolvedPath);
            Directory.CreateDirectory(dir);
            return new CodingLoopActionResult($"mkdir:{dir}", null, string.Empty, dir, dir, false);
        }

        if (type == "write_file")
        {
            var filePath = ResolveWorkspacePath(workspaceRoot, resolvedPath);
            var parent = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            var normalizedContent = NormalizeProviderGeneratedFileContent(provider, resolvedPath ?? string.Empty, action.Content ?? string.Empty);
            await File.WriteAllTextAsync(filePath, normalizedContent, cancellationToken);
            var preview = normalizedContent;
            if (preview.Length > 12000)
            {
                preview = preview[..12000] + "\n...(truncated)";
            }

            return new CodingLoopActionResult($"write:{filePath}", null, preview, filePath, filePath, true);
        }

        if (type == "append_file")
        {
            var filePath = ResolveWorkspacePath(workspaceRoot, resolvedPath);
            var parent = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            var normalizedContent = NormalizeProviderGeneratedFileContent(provider, resolvedPath ?? string.Empty, action.Content ?? string.Empty);
            await File.AppendAllTextAsync(filePath, normalizedContent, cancellationToken);
            string preview;
            try
            {
                var current = await File.ReadAllTextAsync(filePath, cancellationToken);
                preview = current.Length <= 12000 ? current : current[^12000..];
            }
            catch
            {
                preview = normalizedContent;
            }

            return new CodingLoopActionResult($"append:{filePath}", null, preview, filePath, filePath, true);
        }

        if (type == "read_file")
        {
            var filePath = ResolveWorkspacePath(workspaceRoot, resolvedPath);
            if (!File.Exists(filePath))
            {
                return new CodingLoopActionResult($"read_miss:{filePath}", null, string.Empty, filePath, filePath, false);
            }

            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            var preview = content.Length <= 8000 ? content : content[..8000] + "\n...(truncated)";
            return new CodingLoopActionResult($"read:{filePath}", null, preview, filePath, filePath, false);
        }

        if (type == "delete_file")
        {
            var filePath = ResolveWorkspacePath(workspaceRoot, resolvedPath);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                return new CodingLoopActionResult($"delete:{filePath}", null, string.Empty, filePath, filePath, true);
            }

            return new CodingLoopActionResult($"delete_miss:{filePath}", null, string.Empty, filePath, filePath, false);
        }

        if (type == "run")
        {
            var command = NormalizeGeneratedRunCommand(action.Command);
            if (string.IsNullOrWhiteSpace(command))
            {
                return new CodingLoopActionResult("run:empty_command", null, string.Empty, string.Empty, string.Empty, false);
            }

            var shell = await RunWorkspaceCommandWithAutoInstallAsync(command, workspaceRoot, cancellationToken);
            var execution = new CodeExecutionResult(
                "bash",
                workspaceRoot,
                "-",
                command,
                shell.ExitCode,
                shell.StdOut,
                shell.StdErr,
                shell.TimedOut ? "timeout" : (shell.ExitCode == 0 ? "ok" : "error")
            );
            return new CodingLoopActionResult($"run:{command} => {execution.Status}", execution, string.Empty, string.Empty, string.Empty, false);
        }

        return new CodingLoopActionResult($"unsupported_action:{type}", null, string.Empty, string.Empty, string.Empty, false);
    }

    private static bool LooksLikeFilePathForDirectoryAction(string? resolvedPath, IReadOnlyList<string>? requestedPaths)
    {
        var normalized = NormalizeRequestedCodingPath(resolvedPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (requestedPaths != null
            && requestedPaths.Any(path => string.Equals(NormalizeRequestedCodingPath(path), normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var extension = Path.GetExtension(normalized);
        return !string.IsNullOrWhiteSpace(extension);
    }

    private static CodingLoopPlan? ParseCodingLoopPlan(string rawText)
    {
        var text = (rawText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var variants = BuildCodingPlanTextVariants(text);
        var candidates = new List<string>();
        foreach (var variant in variants)
        {
            if (variant.StartsWith("{", StringComparison.Ordinal))
            {
                candidates.Add(variant);
            }

            var firstBrace = variant.IndexOf('{');
            var lastBrace = variant.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                candidates.Add(variant[firstBrace..(lastBrace + 1)]);
            }

            var codeFence = CodeFenceRegex.Match(variant);
            if (codeFence.Success)
            {
                candidates.Add(codeFence.Groups[2].Value.Trim());
            }
        }

        foreach (var candidate in candidates.Distinct())
        {
            try
            {
                using var doc = JsonDocument.Parse(NormalizeJsonCandidate(candidate));
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var analysis = GetStringProperty(doc.RootElement, "analysis");
                var finalMessage = GetStringProperty(doc.RootElement, "final_message");
                var done = GetBoolProperty(doc.RootElement, "done");
                var actions = new List<CodingLoopAction>();
                if (doc.RootElement.TryGetProperty("actions", out var actionsElement)
                    && actionsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var actionElement in actionsElement.EnumerateArray())
                    {
                        if (actionElement.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        var type = GetStringProperty(actionElement, "type");
                        if (string.IsNullOrWhiteSpace(type))
                        {
                            type = GetStringProperty(actionElement, "op");
                        }

                        var path = GetStringProperty(actionElement, "path") ?? string.Empty;
                        var content = GetStringProperty(actionElement, "content") ?? string.Empty;
                        var command = GetStringProperty(actionElement, "command") ?? string.Empty;
                        actions.Add(new CodingLoopAction(
                            NormalizeCodingActionType(type, path, content, command),
                            path,
                            content,
                            command
                        ));
                    }
                }

                return new CodingLoopPlan(analysis ?? string.Empty, finalMessage ?? string.Empty, done, actions);
            }
            catch
            {
            }
        }

        return null;
    }

    private static IReadOnlyList<string> BuildCodingPlanTextVariants(string text)
    {
        var list = new List<string>();
        AddVariant(list, text);

        var decoded = WebUtility.HtmlDecode(text);
        AddVariant(list, decoded);
        AddVariant(list, UnwrapHtmlContainer(decoded));

        var unwrapped = UnwrapHtmlContainer(text);
        AddVariant(list, unwrapped);
        AddVariant(list, WebUtility.HtmlDecode(unwrapped));

        return list.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static void AddVariant(List<string> variants, string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            variants.Add(trimmed);
        }
    }

    private static string UnwrapHtmlContainer(string text)
    {
        var current = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(current))
        {
            return string.Empty;
        }

        current = current.TrimStart('●', '•', '-', '*', ' ');
        for (var i = 0; i < 3; i++)
        {
            var match = OuterHtmlContainerRegex.Match(current);
            if (!match.Success)
            {
                break;
            }

            current = match.Groups[2].Value.Trim();
        }

        return current;
    }

    private static string NormalizeJsonCandidate(string candidate)
    {
        var normalized = (candidate ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        normalized = WebUtility.HtmlDecode(normalized);
        normalized = UnwrapHtmlContainer(normalized);
        normalized = normalized
            .Replace('\u201c', '"')
            .Replace('\u201d', '"')
            .Replace('\u2018', '\'')
            .Replace('\u2019', '\'');
        normalized = EscapeJsonControlCharsInsideStrings(normalized);
        normalized = JsonTrailingCommaRegex.Replace(normalized, "$1");

        return normalized.Trim();
    }

    private static string EscapeJsonControlCharsInsideStrings(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input ?? string.Empty;
        }

        var builder = new StringBuilder(input.Length + 64);
        var inString = false;
        var escaped = false;

        for (var i = 0; i < input.Length; i++)
        {
            var ch = input[i];
            if (!inString)
            {
                if (ch == '"')
                {
                    inString = true;
                }

                builder.Append(ch);
                continue;
            }

            if (escaped)
            {
                builder.Append(ch);
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                builder.Append(ch);
                escaped = true;
                continue;
            }

            if (ch == '"')
            {
                builder.Append(ch);
                inString = false;
                continue;
            }

            if (ch == '\r')
            {
                builder.Append("\\n");
                if (i + 1 < input.Length && input[i + 1] == '\n')
                {
                    i++;
                }
                continue;
            }

            if (ch == '\n')
            {
                builder.Append("\\n");
                continue;
            }

            if (ch == '\t')
            {
                builder.Append("\\t");
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static string BuildFallbackCodeOnlyPrompt(string objective, string languageHint)
    {
        var resolvedLanguage = ResolveInitialCodingLanguage(languageHint, objective);
        var builder = new StringBuilder();
        builder.AppendLine("아래 요구사항을 만족하는 실행 가능한 코드만 반환하세요.");
        builder.AppendLine("규칙:");
        builder.AppendLine("- 반드시 첫 줄: LANGUAGE=<언어>");
        builder.AppendLine("- 반드시 단 하나의 코드블록만 출력");
        builder.AppendLine("- 설명/해설/JSON/HTML 태그 금지");
        builder.AppendLine("- 코드블록 안에는 순수 코드만 작성");
        builder.AppendLine("- 더미 구현, TODO, 의사코드 금지");
        builder.AppendLine("- 요청에 실행/출력 조건이 있으면 실제로 그 조건을 만족하는 코드만 작성");
        foreach (var rule in BuildLanguagePromptRuleLines(string.Empty, string.Empty, resolvedLanguage, objective))
        {
            builder.AppendLine(rule);
        }

        builder.AppendLine();
        builder.AppendLine($"언어 힌트: {resolvedLanguage}");
        builder.AppendLine("요구사항:");
        builder.AppendLine(objective ?? string.Empty);
        return builder.ToString().Trim();
    }

    private static string BuildFallbackFileBundlePrompt(
        string objective,
        string languageHint,
        IReadOnlyList<string>? requestedPaths = null
    )
    {
        var resolvedLanguage = ResolveInitialCodingLanguage(languageHint, objective);
        var builder = new StringBuilder();
        builder.AppendLine("아래 요구사항을 만족하는 파일 번들을 JSON으로만 반환하세요.");
        builder.AppendLine("규칙:");
        builder.AppendLine("- 반드시 JSON 객체만 출력");
        builder.AppendLine("- 설명, 해설, 마크다운, 코드펜스 금지");
        builder.AppendLine("- files 배열에는 필요한 파일만 넣기");
        builder.AppendLine("- path 는 상대경로만 사용");
        builder.AppendLine("- content 는 실제 파일 내용 전체를 문자열로 넣기");
        builder.AppendLine("- run 은 최종 실행 명령이 있으면 넣고, 없으면 빈 문자열");
        builder.AppendLine("- 미사용 파일, 설명용 더미 파일, TODO 전용 파일 금지");
        foreach (var rule in BuildLanguagePromptRuleLines(string.Empty, string.Empty, resolvedLanguage, objective, requestedPaths))
        {
            builder.AppendLine(rule);
        }
        builder.AppendLine("스키마:");
        builder.AppendLine("{\"language\":\"python\",\"files\":[{\"path\":\"calculator.py\",\"content\":\"...\"}],\"run\":\"python3 calculator.py\"}");
        builder.AppendLine($"언어 힌트: {resolvedLanguage}");
        if (requestedPaths != null && requestedPaths.Count > 0)
        {
            builder.AppendLine("우선 파일 후보:");
            foreach (var path in requestedPaths.Take(6))
            {
                builder.AppendLine($"- {path}");
            }
        }

        builder.AppendLine("요구사항:");
        builder.AppendLine(objective);
        return builder.ToString().Trim();
    }

    private static ParsedCode ExtractFallbackCode(string rawText, string languageHint, string objective)
    {
        var initialLanguage = ResolveInitialCodingLanguage(languageHint, objective);
        var variants = BuildCodingPlanTextVariants(rawText ?? string.Empty);
        foreach (var variant in variants)
        {
            var matches = CodeFenceRegex.Matches(variant);
            if (matches.Count == 0)
            {
                continue;
            }

            var fence = matches[^1];
            var code = fence.Groups[2].Value.Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                continue;
            }

            var fenceLanguage = fence.Groups[1].Value.Trim();
            var resolved = string.IsNullOrWhiteSpace(fenceLanguage)
                ? initialLanguage
                : NormalizeLanguageForCode(fenceLanguage);
            return new ParsedCode(resolved, NormalizeGeneratedFileContent(code));
        }

        foreach (var variant in variants)
        {
            var prefixed = ExtractLanguagePrefixedPlainCode(variant, initialLanguage);
            if (!string.IsNullOrWhiteSpace(prefixed.Code))
            {
                return new ParsedCode(prefixed.Language, NormalizeGeneratedFileContent(prefixed.Code));
            }
        }

        var plain = (rawText ?? string.Empty).Trim();
        if (plain.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)
            || plain.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            return new ParsedCode("html", plain);
        }

        var decoded = WebUtility.HtmlDecode(rawText ?? string.Empty);
        var contentMatches = JsonContentFieldRegex.Matches(decoded)
            .Select(match => new
            {
                Value = Regex.Unescape(match.Groups[1].Value).Trim(),
                Index = match.Index
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .OrderByDescending(x => x.Value.Length)
            .ToArray();
        if (contentMatches.Length > 0)
        {
            var extractedContent = contentMatches[0].Value;
            var detectedLanguage = initialLanguage;
            var pathMatches = JsonPathFieldRegex.Matches(decoded)
                .Select(match => new
                {
                    Value = Regex.Unescape(match.Groups[1].Value).Trim(),
                    Index = match.Index
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Value))
                .ToArray();
            if (pathMatches.Length > 0)
            {
                var closestPath = pathMatches
                    .OrderBy(path => Math.Abs(path.Index - contentMatches[0].Index))
                    .First();
                detectedLanguage = GuessLanguageFromPath(closestPath.Value, detectedLanguage);
            }

            return new ParsedCode(detectedLanguage, NormalizeGeneratedFileContent(extractedContent));
        }

        return new ParsedCode(initialLanguage, string.Empty);
    }

    private static FallbackFileBundle ExtractFallbackFileBundle(string rawText, string languageHint, string objective)
    {
        var initialLanguage = ResolveInitialCodingLanguage(languageHint, objective);
        var explicitLanguage = ResolveExplicitObjectiveLanguage(objective);
        var text = (rawText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new FallbackFileBundle(initialLanguage, string.Empty, Array.Empty<FallbackGeneratedFile>());
        }

        var variants = BuildCodingPlanTextVariants(text);
        var candidates = new List<string>();
        foreach (var variant in variants)
        {
            if (variant.StartsWith("{", StringComparison.Ordinal))
            {
                candidates.Add(variant);
            }

            var firstBrace = variant.IndexOf('{');
            var lastBrace = variant.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                candidates.Add(variant[firstBrace..(lastBrace + 1)]);
            }

            var codeFence = CodeFenceRegex.Match(variant);
            if (codeFence.Success)
            {
                candidates.Add(codeFence.Groups[2].Value.Trim());
            }
        }

        foreach (var candidate in candidates.Distinct(StringComparer.Ordinal))
        {
            try
            {
                using var doc = JsonDocument.Parse(NormalizeJsonCandidate(candidate));
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var language = NormalizeLanguageForCode(GetStringProperty(doc.RootElement, "language") ?? initialLanguage);
                var runCommand = GetStringProperty(doc.RootElement, "run")
                    ?? GetStringProperty(doc.RootElement, "command")
                    ?? string.Empty;
                var files = new List<FallbackGeneratedFile>();
                if (doc.RootElement.TryGetProperty("files", out var filesElement)
                    && filesElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var fileElement in filesElement.EnumerateArray())
                    {
                        if (fileElement.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        var rawPath = GetStringProperty(fileElement, "path")
                            ?? GetStringProperty(fileElement, "file")
                            ?? string.Empty;
                        var content = GetStringProperty(fileElement, "content")
                            ?? GetStringProperty(fileElement, "code")
                            ?? GetStringProperty(fileElement, "text")
                            ?? string.Empty;
                        var normalizedPath = NormalizeRequestedCodingPath(rawPath);
                        if (string.IsNullOrWhiteSpace(normalizedPath) || string.IsNullOrWhiteSpace(content))
                        {
                            continue;
                        }

                        files.Add(new FallbackGeneratedFile(normalizedPath, NormalizeGeneratedFileContent(content)));
                    }
                }

                if (files.Count > 0)
                {
                    if (!string.IsNullOrWhiteSpace(explicitLanguage)
                        && explicitLanguage is not ("html" or "css" or "javascript")
                        && language is "html" or "css" or "javascript")
                    {
                        continue;
                    }

                    return new FallbackFileBundle(language, NormalizeGeneratedRunCommand(runCommand), files);
                }
            }
            catch
            {
            }
        }

        return new FallbackFileBundle(initialLanguage, string.Empty, Array.Empty<FallbackGeneratedFile>());
    }

    private sealed record FallbackGeneratedFile(string Path, string Content);
    private sealed record FallbackFileBundle(string Language, string RunCommand, IReadOnlyList<FallbackGeneratedFile> Files);

    private static string NormalizeGeneratedFileContent(string content)
    {
        var normalized = (content ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return normalized.Trim();
        }

        var lines = normalized.Split('\n');
        var minIndent = lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(GetLeadingIndentWidth)
            .DefaultIfEmpty(0)
            .Min();
        if (minIndent <= 0)
        {
            return normalized.Trim('\n');
        }

        for (var i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                lines[i] = string.Empty;
                continue;
            }

            var trimCount = Math.Min(minIndent, GetLeadingIndentWidth(lines[i]));
            lines[i] = lines[i][trimCount..];
        }

        return string.Join('\n', lines).Trim('\n');
    }

    private static int GetLeadingIndentWidth(string line)
    {
        var width = 0;
        while (width < line.Length && (line[width] == ' ' || line[width] == '\t'))
        {
            width++;
        }

        return width;
    }

    private static ParsedCode ExtractLanguagePrefixedPlainCode(string text, string fallbackLanguage)
    {
        var normalized = WebUtility.HtmlDecode(text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new ParsedCode(NormalizeLanguageForCode(fallbackLanguage), string.Empty);
        }

        var lines = normalized.Split('\n');
        if (lines.Length == 0)
        {
            return new ParsedCode(NormalizeLanguageForCode(fallbackLanguage), string.Empty);
        }

        var firstLine = lines[0].Trim();
        if (!firstLine.StartsWith("LANGUAGE=", StringComparison.OrdinalIgnoreCase))
        {
            return new ParsedCode(NormalizeLanguageForCode(fallbackLanguage), string.Empty);
        }

        var declaredLanguage = firstLine["LANGUAGE=".Length..].Trim();
        var resolvedLanguage = NormalizeLanguageForCode(
            string.IsNullOrWhiteSpace(declaredLanguage) ? fallbackLanguage : declaredLanguage
        );

        if (lines.Length <= 1)
        {
            return new ParsedCode(resolvedLanguage, string.Empty);
        }

        var code = string.Join('\n', lines.Skip(1))
            .Trim('\n', '\r')
            .TrimEnd();

        if (code.StartsWith("```", StringComparison.Ordinal))
        {
            var fence = CodeFenceRegex.Match(code);
            if (fence.Success)
            {
                var fencedCode = fence.Groups[2].Value.Trim();
                if (!string.IsNullOrWhiteSpace(fencedCode))
                {
                    return new ParsedCode(resolvedLanguage, fencedCode);
                }
            }
        }

        return new ParsedCode(resolvedLanguage, code);
    }

    private static string SuggestFallbackEntryPath(string language, string objective, IReadOnlyList<string>? requestedPaths = null)
    {
        var requestedPath = SelectRequestedCodingPath(requestedPaths, language, null);
        if (!string.IsNullOrWhiteSpace(requestedPath))
        {
            return requestedPath;
        }

        var normalizedLanguage = NormalizeLanguageForCode(language);
        var objectiveText = ExtractLatestCodingRequestText(WebUtility.HtmlDecode(objective ?? string.Empty)).ToLowerInvariant();
        var domainMatch = DomainRegex.Match(objectiveText);
        var projectFolder = domainMatch.Success
            ? SanitizePathSegment(domainMatch.Groups[1].Value)
            : objectiveText.Contains("naver", StringComparison.OrdinalIgnoreCase)
                ? "naver.com"
                : objectiveText.Contains("clone", StringComparison.OrdinalIgnoreCase) || objectiveText.Contains("클론", StringComparison.OrdinalIgnoreCase)
                    ? "web-clone"
                    : "task";

        if (string.IsNullOrWhiteSpace(projectFolder))
        {
            projectFolder = "task";
        }

        return normalizedLanguage switch
        {
            "html" => $"{projectFolder}/index.html",
            "css" => $"{projectFolder}/styles.css",
            "javascript" => $"{projectFolder}/app.js",
            "c" => $"{projectFolder}/main.c",
            "cpp" => $"{projectFolder}/main.cpp",
            "csharp" => $"{projectFolder}/Program.cs",
            "java" => $"{projectFolder}/Main.java",
            "kotlin" => $"{projectFolder}/Main.kt",
            "bash" => $"{projectFolder}/run.sh",
            _ => $"{projectFolder}/main.py"
        };
    }

    private static string BuildCodingRepairObjectivePrompt(
        string objective,
        string languageHint,
        string workspaceRoot,
        CodeExecutionResult lastExecution,
        IReadOnlyCollection<string> changedFiles
    )
    {
        var builder = new StringBuilder();
        builder.AppendLine(objective?.Trim() ?? string.Empty);
        builder.AppendLine();
        builder.AppendLine("[최종 검증 실패]");
        builder.AppendLine($"언어 힌트: {languageHint}");
        builder.AppendLine($"실패 명령: {CompactWorkspaceCommandForPrompt(lastExecution.Command, workspaceRoot)}");
        builder.AppendLine($"stderr: {TrimForOutput(lastExecution.StdErr, 1200)}");
        builder.AppendLine($"stdout: {TrimForOutput(lastExecution.StdOut, 600)}");
        builder.AppendLine("수정 규칙:");
        builder.AppendLine("- 방금 생성한 파일을 우선 수정");
        builder.AppendLine("- 파일 경로 문자열에 줄바꿈이나 탭을 넣지 말 것");
        builder.AppendLine("- 문자열 리터럴을 불필요한 줄바꿈으로 끊지 말 것");
        builder.AppendLine("- 실패 원인을 실제로 해결한 뒤 최종 검증까지 끝낼 것");
        builder.AppendLine("- 최종 검증에서 생성 파일 존재와 stdout 조건까지 다시 만족할 것");
        if (ShouldRequireDependencyFreePythonGame(objective ?? string.Empty, languageHint))
        {
            builder.AppendLine("- 이번 수정에서는 pygame, pyglet, arcade 같은 외부 패키지와 pip install 시도를 금지한다");
            builder.AppendLine("- curses를 우선 사용하고, tkinter가 꼭 필요할 때만 선택하라");
            builder.AppendLine("- print 문만 반복하는 텍스트 시뮬레이션은 금지하고 실제 입력 처리와 화면 갱신이 있는 게임 루프를 구현하라");
            var stderr = lastExecution.StdErr ?? string.Empty;
            if (stderr.Contains("_tkinter", StringComparison.OrdinalIgnoreCase)
                || stderr.Contains("tkinter", StringComparison.OrdinalIgnoreCase))
            {
                builder.AppendLine("- 현재 환경에서는 tkinter 계열 import가 실패했으므로 이번 수정에서는 tkinter를 제거하고 curses 기반으로 바꿔라");
            }
        }
        foreach (var rule in BuildLanguagePromptRuleLines(string.Empty, string.Empty, languageHint, objective ?? string.Empty))
        {
            builder.AppendLine(rule);
        }
        if (changedFiles.Count > 0)
        {
            builder.AppendLine("현재 변경 파일:");
            foreach (var path in changedFiles.Take(8))
            {
                builder.AppendLine($"- {ToWorkspaceRelativePathForPrompt(workspaceRoot, path)}");
            }
        }

        return builder.ToString().Trim();
    }

    private static string ResolveFinalCodingResultLanguage(
        string currentLanguage,
        string languageHint,
        string objective,
        IReadOnlyCollection<string> changedFiles
    )
    {
        var normalizedCurrent = NormalizeLanguageForCode(currentLanguage);
        var normalizedInitial = ResolveInitialCodingLanguage(languageHint, objective);
        if (normalizedInitial == "html")
        {
            return "html";
        }

        if (normalizedCurrent is "javascript" or "css")
        {
            var hasHtmlFile = (changedFiles ?? Array.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Any(path =>
                {
                    var extension = Path.GetExtension(path).ToLowerInvariant();
                    return extension is ".html" or ".htm";
                });
            if (hasHtmlFile)
            {
                return "html";
            }
        }

        return normalizedCurrent;
    }

    private static string CompactWorkspaceCommandForPrompt(string command, string workspaceRoot)
    {
        var normalized = (command ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            normalized = normalized.Replace(workspaceRoot, ".", StringComparison.OrdinalIgnoreCase);
        }

        return TrimForOutput(normalized, 500);
    }

    private static string ToWorkspaceRelativePathForPrompt(string workspaceRoot, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            var relative = Path.GetRelativePath(workspaceRoot, path).Replace('\\', '/');
            return string.IsNullOrWhiteSpace(relative) ? Path.GetFileName(path) : relative;
        }
        catch
        {
            return Path.GetFileName(path);
        }
    }

    private static IReadOnlyList<string> ExtractRequestedCodingPaths(string objective, string languageHint)
    {
        var text = ExtractLatestCodingRequestText(WebUtility.HtmlDecode(objective ?? string.Empty));
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var normalizedLanguage = NormalizeLanguageForCode(languageHint);
        return RequestedCodingPathRegex.Matches(text)
            .Select(match => NormalizeRequestedCodingPath(match.Groups["path"].Value.Replace('\\', '/')))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(path => !path.Contains("://", StringComparison.Ordinal))
            .Where(path => !path.Contains("..", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(path => string.Equals(GuessLanguageFromPath(path, normalizedLanguage), normalizedLanguage, StringComparison.OrdinalIgnoreCase))
            .ThenBy(path => path.Count(ch => ch == '/'))
            .ToArray();
    }

    private static string SelectRequestedCodingPath(
        IReadOnlyList<string>? requestedPaths,
        string? languageHint,
        string? content
    )
    {
        if (requestedPaths == null || requestedPaths.Count == 0)
        {
            return string.Empty;
        }

        if (requestedPaths.Count == 1)
        {
            var onlyPath = requestedPaths[0];
            var singlePathLanguage = NormalizeCodingLanguageHintPreservingAuto(languageHint);
            if (singlePathLanguage == "auto" && !string.IsNullOrWhiteSpace(content))
            {
                singlePathLanguage = GuessLanguageFromPath(InferFallbackPathForGeneratedCode(content), singlePathLanguage);
            }

            if (singlePathLanguage == "auto")
            {
                return onlyPath;
            }

            var pathLanguage = GuessLanguageFromPath(onlyPath, "auto");
            return string.Equals(pathLanguage, singlePathLanguage, StringComparison.OrdinalIgnoreCase)
                ? onlyPath
                : string.Empty;
        }

        var normalizedLanguage = NormalizeCodingLanguageHintPreservingAuto(languageHint);
        if (normalizedLanguage == "auto" && !string.IsNullOrWhiteSpace(content))
        {
            normalizedLanguage = GuessLanguageFromPath(InferFallbackPathForGeneratedCode(content), normalizedLanguage);
        }

        if (normalizedLanguage == "auto")
        {
            return string.Empty;
        }

        return requestedPaths.FirstOrDefault(path =>
            string.Equals(GuessLanguageFromPath(path, normalizedLanguage), normalizedLanguage, StringComparison.OrdinalIgnoreCase))
            ?? string.Empty;
    }

    private static bool HasSingleFileIntent(string objective)
    {
        var text = (objective ?? string.Empty).ToLowerInvariant();
        return ContainsAny(
            text,
            "파일 하나",
            "파일 한개",
            "파일 1개",
            "한 파일",
            "single file",
            "single-file",
            "one file",
            "하나만"
        );
    }

    private static string[] CleanupRedundantSingleFileArtifacts(
        string workspaceRoot,
        string objective,
        IReadOnlyList<string> requestedPaths,
        IReadOnlyList<string> changedFiles
    )
    {
        if (requestedPaths.Count != 1 || !HasSingleFileIntent(objective) || changedFiles.Count == 0)
        {
            return changedFiles
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        string requestedFullPath;
        try
        {
            requestedFullPath = ResolveWorkspacePath(workspaceRoot, requestedPaths[0]);
        }
        catch
        {
            return changedFiles
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (!File.Exists(requestedFullPath))
        {
            return changedFiles
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var requestedExtension = Path.GetExtension(requestedFullPath);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedWorkspaceRoot = Path.GetFullPath(workspaceRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var cleaned = new List<string>();
        foreach (var path in changedFiles)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(path);
            if (string.Equals(fullPath, requestedFullPath, comparison))
            {
                cleaned.Add(fullPath);
                continue;
            }

            var parent = (Path.GetDirectoryName(fullPath) ?? string.Empty)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fileName = Path.GetFileName(fullPath);
            var shouldDelete =
                string.Equals(parent, normalizedWorkspaceRoot, comparison)
                && GenericCodingFallbackFileNames.Contains(fileName)
                && string.Equals(Path.GetExtension(fullPath), requestedExtension, StringComparison.OrdinalIgnoreCase)
                && File.Exists(fullPath);

            if (!shouldDelete)
            {
                cleaned.Add(fullPath);
                continue;
            }

            try
            {
                File.Delete(fullPath);
            }
            catch
            {
                cleaned.Add(fullPath);
            }
        }

        return cleaned
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ExtractExpectedConsoleOutput(string objective)
    {
        var text = ExtractLatestCodingRequestText(WebUtility.HtmlDecode(objective ?? string.Empty));
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        foreach (var regex in new[] { ExpectedOutputAfterQuotedRegex, ExpectedOutputBeforeQuotedRegex })
        {
            foreach (Match match in regex.Matches(text))
            {
                var value = match.Groups["value"].Value.Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (RequestedCodingPathRegex.IsMatch(value) || !IsLikelyExpectedOutputLiteral(value))
                {
                    continue;
                }

                return value;
            }
        }

        if (ContainsAny(text.ToLowerInvariant(), "출력", "print", "echo", "표시"))
        {
            var fallbackCandidate = GenericQuotedTextRegex.Matches(text)
                .Select(match => match.Groups["value"].Value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Where(value => !RequestedCodingPathRegex.IsMatch(value))
                .Where(IsLikelyExpectedOutputLiteral)
                .LastOrDefault();
            if (!string.IsNullOrWhiteSpace(fallbackCandidate))
            {
                return fallbackCandidate;
            }
        }

        return string.Empty;
    }

    private static string ExtractLatestCodingRequestText(string objective)
    {
        var text = (objective ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lastNewRequestMarker = text.LastIndexOf("[새 요청]", StringComparison.Ordinal);
        if (lastNewRequestMarker >= 0)
        {
            var requestText = text[(lastNewRequestMarker + "[새 요청]".Length)..].Trim();
            if (string.IsNullOrWhiteSpace(requestText))
            {
                return string.Empty;
            }

            var lines = requestText
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n', StringSplitOptions.None);
            var collected = new List<string>(lines.Length);
            foreach (var rawLine in lines)
            {
                var line = rawLine ?? string.Empty;
                var trimmed = line.Trim();
                if (collected.Count > 0
                    && trimmed.Length > 0
                    && Regex.IsMatch(trimmed, @"^\[[^\]\r\n]{1,80}\]$", RegexOptions.CultureInvariant))
                {
                    break;
                }

                collected.Add(line);
            }

            return string.Join('\n', collected).Trim();
        }

        return text;
    }

    private static bool IsLikelyExpectedOutputLiteral(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (normalized.Length > 120)
        {
            return false;
        }

        return !normalized.Equals("새 요청", StringComparison.OrdinalIgnoreCase)
               && !normalized.Equals("사용자 요청", StringComparison.OrdinalIgnoreCase)
               && !normalized.Equals("컨텍스트 사용 규칙", StringComparison.OrdinalIgnoreCase)
               && !normalized.Equals("최근 대화", StringComparison.OrdinalIgnoreCase)
               && !normalized.Equals("최종 검증 실패", StringComparison.OrdinalIgnoreCase)
               && !Regex.IsMatch(normalized, @"^(?:user|assistant|system)\s*=\s*(?:true|false)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool ShouldTryDeterministicSingleFileOutputRepair(
        string objective,
        string languageHint,
        IReadOnlyList<string> requestedPaths,
        string expectedOutput
    )
    {
        if (requestedPaths.Count != 1 || string.IsNullOrWhiteSpace(expectedOutput))
        {
            return false;
        }

        var normalizedLanguage = NormalizeLanguageForCode(languageHint);
        if (normalizedLanguage == "auto")
        {
            normalizedLanguage = GuessLanguageFromPath(requestedPaths[0], normalizedLanguage);
        }

        return string.Equals(normalizedLanguage, "python", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldPreferFileBundleFallback(string objective, IReadOnlyList<string>? requestedPaths)
    {
        if (requestedPaths != null && requestedPaths.Count > 1)
        {
            return true;
        }

        var text = ExtractLatestCodingRequestText(WebUtility.HtmlDecode(objective ?? string.Empty)).ToLowerInvariant();
        return ContainsAny(
            text,
            "두 파일",
            "2개 파일",
            "여러 파일",
            "multi-file",
            "multiple files"
        );
    }

    private static string BuildPythonStringLiteral(string value)
    {
        var builder = new StringBuilder();
        builder.Append('"');
        foreach (var ch in value ?? string.Empty)
        {
            switch (ch)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (char.IsControl(ch))
                    {
                        builder.Append($"\\u{(int)ch:x4}");
                    }
                    else
                    {
                        builder.Append(ch);
                    }
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
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
        var fullPath = ResolveWorkspacePath(workspaceRoot, requestedPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var code = $"print({BuildPythonStringLiteral(expectedOutput)})\n";
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
        files = Array.Empty<ScaffoldFileSpec>();
        var text = ExtractLatestCodingRequestText(WebUtility.HtmlDecode(objective ?? string.Empty)).ToLowerInvariant();
        var explicitLanguage = ResolveExplicitObjectiveLanguage(objective);
        if (!string.IsNullOrWhiteSpace(explicitLanguage)
            && explicitLanguage is not ("html" or "css" or "javascript"))
        {
            return false;
        }

        var isUiClone = ContainsAny(
            text,
            "클론",
            "clone",
            "ui",
            "레이아웃",
            "html",
            "css",
            "landing",
            "페이지"
        );
        if (!isUiClone || ContainsAny(text, "게임", "game", "테트리스", "tetris", "슈팅", "shooter", "비행기", "1942"))
        {
            return false;
        }

        var domainMatch = DomainRegex.Match(text);
        var folder = domainMatch.Success ? SanitizePathSegment(domainMatch.Groups[1].Value) : "web-clone";
        if (string.IsNullOrWhiteSpace(folder))
        {
            folder = "web-clone";
        }

        var indexPath = $"{folder}/index.html";
        var cssPath = $"{folder}/styles.css";
        var jsPath = $"{folder}/script.js";

        var indexContent = $"""
                            <!doctype html>
                            <html lang="ko">
                            <head>
                              <meta charset="utf-8" />
                              <meta name="viewport" content="width=device-width, initial-scale=1" />
                              <title>{folder} UI Clone</title>
                              <link rel="stylesheet" href="styles.css" />
                            </head>
                            <body>
                              <header class="topbar">
                                <div class="logo">{folder.ToUpperInvariant()}</div>
                                <nav class="menu">
                                  <a href="#">메일</a>
                                  <a href="#">카페</a>
                                  <a href="#">블로그</a>
                                  <a href="#">쇼핑</a>
                                </nav>
                              </header>
                              <main class="layout">
                                <section class="hero">
                                  <h1>{folder} 클론 UI</h1>
                                  <p>요청에 따라 실제 기능은 제외하고 화면만 구현된 버전입니다.</p>
                                </section>
                                <section class="grid">
                                  <article class="card">콘텐츠 카드 1</article>
                                  <article class="card">콘텐츠 카드 2</article>
                                  <article class="card">콘텐츠 카드 3</article>
                                </section>
                              </main>
                              <script src="script.js"></script>
                            </body>
                            </html>
                            """;
        var cssContent = """
                         * { box-sizing: border-box; }
                         body { margin: 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', 'Noto Sans KR', sans-serif; background: #f4f6f8; color: #1f2937; }
                         .topbar { height: 64px; display: flex; align-items: center; justify-content: space-between; padding: 0 20px; background: #ffffff; border-bottom: 1px solid #e5e7eb; position: sticky; top: 0; }
                         .logo { font-weight: 800; color: #03c75a; letter-spacing: 0.04em; }
                         .menu { display: flex; gap: 16px; }
                         .menu a { text-decoration: none; color: #374151; font-size: 14px; }
                         .layout { max-width: 1080px; margin: 24px auto; padding: 0 16px; }
                         .hero { background: #ffffff; border: 1px solid #e5e7eb; border-radius: 12px; padding: 24px; margin-bottom: 16px; }
                         .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 12px; }
                         .card { background: #ffffff; border: 1px solid #e5e7eb; border-radius: 10px; min-height: 140px; padding: 16px; }
                         """;
        var jsContent = """
                        document.addEventListener('DOMContentLoaded', () => {
                          console.log('UI clone rendered.');
                        });
                        """;

        files = new[]
        {
            new ScaffoldFileSpec(indexPath, indexContent),
            new ScaffoldFileSpec(cssPath, cssContent),
            new ScaffoldFileSpec(jsPath, jsContent)
        };
        return true;
    }

    private static bool TryGenerateDeterministicWebShooterScaffold(
        string objective,
        string languageHint,
        out IReadOnlyList<ScaffoldFileSpec> files
    )
    {
        files = Array.Empty<ScaffoldFileSpec>();
        var lang = NormalizeCodingLanguageHintPreservingAuto(languageHint);
        var explicitLanguage = ResolveExplicitObjectiveLanguage(objective);
        if (!string.IsNullOrWhiteSpace(explicitLanguage)
            && explicitLanguage is not ("html" or "javascript" or "css"))
        {
            return false;
        }

        if (lang != "auto" && lang != "html" && lang != "javascript" && lang != "css")
        {
            return false;
        }

        var text = ExtractLatestCodingRequestText(WebUtility.HtmlDecode(objective ?? string.Empty)).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var webSignals = ContainsAny(text, "웹", "web", "브라우저", "html", "canvas");
        var shooterSignals = ContainsAny(text, "슈팅", "shooter", "shooting", "비행기", "fighter", "flight", "arcade", "종스크롤", "scroll");
        var gameSignals = ContainsAny(text, "게임", "game");
        var frontendRequested = lang is "html" or "javascript" or "css"
            || explicitLanguage is "html" or "javascript" or "css";
        if (!(shooterSignals && gameSignals && (webSignals || frontendRequested)))
        {
            return false;
        }

        var indexContent = """
                           <!doctype html>
                           <html lang="ko">
                           <head>
                             <meta charset="utf-8" />
                             <meta name="viewport" content="width=device-width, initial-scale=1" />
                             <title>Sky Patrol</title>
                             <style>
                               :root {
                                 color-scheme: dark;
                                 --bg-top: #07111f;
                                 --bg-bottom: #10345a;
                                 --panel: rgba(7, 15, 28, 0.82);
                                 --line: rgba(255, 255, 255, 0.12);
                                 --text: #f4f7fb;
                                 --accent: #ffd166;
                                 --danger: #ff5d5d;
                                 --ok: #67e8b1;
                               }
                               * { box-sizing: border-box; }
                               body {
                                 margin: 0;
                                 min-height: 100vh;
                                 display: grid;
                                 place-items: center;
                                 background:
                                   radial-gradient(circle at top, rgba(255,255,255,0.08), transparent 30%),
                                   linear-gradient(180deg, var(--bg-top), var(--bg-bottom));
                                 color: var(--text);
                                 font-family: "Trebuchet MS", "Noto Sans KR", sans-serif;
                               }
                               .shell {
                                 width: min(100vw, 1024px);
                                 padding: 16px;
                               }
                               .hud {
                                 display: flex;
                                 justify-content: space-between;
                                 gap: 12px;
                                 padding: 12px 14px;
                                 margin-bottom: 12px;
                                 border: 1px solid var(--line);
                                 border-radius: 16px;
                                 background: var(--panel);
                                 backdrop-filter: blur(10px);
                                 text-transform: uppercase;
                                 letter-spacing: 0.08em;
                                 font-size: 13px;
                               }
                               .hud strong {
                                 color: var(--accent);
                                 font-size: 18px;
                                 margin-left: 8px;
                               }
                               .frame {
                                 position: relative;
                                 border-radius: 18px;
                                 overflow: hidden;
                                 border: 1px solid rgba(255,255,255,0.14);
                                 box-shadow: 0 28px 80px rgba(0, 0, 0, 0.35);
                               }
                               canvas {
                                 display: block;
                                 width: 100%;
                                 height: auto;
                                 background: linear-gradient(180deg, rgba(7,17,31,0.98), rgba(11,31,54,0.98));
                               }
                               .overlay {
                                 position: absolute;
                                 inset: 0;
                                 display: grid;
                                 place-items: center;
                                 background: linear-gradient(180deg, rgba(4, 10, 18, 0.25), rgba(4, 10, 18, 0.82));
                                 text-align: center;
                                 padding: 24px;
                               }
                               .panel {
                                 width: min(92%, 520px);
                                 padding: 28px 24px;
                                 border-radius: 20px;
                                 border: 1px solid var(--line);
                                 background: rgba(5, 12, 22, 0.88);
                                 box-shadow: 0 24px 70px rgba(0, 0, 0, 0.28);
                               }
                               .eyebrow {
                                 color: var(--ok);
                                 letter-spacing: 0.3em;
                                 font-size: 12px;
                                 text-transform: uppercase;
                               }
                               h1 {
                                 margin: 12px 0 8px;
                                 font-size: clamp(34px, 6vw, 58px);
                                 line-height: 0.95;
                               }
                               p {
                                 margin: 0;
                                 color: rgba(244, 247, 251, 0.82);
                                 line-height: 1.6;
                               }
                               .controls {
                                 margin-top: 18px;
                                 padding-top: 18px;
                                 border-top: 1px solid var(--line);
                                 display: grid;
                                 gap: 6px;
                                 font-size: 14px;
                               }
                               .cta {
                                 margin-top: 20px;
                                 display: inline-flex;
                                 align-items: center;
                                 justify-content: center;
                                 min-width: 180px;
                                 min-height: 48px;
                                 padding: 0 18px;
                                 border-radius: 999px;
                                 border: 1px solid rgba(255, 209, 102, 0.4);
                                 background: linear-gradient(180deg, rgba(255, 209, 102, 0.28), rgba(255, 209, 102, 0.1));
                                 color: #fff7df;
                                 font-weight: 700;
                               }
                               .hidden { display: none; }
                               @media (max-width: 640px) {
                                 .hud {
                                   flex-wrap: wrap;
                                   font-size: 12px;
                                 }
                               }
                             </style>
                           </head>
                           <body>
                             <div class="shell">
                               <div class="hud">
                                 <div>Score <strong id="score">0</strong></div>
                                 <div>Lives <strong id="lives">3</strong></div>
                                 <div>Wave <strong id="wave">1</strong></div>
                               </div>
                               <div class="frame">
                                 <canvas id="game" width="960" height="640"></canvas>
                                 <div id="overlay" class="overlay">
                                   <div class="panel">
                                     <div class="eyebrow">Arcade Shooter</div>
                                     <h1>Sky Patrol</h1>
                                     <p>브라우저에서 바로 실행되는 종스크롤 아케이드 슈팅 게임입니다. 적 편대를 피하고 격추해 최고 점수를 노리세요.</p>
                                     <div class="controls">
                                       <div>이동: 화살표 또는 WASD</div>
                                       <div>사격: Space 또는 J</div>
                                       <div>시작/재시작: Enter</div>
                                     </div>
                                     <div class="cta" id="overlayButton">Enter 키로 출격</div>
                                   </div>
                                 </div>
                               </div>
                             </div>
                             <script>
                               const canvas = document.getElementById("game");
                               const ctx = canvas.getContext("2d");
                               const overlay = document.getElementById("overlay");
                               const overlayButton = document.getElementById("overlayButton");
                               const scoreEl = document.getElementById("score");
                               const livesEl = document.getElementById("lives");
                               const waveEl = document.getElementById("wave");

                               const state = {
                                 running: false,
                                 gameOver: false,
                                 score: 0,
                                 wave: 1,
                                 spawnTimer: 0,
                                 stars: Array.from({ length: 90 }, () => ({
                                   x: Math.random() * canvas.width,
                                   y: Math.random() * canvas.height,
                                   size: Math.random() * 2 + 1,
                                   speed: Math.random() * 90 + 25
                                 })),
                                 player: null,
                                 bullets: [],
                                 enemyBullets: [],
                                 enemies: [],
                                 particles: [],
                                 keys: new Set(),
                                 lastTick: 0
                               };

                               function resetGame() {
                                 state.running = true;
                                 state.gameOver = false;
                                 state.score = 0;
                                 state.wave = 1;
                                 state.spawnTimer = 0;
                                 state.bullets = [];
                                 state.enemyBullets = [];
                                 state.enemies = [];
                                 state.particles = [];
                                 state.player = {
                                   x: canvas.width / 2 - 24,
                                   y: canvas.height - 92,
                                   w: 48,
                                   h: 54,
                                   speed: 340,
                                   cooldown: 0,
                                   lives: 3,
                                   fireRate: 0.16
                                 };
                                 syncHud();
                                 overlay.classList.add("hidden");
                               }

                               function syncHud() {
                                 scoreEl.textContent = String(state.score);
                                 livesEl.textContent = String(Math.max(0, state.player ? state.player.lives : 0));
                                 waveEl.textContent = String(state.wave);
                               }

                               function setOverlay(title, message, action) {
                                 overlay.classList.remove("hidden");
                                 overlay.querySelector("h1").textContent = title;
                                 overlay.querySelector("p").textContent = message;
                                 overlayButton.textContent = action;
                               }

                               function spawnEnemy() {
                                 const width = 34 + Math.random() * 18;
                                 const type = Math.random() > 0.72 ? "ace" : "scout";
                                 const speed = type === "ace" ? 130 + state.wave * 10 : 90 + state.wave * 8;
                                 state.enemies.push({
                                   x: 40 + Math.random() * (canvas.width - 80 - width),
                                   y: -70,
                                   w: width,
                                   h: type === "ace" ? 44 : 34,
                                   speed,
                                   hp: type === "ace" ? 3 : 1,
                                   fireTimer: 0.8 + Math.random() * 1.8,
                                   drift: (Math.random() * 2 - 1) * (type === "ace" ? 70 : 35),
                                   seed: Math.random() * Math.PI * 2,
                                   type
                                 });
                               }

                               function firePlayerBullet() {
                                 const p = state.player;
                                 state.bullets.push(
                                   { x: p.x + 9, y: p.y - 6, w: 8, h: 20, speed: 520 },
                                   { x: p.x + p.w - 17, y: p.y - 6, w: 8, h: 20, speed: 520 }
                                 );
                               }

                               function fireEnemyBullet(enemy) {
                                 state.enemyBullets.push({
                                   x: enemy.x + enemy.w / 2 - 3,
                                   y: enemy.y + enemy.h,
                                   w: 6,
                                   h: 16,
                                   speed: 220 + state.wave * 10
                                 });
                               }

                               function explode(x, y, color) {
                                 for (let i = 0; i < 18; i += 1) {
                                   const angle = (Math.PI * 2 * i) / 18;
                                   const speed = 60 + Math.random() * 140;
                                   state.particles.push({
                                     x,
                                     y,
                                     vx: Math.cos(angle) * speed,
                                     vy: Math.sin(angle) * speed,
                                     life: 0.55 + Math.random() * 0.35,
                                     maxLife: 0.9,
                                     size: 2 + Math.random() * 3,
                                     color
                                   });
                                 }
                               }

                               function hitPlayer() {
                                 if (!state.player || state.gameOver) {
                                   return;
                                 }
                                 state.player.lives -= 1;
                                 explode(state.player.x + state.player.w / 2, state.player.y + state.player.h / 2, "#ff9f68");
                                 syncHud();
                                 if (state.player.lives <= 0) {
                                   state.running = false;
                                   state.gameOver = true;
                                   setOverlay("Mission Failed", `최종 점수 ${state.score}점. Enter 키로 다시 출격하세요.`, "Enter 키로 재도전");
                                   return;
                                 }
                                 state.player.x = canvas.width / 2 - state.player.w / 2;
                                 state.player.y = canvas.height - 92;
                               }

                               function rectsIntersect(a, b) {
                                 return a.x < b.x + b.w && a.x + a.w > b.x && a.y < b.y + b.h && a.y + a.h > b.y;
                               }

                               function update(dt) {
                                 state.stars.forEach((star) => {
                                   star.y += star.speed * dt;
                                   if (star.y > canvas.height + 4) {
                                     star.y = -4;
                                     star.x = Math.random() * canvas.width;
                                   }
                                 });

                                 state.particles = state.particles.filter((particle) => {
                                   particle.life -= dt;
                                   particle.x += particle.vx * dt;
                                   particle.y += particle.vy * dt;
                                   particle.vx *= 0.98;
                                   particle.vy *= 0.98;
                                   return particle.life > 0;
                                 });

                                 if (!state.running || !state.player) {
                                   return;
                                 }

                                 const p = state.player;
                                 if (state.keys.has("ArrowLeft") || state.keys.has("a")) p.x -= p.speed * dt;
                                 if (state.keys.has("ArrowRight") || state.keys.has("d")) p.x += p.speed * dt;
                                 if (state.keys.has("ArrowUp") || state.keys.has("w")) p.y -= p.speed * dt;
                                 if (state.keys.has("ArrowDown") || state.keys.has("s")) p.y += p.speed * dt;
                                 p.x = Math.max(18, Math.min(canvas.width - p.w - 18, p.x));
                                 p.y = Math.max(24, Math.min(canvas.height - p.h - 16, p.y));

                                 p.cooldown -= dt;
                                 if ((state.keys.has(" ") || state.keys.has("j")) && p.cooldown <= 0) {
                                   firePlayerBullet();
                                   p.cooldown = p.fireRate;
                                 }

                                 state.spawnTimer -= dt;
                                 if (state.spawnTimer <= 0) {
                                   spawnEnemy();
                                   const density = Math.max(0.28, 1.05 - state.wave * 0.05);
                                   state.spawnTimer = density;
                                 }

                                 state.bullets.forEach((bullet) => { bullet.y -= bullet.speed * dt; });
                                 state.enemyBullets.forEach((bullet) => { bullet.y += bullet.speed * dt; });
                                 state.bullets = state.bullets.filter((bullet) => bullet.y + bullet.h > -10);
                                 state.enemyBullets = state.enemyBullets.filter((bullet) => bullet.y < canvas.height + 20);

                                 for (const enemy of state.enemies) {
                                   enemy.y += enemy.speed * dt;
                                   enemy.x += Math.sin((performance.now() / 1000) * 2.4 + enemy.seed) * enemy.drift * dt;
                                   enemy.x = Math.max(10, Math.min(canvas.width - enemy.w - 10, enemy.x));
                                   enemy.fireTimer -= dt;
                                   if (enemy.fireTimer <= 0) {
                                     fireEnemyBullet(enemy);
                                     enemy.fireTimer = enemy.type === "ace" ? 0.75 : 1.6 + Math.random() * 0.8;
                                   }
                                 }

                                 for (let i = state.enemies.length - 1; i >= 0; i -= 1) {
                                   const enemy = state.enemies[i];
                                   if (enemy.y > canvas.height + 30) {
                                     state.enemies.splice(i, 1);
                                     continue;
                                   }

                                   if (rectsIntersect(enemy, p)) {
                                     state.enemies.splice(i, 1);
                                     hitPlayer();
                                   }
                                 }

                                 for (let i = state.bullets.length - 1; i >= 0; i -= 1) {
                                   const bullet = state.bullets[i];
                                   let consumed = false;
                                   for (let j = state.enemies.length - 1; j >= 0; j -= 1) {
                                     const enemy = state.enemies[j];
                                     if (!rectsIntersect(bullet, enemy)) {
                                       continue;
                                     }
                                     enemy.hp -= 1;
                                     consumed = true;
                                     if (enemy.hp <= 0) {
                                       state.enemies.splice(j, 1);
                                       state.score += enemy.type === "ace" ? 180 : 60;
                                       if (state.score > 0 && state.score % 900 === 0) {
                                         state.wave += 1;
                                       }
                                       explode(enemy.x + enemy.w / 2, enemy.y + enemy.h / 2, enemy.type === "ace" ? "#ffd166" : "#9ad1ff");
                                       syncHud();
                                     }
                                     break;
                                   }
                                   if (consumed) {
                                     state.bullets.splice(i, 1);
                                   }
                                 }

                                 for (let i = state.enemyBullets.length - 1; i >= 0; i -= 1) {
                                   if (rectsIntersect(state.enemyBullets[i], p)) {
                                     state.enemyBullets.splice(i, 1);
                                     hitPlayer();
                                   }
                                 }
                               }

                               function drawBackground() {
                                 const sky = ctx.createLinearGradient(0, 0, 0, canvas.height);
                                 sky.addColorStop(0, "#07111f");
                                 sky.addColorStop(1, "#0d3153");
                                 ctx.fillStyle = sky;
                                 ctx.fillRect(0, 0, canvas.width, canvas.height);

                                 state.stars.forEach((star) => {
                                   ctx.fillStyle = `rgba(255,255,255,${0.35 + star.size * 0.12})`;
                                   ctx.fillRect(star.x, star.y, star.size, star.size * 1.6);
                                 });
                               }

                               function drawPlayer(player) {
                                 ctx.save();
                                 ctx.translate(player.x, player.y);
                                 ctx.fillStyle = "#d9f5ff";
                                 ctx.beginPath();
                                 ctx.moveTo(player.w / 2, 0);
                                 ctx.lineTo(player.w, player.h - 8);
                                 ctx.lineTo(player.w / 2 + 8, player.h - 12);
                                 ctx.lineTo(player.w / 2 + 3, player.h);
                                 ctx.lineTo(player.w / 2 - 3, player.h);
                                 ctx.lineTo(player.w / 2 - 8, player.h - 12);
                                 ctx.lineTo(0, player.h - 8);
                                 ctx.closePath();
                                 ctx.fill();
                                 ctx.fillStyle = "#ff6b6b";
                                 ctx.fillRect(player.w / 2 - 4, 10, 8, 18);
                                 ctx.restore();
                               }

                               function drawEnemy(enemy) {
                                 ctx.save();
                                 ctx.translate(enemy.x, enemy.y);
                                 ctx.fillStyle = enemy.type === "ace" ? "#ff9d5c" : "#ff5d7a";
                                 ctx.beginPath();
                                 ctx.moveTo(enemy.w / 2, enemy.h);
                                 ctx.lineTo(enemy.w, enemy.h * 0.2);
                                 ctx.lineTo(enemy.w * 0.68, 0);
                                 ctx.lineTo(enemy.w * 0.5, enemy.h * 0.28);
                                 ctx.lineTo(enemy.w * 0.32, 0);
                                 ctx.lineTo(0, enemy.h * 0.2);
                                 ctx.closePath();
                                 ctx.fill();
                                 ctx.fillStyle = "rgba(255,255,255,0.5)";
                                 ctx.fillRect(enemy.w / 2 - 3, enemy.h * 0.24, 6, 12);
                                 ctx.restore();
                               }

                               function drawProjectiles() {
                                 ctx.fillStyle = "#ffe08a";
                                 state.bullets.forEach((bullet) => ctx.fillRect(bullet.x, bullet.y, bullet.w, bullet.h));
                                 ctx.fillStyle = "#ff8269";
                                 state.enemyBullets.forEach((bullet) => ctx.fillRect(bullet.x, bullet.y, bullet.w, bullet.h));
                               }

                               function drawParticles() {
                                 state.particles.forEach((particle) => {
                                   const alpha = Math.max(0, particle.life / particle.maxLife);
                                   ctx.fillStyle = particle.color.replace(")", `, ${alpha})`).replace("rgb", "rgba");
                                   if (!ctx.fillStyle.includes("rgba")) {
                                     ctx.fillStyle = particle.color;
                                     ctx.globalAlpha = alpha;
                                   } else {
                                     ctx.globalAlpha = 1;
                                   }
                                   ctx.fillRect(particle.x, particle.y, particle.size, particle.size);
                                 });
                                 ctx.globalAlpha = 1;
                               }

                               function draw() {
                                 drawBackground();
                                 drawProjectiles();
                                 state.enemies.forEach(drawEnemy);
                                 if (state.player) {
                                   drawPlayer(state.player);
                                 }
                                 drawParticles();
                               }

                               function loop(timestamp) {
                                 if (!state.lastTick) {
                                   state.lastTick = timestamp;
                                 }
                                 const dt = Math.min(0.033, (timestamp - state.lastTick) / 1000);
                                 state.lastTick = timestamp;
                                 update(dt);
                                 draw();
                                 requestAnimationFrame(loop);
                               }

                               window.addEventListener("keydown", (event) => {
                                 const key = event.key.length === 1 ? event.key.toLowerCase() : event.key;
                                 if (key === "Enter") {
                                   resetGame();
                                   return;
                                 }
                                 if (key === " ") {
                                   event.preventDefault();
                                 }
                                 state.keys.add(key);
                               });

                               window.addEventListener("keyup", (event) => {
                                 const key = event.key.length === 1 ? event.key.toLowerCase() : event.key;
                                 state.keys.delete(key);
                               });

                               setOverlay("Sky Patrol", "Enter 키를 눌러 출격하세요. 적 편대를 격추하고 생존 시간을 늘리세요.", "Enter 키로 출격");
                               syncHud();
                               requestAnimationFrame(loop);
                             </script>
                           </body>
                           </html>
                           """;

        files = new[]
        {
            new ScaffoldFileSpec("index.html", indexContent)
        };
        return true;
    }

    private static string SanitizePathSegment(string raw)
    {
        var value = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '.')
            {
                builder.Append(ch);
            }
        }

        return builder.Length == 0 ? string.Empty : builder.ToString();
    }

    private static bool IsKnownCodingActionType(string value)
    {
        return value == "mkdir"
               || value == "write_file"
               || value == "append_file"
               || value == "read_file"
               || value == "delete_file"
               || value == "run";
    }

    private static string NormalizeCodingActionType(string? rawType, string? path, string? content, string? command)
    {
        var raw = (rawType ?? string.Empty).Trim().ToLowerInvariant();
        if (IsKnownCodingActionType(raw))
        {
            return raw;
        }

        if (raw.Contains('|', StringComparison.Ordinal))
        {
            var split = raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var token in split)
            {
                if (IsKnownCodingActionType(token))
                {
                    return token;
                }
            }
        }

        if (raw.Contains("mkdir", StringComparison.Ordinal))
        {
            return "mkdir";
        }

        if (raw.Contains("append", StringComparison.Ordinal))
        {
            return "append_file";
        }

        if (raw.Contains("write", StringComparison.Ordinal) || raw.Contains("create", StringComparison.Ordinal))
        {
            return "write_file";
        }

        if (raw.Contains("read", StringComparison.Ordinal))
        {
            return "read_file";
        }

        if (raw.Contains("delete", StringComparison.Ordinal) || raw.Contains("remove", StringComparison.Ordinal))
        {
            return "delete_file";
        }

        if (raw.Contains("run", StringComparison.Ordinal) || raw.Contains("exec", StringComparison.Ordinal))
        {
            return "run";
        }

        var normalizedCommand = (command ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(normalizedCommand))
        {
            return "run";
        }

        var normalizedPath = (path ?? string.Empty).Trim();
        var normalizedContent = (content ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(normalizedPath))
        {
            var hasContent = !string.IsNullOrWhiteSpace(normalizedContent);
            if (hasContent)
            {
                return "write_file";
            }

            if (!Path.HasExtension(normalizedPath))
            {
                return "mkdir";
            }

            return "write_file";
        }

        return "run";
    }

    private int GetCodingPlanMaxOutputTokens(string provider)
    {
        if (string.Equals(provider, "groq", StringComparison.OrdinalIgnoreCase)
            || string.Equals(provider, "gemini", StringComparison.OrdinalIgnoreCase)
            || string.Equals(provider, "cerebras", StringComparison.OrdinalIgnoreCase)
            || string.Equals(provider, "nvidia", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Clamp(_config.CodingMaxOutputTokens, 1200, 3200);
        }

        return Math.Clamp(_config.CodingMaxOutputTokens, 1200, 2400);
    }

    private int ResolveMaxIterations(string provider, bool oneShotMode)
    {
        if (oneShotMode)
        {
            return 1;
        }

        return Math.Max(2, _config.CodingAgentMaxIterations);
    }

    private int ResolveMaxActions(string provider, bool oneShotMode)
    {
        if (oneShotMode)
        {
            return Math.Max(4, _config.CodingAgentMaxActionsPerIteration);
        }

        if (string.Equals(provider, "copilot", StringComparison.OrdinalIgnoreCase))
        {
            var copilotActions = Math.Max(1, _config.CodingCopilotMaxActionsPerIteration);
            return Math.Min(Math.Max(1, _config.CodingAgentMaxActionsPerIteration), copilotActions);
        }

        return Math.Max(1, _config.CodingAgentMaxActionsPerIteration);
    }

    private const int VisibleCodingStageTotal = 6;
    private const int MaxCodingRepairPasses = 1;

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
        return new CodingProgressUpdate(
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
        var compact = Regex.Replace((objective ?? string.Empty).Trim(), @"\s+", " ");
        if (compact.Length > 180)
        {
            compact = compact[..180] + "...";
        }

        var language = string.IsNullOrWhiteSpace(languageHint) ? "auto" : languageHint.Trim();
        return string.IsNullOrWhiteSpace(compact)
            ? $"언어 힌트: {language}"
            : $"{compact} · 언어 힌트: {language}";
    }

    private static string BuildWorkspaceProgressDetail(string workspaceSnapshot)
    {
        var lines = (workspaceSnapshot ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            return "워크스페이스 스냅샷 없음";
        }

        var headline = lines[0];
        if (headline.StartsWith("total_files=", StringComparison.OrdinalIgnoreCase))
        {
            return $"작업공간 점검 완료 · {headline.Replace("total_files=", "파일 수 ", StringComparison.OrdinalIgnoreCase)}";
        }

        return TrimForOutput(headline, 180);
    }

    private static string BuildCodingPlanProgressDetail(CodingLoopPlan plan, IReadOnlyList<CodingLoopAction> actions, bool hasDeferredRunAction)
    {
        var parts = new List<string>();
        var analysis = Regex.Replace((plan.Analysis ?? string.Empty).Trim(), @"\s+", " ");
        if (!string.IsNullOrWhiteSpace(analysis))
        {
            parts.Add(TrimForOutput(analysis, 180));
        }

        var targetPaths = actions
            .Where(action => !string.Equals(action.Type, "run", StringComparison.OrdinalIgnoreCase))
            .Select(action => (action.Path ?? string.Empty).Trim())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();
        if (targetPaths.Length > 0)
        {
            parts.Add("대상 파일: " + string.Join(", ", targetPaths));
        }

        if (hasDeferredRunAction)
        {
            parts.Add("실행은 마지막 단계에서 1회만 수행");
        }

        return parts.Count == 0 ? "이번 반복 구현 계획을 정리했습니다." : string.Join(" · ", parts);
    }

    private static string BuildCodingWriteProgressDetail(IReadOnlyCollection<string> changedPaths, bool hasDeferredRunAction)
    {
        var parts = new List<string>();
        if (changedPaths.Count > 0)
        {
            parts.Add($"변경 파일 {changedPaths.Count}개");
            parts.Add(string.Join(", ", changedPaths.Take(4)));
        }
        else
        {
            parts.Add("실제 파일 변경은 아직 없음");
        }

        if (hasDeferredRunAction)
        {
            parts.Add("중간 실행 없이 최종 검증만 예약");
        }

        return string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private bool ShouldUseOneShotMode(string provider, string objective, string languageHint)
    {
        if (!_config.CodingEnableOneShotUiClone)
        {
            return false;
        }

        if (!string.Equals(provider, "copilot", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(provider, "codex", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var lang = NormalizeLanguageForCode(languageHint);
        if (lang != "auto" && lang != "html" && lang != "javascript" && lang != "css" && lang != "python")
        {
            return false;
        }

        var text = (objective ?? string.Empty).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var uiSignals = ContainsAny(
            text,
            "ui",
            "ux",
            "clone",
            "클론",
            "랜딩",
            "landing",
            "페이지",
            "프론트",
            "frontend",
            "html",
            "css",
            "웹",
            "web",
            "게임",
            "game",
            "슈팅",
            "shooter",
            "화면",
            "레이아웃"
        );
        var backendSignals = ContainsAny(
            text,
            "api",
            "db",
            "database",
            "migration",
            "queue",
            "worker",
            "socket",
            "grpc",
            "redis",
            "kafka",
            "server"
        );

        return uiSignals && !backendSignals;
    }

    private string BuildRecentLoopLogs(IReadOnlyList<string> iterations, string provider)
    {
        if (iterations.Count == 0)
        {
            return string.Empty;
        }

        var keepCount = string.Equals(provider, "copilot", StringComparison.OrdinalIgnoreCase)
            ? Math.Max(1, _config.CodingRecentLoopHistoryForCopilot)
            : 4;
        var selected = iterations.TakeLast(keepCount).ToArray();
        for (var i = 0; i < selected.Length; i++)
        {
            selected[i] = TrimForOutput(selected[i], 500);
        }

        return string.Join("\n", selected);
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
        var builder = new StringBuilder();
        builder.AppendLine("너는 로컬 코딩 실행 에이전트다.");
        builder.AppendLine($"모드: {modeLabel}");
        builder.AppendLine($"모델 제공자: {provider}");
        builder.AppendLine($"모델: {provider}:{model}");
        builder.AppendLine($"반복: {iteration}/{maxIterations}");
        builder.AppendLine($"기준 작업 디렉터리: {workspaceRoot}");
        builder.AppendLine($"언어 힌트: {resolvedLanguage}");
        builder.AppendLine($"one-shot: {(oneShotMode ? "true" : "false")}");
        builder.AppendLine();
        builder.AppendLine("[목표]");
        builder.AppendLine(objective);
        builder.AppendLine();
        builder.AppendLine("[품질 브리프]");
        builder.AppendLine(BuildCodingQualityBrief(objective, resolvedLanguage));
        builder.AppendLine();
        builder.AppendLine("[최근 실행 결과]");
        builder.AppendLine($"status={lastExecution.Status}");
        builder.AppendLine($"command={lastExecution.Command}");
        builder.AppendLine($"stdout={TrimForOutput(lastExecution.StdOut, 1200)}");
        builder.AppendLine($"stderr={TrimForOutput(lastExecution.StdErr, 1200)}");
        builder.AppendLine();
        builder.AppendLine("[최근 루프 로그]");
        builder.AppendLine(string.IsNullOrWhiteSpace(recentLogs) ? "(none)" : recentLogs);
        builder.AppendLine();
        builder.AppendLine("[워크스페이스 스냅샷]");
        builder.AppendLine(workspaceSnapshot);
        builder.AppendLine();
        builder.AppendLine("반드시 JSON 객체만 출력하라. 마크다운/설명 금지.");
        builder.AppendLine("스키마:");
        builder.AppendLine("{\"analysis\":\"...\",\"done\":false,\"final_message\":\"...\",\"actions\":[{\"type\":\"mkdir\",\"path\":\"상대경로\",\"content\":\"...\",\"command\":\"...\"}]}");
        builder.AppendLine("type 허용값: mkdir, write_file, append_file, read_file, delete_file, run");
        builder.AppendLine("주의: type을 `mkdir|write_file`처럼 합치지 말고 반드시 단일 값만 사용");
        builder.AppendLine($"제약: actions 최대 {Math.Max(1, maxActions)}개");
        builder.AppendLine("제공자/모델 힌트:");
        foreach (var rule in BuildProviderModelPromptRuleLines(provider, model))
        {
            builder.AppendLine(rule);
        }

        builder.AppendLine("언어별 힌트:");
        foreach (var rule in BuildLanguagePromptRuleLines(provider, model, resolvedLanguage, objective))
        {
            builder.AppendLine(rule);
        }

        builder.AppendLine("검증 규칙:");
        foreach (var rule in BuildCodingVerificationRuleLines())
        {
            builder.AppendLine(rule);
        }

        builder.AppendLine("규칙:");
        builder.AppendLine("- analysis에는 이번 반복에서 무엇을 만들고 어떤 파일을 건드릴지 짧게 적는다");
        builder.AppendLine("- done=false 이면 actions에 최소 1개의 실질 액션(mkdir/write_file/append_file/read_file/delete_file/run)을 반드시 넣는다");
        builder.AppendLine("- 경로는 가능하면 상대경로 사용");
        builder.AppendLine("- run은 마지막 검증 단계에서 1회만 수행되므로 중간 반복에서는 가능한 한 넣지 말고 파일 생성/수정에 집중");
        builder.AppendLine("- JSON 문자열 내부 줄바꿈은 반드시 \\n 으로 이스케이프");
        builder.AppendLine("- path 값에는 줄바꿈, 탭, 마크다운 bullet, 따옴표 래핑을 넣지 말고 순수 상대경로만 넣는다");
        builder.AppendLine("- content는 실제 파일 내용 그대로 넣고, 문자열 리터럴이나 파일명 중간에 임의 줄바꿈을 넣지 않는다");
        builder.AppendLine("- 가능한 한 한 번에 필요한 파일을 모두 생성하고, 마지막 검증(run) 1회만 수행");
        builder.AppendLine("- 실행 성공 및 요구사항 충족 시 즉시 done=true, actions=[]");
        builder.AppendLine("- 오류가 있으면 원인 수정 액션을 포함");
        builder.AppendLine("- 목표 달성 시 done=true");
        return builder.ToString().Trim();
    }

    private static string BuildCodingAgentObjectivePrompt(string input, string languageHint, string modeLabel)
    {
        var resolvedLanguage = ResolveInitialCodingLanguage(languageHint, input);
        var languageRules = BuildLanguagePromptRuleLines(string.Empty, string.Empty, resolvedLanguage, input);
        var builder = new StringBuilder();
        builder.AppendLine("목표: 사용자의 코딩 요청을 로컬 프로젝트에서 실제로 완성하세요.");
        builder.AppendLine($"모드: {modeLabel}");
        builder.AppendLine($"언어 힌트: {resolvedLanguage}");
        builder.AppendLine("요구사항:");
        builder.AppendLine("- 기존 파일 구조를 우선 존중하고 필요한 최소 범위만 수정");
        builder.AppendLine("- 필요한 폴더/파일 생성 및 수정");
        builder.AppendLine("- 빌드/컴파일/실행/테스트 수행");
        builder.AppendLine("- 오류 발생 시 원인 분석 후 수정 반복");
        builder.AppendLine("- 더미 구현, TODO만 남기는 미완성 결과, 가짜 성공 보고 금지");
        builder.AppendLine("- 실패한 명령을 같은 형태로 반복하지 말고 원인을 바꿔 수정");
        builder.AppendLine("- 완료 보고 전에 최종 실행 1회와 생성/수정 파일 존재 여부를 확인");
        builder.AppendLine("- 요청에 출력값이 있으면 stdout도 실제 결과로 확인");
        builder.AppendLine("- 최종 요약에는 실제 변경 내용과 검증 결과만 반영");
        builder.AppendLine("- 최종적으로 실행 가능한 상태를 목표로 진행");
        if (languageRules.Count > 0)
        {
            builder.AppendLine("- 아래 언어별 제약을 같이 만족");
            foreach (var rule in languageRules)
            {
                builder.AppendLine(rule);
            }
        }

        builder.AppendLine();
        builder.AppendLine("사용자 요청:");
        builder.AppendLine(input ?? string.Empty);
        return builder.ToString().Trim();
    }

    private static string BuildDraftCodingWorkerPrompt(string objective, string languageHint)
    {
        var resolvedLanguage = ResolveInitialCodingLanguage(languageHint, objective);
        var builder = new StringBuilder();
        builder.AppendLine("너는 병렬 코딩 워커 초안 생성기다.");
        builder.AppendLine("실제 파일 수정/삭제/실행은 하지 말고 최종 통합용 초안만 작성하라.");
        builder.AppendLine($"언어 힌트: {resolvedLanguage}");
        builder.AppendLine();
        builder.AppendLine("[작업 목표]");
        builder.AppendLine(objective ?? string.Empty);
        builder.AppendLine();
        builder.AppendLine("규칙:");
        builder.AppendLine("- 가장 가능성 높은 구현안 1개만 제시");
        builder.AppendLine("- 불필요한 서론, 사과, 메타 설명 금지");
        builder.AppendLine("- 필요한 파일은 상대경로로 적기");
        builder.AppendLine("- 더미 코드, TODO, 의사코드 금지");
        builder.AppendLine("- 마지막에는 반드시 LANGUAGE=<언어> 줄과 최소 1개의 코드블록 포함");
        builder.AppendLine("- 여러 파일이 필요하면 `FILE: 상대경로` 줄 다음에 코드블록을 이어서 작성");
        foreach (var rule in BuildLanguagePromptRuleLines(string.Empty, string.Empty, resolvedLanguage, objective ?? string.Empty))
        {
            builder.AppendLine(rule);
        }

        builder.AppendLine();
        builder.AppendLine("출력 형식:");
        builder.AppendLine("[요약]");
        builder.AppendLine("- 핵심 구현 전략");
        builder.AppendLine("[파일]");
        builder.AppendLine("- 상대경로: 역할");
        builder.AppendLine("[리스크]");
        builder.AppendLine("- 남는 위험 또는 검증 포인트");
        builder.AppendLine("LANGUAGE=<언어>");
        builder.AppendLine("FILE: <핵심 파일 상대경로>");
        builder.AppendLine("```<언어>");
        builder.AppendLine("// 핵심 코드");
        builder.AppendLine("```");
        return builder.ToString().Trim();
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static bool GetBoolProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => value.GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true,
            _ => false
        };
    }

    private static string GuessLanguageFromPath(string path, string fallback)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return fallback;
        }

        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".py" => "python",
            ".js" or ".mjs" => "javascript",
            ".ts" => "javascript",
            ".c" => "c",
            ".cpp" or ".cc" or ".cxx" => "cpp",
            ".cs" => "csharp",
            ".java" => "java",
            ".kt" => "kotlin",
            ".html" or ".htm" => "html",
            ".css" => "css",
            ".sh" => "bash",
            _ => fallback
        };
    }

    private static string BuildOrchestrationCodingAggregatePrompt(
        string input,
        IReadOnlyList<CodingWorkerResult> workers,
        string languageHint
    )
    {
        return BuildParallelCodingAggregatePrompt(
            "오케스트레이션 통합",
            input,
            workers,
            languageHint,
            "역할별 초안의 장점을 취합해 한 번에 완성도 높은 최종 구현으로 정리"
        );
    }

    private static string BuildMultiCodingAggregatePrompt(
        string input,
        IReadOnlyList<CodingWorkerResult> workers,
        string languageHint
    )
    {
        return BuildParallelCodingAggregatePrompt(
            "다중 코딩 통합",
            input,
            workers,
            languageHint,
            "여러 모델 초안을 비교해 가장 안정적인 구현안을 선택하고 부족한 부분은 보완"
        );
    }

    private static string BuildParallelCodingAggregatePrompt(
        string modeLabel,
        string input,
        IReadOnlyList<CodingWorkerResult> workers,
        string languageHint,
        string strategyHint
    )
    {
        var builder = new StringBuilder();
        builder.AppendLine($"다음은 {modeLabel} 단계에서 수집한 병렬 워커 초안들입니다.");
        builder.AppendLine($"요청: {input}");
        builder.AppendLine($"언어 힌트: {languageHint}");
        builder.AppendLine($"통합 전략: {strategyHint}");
        builder.AppendLine();
        foreach (var worker in workers)
        {
            builder.AppendLine($"[Worker {worker.Provider}:{worker.Model}]");
            builder.AppendLine($"status={worker.Execution.Status} exit={worker.Execution.ExitCode} language={worker.Language}");
            if (worker.ChangedFiles.Count > 0)
            {
                builder.AppendLine("changed_files=" + string.Join(", ", worker.ChangedFiles.Take(6)));
            }

            var draft = (worker.RawResponse ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(draft))
            {
                builder.AppendLine("draft:");
                builder.AppendLine(TrimForOutput(draft, 2200));
            }
            else if (!string.IsNullOrWhiteSpace(worker.Code))
            {
                builder.AppendLine("code:");
                builder.AppendLine(TrimForOutput(worker.Code, 2200));
            }

            builder.AppendLine();
        }

        builder.AppendLine("요구사항:");
        builder.AppendLine("1) 워커 초안을 참고하되 그대로 복붙하지 말고 충돌과 중복을 정리");
        builder.AppendLine("2) 실제 워크스페이스에는 필요한 최소 파일만 생성/수정");
        builder.AppendLine("3) 더미 구현, TODO만 남는 미완성 결과, 가짜 성공 보고 금지");
        builder.AppendLine("4) 최종 실행/검증까지 마칠 수 있는 현실적인 구현을 선택");
        return builder.ToString().Trim();
    }

    private static string BuildMultiCodingSummaryPrompt(string originalInput, IReadOnlyList<CodingWorkerResult> workers)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"원본 요청: {originalInput}");
        builder.AppendLine();
        builder.AppendLine("아래 각 모델의 독립 코딩 결과를 비교해 공통점과 차이를 정리하세요.");
        builder.AppendLine("출력 형식:");
        builder.AppendLine("[공통 요약]");
        builder.AppendLine("- 여러 결과를 한 번에 이해할 수 있는 짧은 요약");
        builder.AppendLine("[공통점]");
        builder.AppendLine("- 대부분 결과가 공통으로 만족한 구현/검증 포인트");
        builder.AppendLine("[차이]");
        builder.AppendLine("- 모델별로 갈린 구현 방식, 검증 결과, 리스크");
        builder.AppendLine("[추천]");
        builder.AppendLine("- 어떤 결과를 우선 볼지, 어떤 차이를 주의할지");
        builder.AppendLine();
        builder.AppendLine("규칙:");
        builder.AppendLine("- 실행 상태와 변경 파일, 검증 명령, 요약을 근거로 작성");
        builder.AppendLine("- 공통점이 거의 없으면 [공통점]에 '공통점 없음'이라고 적기");
        builder.AppendLine("- 차이가 거의 없으면 [차이]에 '의미 있는 차이 없음'이라고 적기");
        builder.AppendLine("- 추천은 2~4줄 이내로 간결하게 작성");
        builder.AppendLine();
        foreach (var worker in workers)
        {
            builder.AppendLine($"[{worker.Provider}:{worker.Model}]");
            builder.AppendLine(BuildCodingWorkerDigest(worker));
            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }

    private static string RemoveCodeBlocksFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        normalized = Regex.Replace(normalized, "```[\\s\\S]*?```", "[코드 블록 숨김]");
        normalized = Regex.Replace(normalized, @"(?is)\[code\][\s\S]*?(?=\n\[[^\n]+\]|$)", "[code] (숨김)\n");
        normalized = Regex.Replace(normalized, @"(?is)<code[^>]*>[\s\S]*?</code>", "[코드 숨김]");
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
        return normalized.Trim();
    }

    private async Task<IReadOnlyList<string>> GetAvailableProvidersAsync(CancellationToken cancellationToken)
    {
        return await _providerRegistry.GetAvailableProvidersAsync(cancellationToken);
    }

    private static string NormalizeLanguageForCode(string? language)
    {
        var value = (language ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            "py" or "python3" => "python",
            "js" or "node" => "javascript",
            "sh" or "shell" => "bash",
            "c++" or "cc" => "cpp",
            "cs" or "c#" => "csharp",
            "kt" => "kotlin",
            "htm" => "html",
            "" or "auto" => "python",
            _ => value
        };
    }

    private static string NormalizeCodingLanguageHintPreservingAuto(string? languageHint)
    {
        var raw = (languageHint ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(raw) || raw == "auto")
        {
            return "auto";
        }

        return NormalizeLanguageForCode(raw);
    }

    private static string ResolveExplicitObjectiveLanguage(string? objective)
    {
        var text = ExtractLatestCodingRequestText(WebUtility.HtmlDecode(objective ?? string.Empty)).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (ContainsAny(text, "파이썬", "python"))
        {
            return "python";
        }

        if (ContainsAny(text, "자바스크립트", "javascript", "node.js", "nodejs"))
        {
            return "javascript";
        }

        if (ContainsAny(text, "c#", "csharp", "dotnet", "asp.net"))
        {
            return "csharp";
        }

        if (ContainsAny(text, "c++", "cpp"))
        {
            return "cpp";
        }

        if (ContainsAny(text, "html"))
        {
            return "html";
        }

        if (ContainsAny(text, "css"))
        {
            return "css";
        }

        if (ContainsAny(text, "코틀린", "kotlin", "안드로이드"))
        {
            return "kotlin";
        }

        if (Regex.IsMatch(text, @"(?<![a-z])java(?!script)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            || Regex.IsMatch(text, @"자바(?!스크립트)", RegexOptions.CultureInvariant)
            || ContainsAny(text, "spring"))
        {
            return "java";
        }

        if (ContainsAny(text, "c언어", "gcc", "clang"))
        {
            return "c";
        }

        if (ContainsAny(text, "bash", "shell", "쉘"))
        {
            return "bash";
        }

        return string.Empty;
    }

    private static string ResolveInitialCodingLanguage(string? languageHint, string objective)
    {
        var rawHint = (languageHint ?? string.Empty).Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(rawHint) && rawHint != "auto")
        {
            return NormalizeLanguageForCode(rawHint);
        }

        var explicitLanguage = ResolveExplicitObjectiveLanguage(objective);
        if (!string.IsNullOrWhiteSpace(explicitLanguage))
        {
            return explicitLanguage;
        }

        var text = ExtractLatestCodingRequestText(WebUtility.HtmlDecode(objective ?? string.Empty)).ToLowerInvariant();
        if (ContainsAny(text, "html", "css", "javascript", "js", "ui", "웹", "frontend", "react", "vue", "next", "클론"))
        {
            return "html";
        }

        if (ContainsAny(text, "c#", "dotnet", "asp.net"))
        {
            return "csharp";
        }

        if (ContainsAny(text, "kotlin", "안드로이드"))
        {
            return "kotlin";
        }

        if (ContainsAny(text, "java", "spring"))
        {
            return "java";
        }

        if (ContainsAny(text, "c++", "cpp"))
        {
            return "cpp";
        }

        if (ContainsAny(text, " c ", " gcc ", "clang", "c언어"))
        {
            return "c";
        }

        if (ContainsAny(text, "bash", "shell", "스크립트"))
        {
            return "bash";
        }

        return "python";
    }
}
