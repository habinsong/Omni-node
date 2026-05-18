using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    private static readonly Regex BrowserIntentOpenVerbRegex = new(
        @"(?i)(열어\s*줘?|열어|띄워\s*줘?|띄워|접속(?:해|해줘|시켜줘)?|이동(?:해|해줘)?|방문(?:해|해줘)?|open|navigate|go\s+to|visit)"
    );
    private static readonly Regex BrowserIntentStartRegex = new(
        @"(?i)(브라우저|browser).*(켜\s*줘?|열어\s*줘?|시작|start|open)"
    );
    private static readonly Regex BrowserIntentStopRegex = new(
        @"(?i)(브라우저|browser).*(꺼\s*줘?|닫아\s*줘?|중지|종료|stop|close)"
    );
    private static readonly Regex BrowserIntentNewTabRegex = new(
        @"(?i)(새\s*탭|새창|new\s+tab)"
    );
    private static readonly Regex BrowserIntentTrailingPunctuationRegex = new(
        @"[)\].,!?;:'""，。！？、]+$"
    );

    private static readonly IReadOnlyList<(string Keyword, string Url, string Label)> BrowserIntentKnownSites =
        new (string Keyword, string Url, string Label)[]
        {
            ("네이버", "https://www.naver.com/", "네이버"),
            ("naver", "https://www.naver.com/", "네이버"),
            ("구글", "https://www.google.com/", "구글"),
            ("google", "https://www.google.com/", "구글"),
            ("유튜브", "https://www.youtube.com/", "유튜브"),
            ("유투브", "https://www.youtube.com/", "유튜브"),
            ("youtube", "https://www.youtube.com/", "유튜브"),
            ("다음", "https://www.daum.net/", "다음"),
            ("daum", "https://www.daum.net/", "다음"),
            ("카카오", "https://www.kakao.com/", "카카오"),
            ("kakao", "https://www.kakao.com/", "카카오"),
            ("깃허브", "https://github.com/", "GitHub"),
            ("github", "https://github.com/", "GitHub"),
            ("지메일", "https://mail.google.com/", "Gmail"),
            ("gmail", "https://mail.google.com/", "Gmail"),
            ("오픈ai", "https://openai.com/", "OpenAI"),
            ("openai", "https://openai.com/", "OpenAI")
        };

    private ConversationChatResult? TryHandleBrowserChatIntent(
        SessionContext session,
        string rawInput,
        string mode,
        string? requestId = null
    )
    {
        if (!TryParseBrowserIntent(rawInput, out var command))
        {
            return null;
        }

        var result = _browserTool.Execute(command.Action, command.Url);
        var assistantText = BuildBrowserIntentAssistantText(command, result);
        _conversationStore.AppendMessage(session.Thread.Id, "user", rawInput, "browser:intent");
        _conversationStore.AppendMessage(session.Thread.Id, "assistant", assistantText, "browser:intent");
        ScheduleConversationMaintenance(
            session.Thread.Id,
            $"{session.Scope}-{session.Mode}",
            "browser",
            result.Adapter
        );

        var updated = _conversationStore.Get(session.Thread.Id) ?? session.Thread;
        return new ConversationChatResult(
            mode,
            updated.Id,
            "browser",
            result.Adapter,
            assistantText,
            "browser:intent",
            updated,
            null,
            null,
            RequestId: requestId
        );
    }

    private ConversationMultiResult? TryHandleBrowserMultiIntent(
        SessionContext session,
        string rawInput,
        MultiChatRequest request
    )
    {
        if (!TryParseBrowserIntent(rawInput, out var command))
        {
            return null;
        }

        var result = _browserTool.Execute(command.Action, command.Url);
        var assistantText = BuildBrowserIntentAssistantText(command, result);
        _conversationStore.AppendMessage(session.Thread.Id, "user", rawInput, "browser:intent");
        _conversationStore.AppendMessage(session.Thread.Id, "assistant", assistantText, "browser:intent");
        ScheduleConversationMaintenance(
            session.Thread.Id,
            $"{session.Scope}-{session.Mode}",
            "browser",
            result.Adapter
        );

        var updated = _conversationStore.Get(session.Thread.Id) ?? session.Thread;
        var model = result.Adapter;
        return new ConversationMultiResult(
            updated.Id,
            assistantText,
            assistantText,
            assistantText,
            assistantText,
            assistantText,
            NormalizeModelSelection(request.GroqModel) ?? model,
            NormalizeModelSelection(request.GeminiModel) ?? model,
            NormalizeModelSelection(request.CerebrasModel) ?? model,
            NormalizeModelSelection(request.CopilotModel) ?? model,
            request.SummaryProvider ?? "browser",
            "browser",
            updated,
            null,
            null,
            CodexText: assistantText,
            CodexModel: NormalizeModelSelection(request.CodexModel) ?? model,
            CommonCore: assistantText,
            Differences: "",
            NvidiaText: assistantText,
            NvidiaModel: NormalizeModelSelection(request.NvidiaModel) ?? model
        );
    }

    private CodingRunResult? TryHandleBrowserCodingIntent(
        SessionContext session,
        string rawInput,
        string mode,
        string codingRunRoot,
        string language
    )
    {
        if (!TryParseBrowserIntent(rawInput, out var command))
        {
            return null;
        }

        Directory.CreateDirectory(codingRunRoot);
        var result = _browserTool.Execute(command.Action, command.Url);
        var assistantText = BuildBrowserIntentAssistantText(command, result);
        _conversationStore.AppendMessage(session.Thread.Id, "user", rawInput, "browser:intent");
        _conversationStore.AppendMessage(session.Thread.Id, "assistant", assistantText, "browser:intent");
        ScheduleConversationMaintenance(
            session.Thread.Id,
            $"{session.Scope}-{session.Mode}",
            "browser",
            result.Adapter
        );

        var updated = _conversationStore.Get(session.Thread.Id) ?? session.Thread;
        var execution = new CodeExecutionResult(
            "browser",
            codingRunRoot,
            "",
            BuildBrowserIntentCommandLabel(command, result),
            result.Ok ? 0 : 1,
            assistantText,
            result.Error ?? "",
            result.Ok ? "ok" : "error"
        );
        return new CodingRunResult(
            mode,
            updated.Id,
            "browser",
            result.Adapter,
            language,
            "",
            execution,
            Array.Empty<CodingWorkerResult>(),
            Array.Empty<string>(),
            assistantText,
            updated,
            null
        );
    }

    private static bool TryParseBrowserIntent(string input, out BrowserIntentCommand command)
    {
        command = default!;
        var text = (input ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (BrowserIntentStopRegex.IsMatch(text))
        {
            command = new BrowserIntentCommand("stop", null, "브라우저");
            return true;
        }

        var hasOpenVerb = BrowserIntentOpenVerbRegex.IsMatch(text);
        var hasBrowserWord = ContainsAnyIgnoreCase(text, "브라우저", "browser");
        var target = ResolveBrowserIntentTarget(text);
        if (target != null && (hasOpenVerb || hasBrowserWord))
        {
            var action = BrowserIntentNewTabRegex.IsMatch(text) ? "open" : "navigate";
            command = new BrowserIntentCommand(action, target.Value.Url, target.Value.Label);
            return true;
        }

        if (target == null && BrowserIntentStartRegex.IsMatch(text))
        {
            command = new BrowserIntentCommand("start", null, "브라우저");
            return true;
        }

        return false;
    }

    private static (string Url, string Label)? ResolveBrowserIntentTarget(string text)
    {
        var urlMatch = HttpUrlRegex.Match(text);
        if (urlMatch.Success)
        {
            var url = CleanBrowserIntentUrl(urlMatch.Value);
            return (url, url);
        }

        foreach (var site in BrowserIntentKnownSites)
        {
            if (text.Contains(site.Keyword, StringComparison.OrdinalIgnoreCase))
            {
                return (site.Url, site.Label);
            }
        }

        var domainMatch = DomainRegex.Match(text);
        if (domainMatch.Success)
        {
            var domain = CleanBrowserIntentUrl(domainMatch.Value);
            return ($"https://{domain}", domain);
        }

        return null;
    }

    private static string CleanBrowserIntentUrl(string value)
    {
        return BrowserIntentTrailingPunctuationRegex.Replace((value ?? string.Empty).Trim(), "");
    }

    private static bool ContainsAnyIgnoreCase(string text, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (!string.IsNullOrWhiteSpace(needle)
                && text.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildBrowserIntentAssistantText(BrowserIntentCommand command, BrowserToolResult result)
    {
        var targetText = string.IsNullOrWhiteSpace(command.Url)
            ? command.Label
            : command.Url;
        if (result.Ok && string.Equals(result.Adapter, "playwright", StringComparison.OrdinalIgnoreCase))
        {
            return command.Action switch
            {
                "stop" => "브라우저를 닫았습니다.",
                "start" => "브라우저를 열었습니다.",
                "open" => $"브라우저 새 탭에서 {targetText} 을(를) 열었습니다.",
                _ => $"브라우저에서 {targetText} 으로 이동했습니다."
            } + $"\n\nadapter=playwright running={(result.Running ? "true" : "false")} tabs={result.Tabs.Count}";
        }

        if (result.Ok)
        {
            return $"브라우저 요청은 처리했지만 실제 Chromium이 아니라 {result.Adapter} adapter로 기록됐습니다."
                + $"\n\n요청: {BuildBrowserIntentCommandLabel(command, result)}";
        }

        return $"브라우저 요청을 실행하지 못했습니다.\n\n요청: {BuildBrowserIntentCommandLabel(command, result)}\n오류: {result.Error ?? "-"}";
    }

    private static string BuildBrowserIntentCommandLabel(BrowserIntentCommand command, BrowserToolResult result)
    {
        var target = string.IsNullOrWhiteSpace(command.Url) ? "" : $" {command.Url}";
        return $"browser.{command.Action}{target} adapter={result.Adapter}";
    }

    private sealed record BrowserIntentCommand(
        string Action,
        string? Url,
        string Label
    );
}
