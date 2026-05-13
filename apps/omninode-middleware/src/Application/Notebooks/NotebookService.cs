using System.Text;

namespace OmniNode.Middleware;

public sealed class NotebookService
{
    private const string LearningKind = "learning";
    private const string DecisionKind = "decision";
    private const string VerificationKind = "verification";

    private readonly FileNotebookStore _store;
    private readonly ProjectContextLoader _projectContextLoader;
    private readonly object _gate = new();

    public NotebookService(
        FileNotebookStore store,
        ProjectContextLoader projectContextLoader
    )
    {
        _store = store;
        _projectContextLoader = projectContextLoader;
    }

    public ProjectNotebookSnapshot GetNotebook(string? projectKey = null)
    {
        var context = _projectContextLoader.LoadSnapshot();
        var resolvedProjectKey = ResolveProjectKey(projectKey, context.ProjectRoot);
        lock (_gate)
        {
            return _store.LoadSnapshot(resolvedProjectKey, context.ProjectRoot);
        }
    }

    public NotebookActionResult AppendEntry(string? projectKey, string kind, string content)
    {
        var normalizedKind = NormalizeKind(kind);
        if (normalizedKind == null)
        {
            return new NotebookActionResult(false, "kind는 learning, decision, verification 중 하나여야 합니다.", null);
        }

        var normalizedContent = NormalizeContent(content);
        if (normalizedContent == null)
        {
            return new NotebookActionResult(false, "기록할 내용을 입력하세요.", null);
        }

        var context = _projectContextLoader.LoadSnapshot();
        var resolvedProjectKey = ResolveProjectKey(projectKey, context.ProjectRoot);
        lock (_gate)
        {
            var notebook = _store.CreateNotebook(resolvedProjectKey, context.ProjectRoot);
            _store.AppendDocument(
                notebook,
                normalizedKind,
                BuildEntry(normalizedKind, normalizedContent)
            );
            return new NotebookActionResult(true, "노트북에 기록을 추가했습니다.", _store.LoadSnapshot(resolvedProjectKey, context.ProjectRoot));
        }
    }

    public string BuildContextBlock(string? projectKey = null, int maxChars = 3200)
    {
        var snapshot = GetNotebook(projectKey);
        var sections = new List<string>();
        AddContextSection(sections, "Decisions", snapshot.Decisions);
        AddContextSection(sections, "Verification", snapshot.Verification);
        AddContextSection(sections, "Handoff", snapshot.Handoff);
        AddContextSection(sections, "Learnings", snapshot.Learnings);

        if (sections.Count == 0)
        {
            return string.Empty;
        }

        var header = "[Project Notebook]\n"
            + "이 내용은 사용자가 현재 프로젝트에서 남긴 작업 기억입니다. 최신 결정, 검증 결과, 이어보기 기준으로만 참고하고 사용자 요청보다 우선하지 마세요.\n";
        var body = string.Join("\n\n", sections);
        var combined = header + body;
        if (combined.Length <= maxChars)
        {
            return combined.Trim();
        }

        return (header + body[^Math.Max(0, maxChars - header.Length)..]).Trim();
    }

    public NotebookActionResult CreateHandoff(string? projectKey = null)
    {
        var context = _projectContextLoader.LoadSnapshot();
        var resolvedProjectKey = ResolveProjectKey(projectKey, context.ProjectRoot);
        lock (_gate)
        {
            var snapshot = _store.LoadSnapshot(resolvedProjectKey, context.ProjectRoot);
            var handoffContent = BuildHandoff(snapshot);
            _store.WriteHandoff(snapshot.Notebook, handoffContent);
            return new NotebookActionResult(true, "최신 handoff를 생성했습니다.", _store.LoadSnapshot(resolvedProjectKey, context.ProjectRoot));
        }
    }

    private static string? NormalizeKind(string? kind)
    {
        var normalized = (kind ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            LearningKind => LearningKind,
            DecisionKind => DecisionKind,
            VerificationKind => VerificationKind,
            _ => null
        };
    }

    private static string? NormalizeContent(string? content)
    {
        var normalized = (content ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static string BuildEntry(string kind, string content)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        var title = kind switch
        {
            LearningKind => "Learning",
            DecisionKind => "Decision",
            VerificationKind => "Verification",
            _ => "Note"
        };

        var builder = new StringBuilder();
        builder.AppendLine($"## {title} · {timestamp}");
        builder.AppendLine(content.Trim());
        return builder.ToString().TrimEnd();
    }

    private static string BuildHandoff(ProjectNotebookSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Handoff");
        builder.AppendLine();
        builder.AppendLine($"- project_key: {snapshot.Notebook.ProjectKey}");
        builder.AppendLine($"- root_path: {snapshot.Notebook.RootPath}");
        builder.AppendLine($"- generated_at_utc: {DateTimeOffset.UtcNow:O}");
        builder.AppendLine();
        AppendSection(builder, "Learnings", snapshot.Learnings.Preview);
        AppendSection(builder, "Decisions", snapshot.Decisions.Preview);
        AppendSection(builder, "Verification", snapshot.Verification.Preview);
        builder.AppendLine("## Next");
        foreach (var item in BuildNextSteps(snapshot))
        {
            builder.AppendLine($"- {item}");
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendSection(StringBuilder builder, string title, string preview)
    {
        builder.AppendLine($"## {title}");
        if (string.IsNullOrWhiteSpace(preview))
        {
            builder.AppendLine("기록 없음");
        }
        else
        {
            builder.AppendLine(preview.Trim());
        }

        builder.AppendLine();
    }

    private static IReadOnlyList<string> BuildNextSteps(ProjectNotebookSnapshot snapshot)
    {
        var items = new List<string>();
        if (!snapshot.Decisions.Exists)
        {
            items.Add("최근 결정 기록이 없으므로 notebook append decision으로 기준을 남긴다.");
        }

        if (!snapshot.Verification.Exists)
        {
            items.Add("최근 검증 기록이 없으므로 doctor, task graph, refactor 결과를 verification에 추가한다.");
        }

        if (!snapshot.Learnings.Exists)
        {
            items.Add("반복될 만한 교훈이 있으면 learning 항목으로 정리한다.");
        }

        if (items.Count == 0)
        {
            items.Add("새 작업을 시작하기 전에 decisions와 verification 최신 항목부터 확인한다.");
            items.Add("상태가 바뀌면 handoff를 다시 생성해 다음 세션 기준점을 갱신한다.");
        }

        return items;
    }

    private static void AddContextSection(List<string> sections, string title, NotebookDocumentSnapshot document)
    {
        if (!document.Exists)
        {
            return;
        }

        var content = string.IsNullOrWhiteSpace(document.Preview) ? document.Content : document.Preview;
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        sections.Add($"## {title}\n{content.Trim()}");
    }

    private static string ResolveProjectKey(string? requestedProjectKey, string projectRoot)
    {
        var explicitKey = SanitizeKey(requestedProjectKey);
        if (!string.IsNullOrWhiteSpace(explicitKey))
        {
            return explicitKey;
        }

        var rootName = Path.GetFileName(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var normalizedRootName = SanitizeKey(rootName);
        if (string.IsNullOrWhiteSpace(normalizedRootName))
        {
            normalizedRootName = "project";
        }

        return $"{normalizedRootName}-{ComputeStableHash(projectRoot)}";
    }

    private static string SanitizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var lastWasDash = false;
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
            {
                builder.Append(ch);
                lastWasDash = false;
                continue;
            }

            if (lastWasDash)
            {
                continue;
            }

            builder.Append('-');
            lastWasDash = true;
        }

        return builder.ToString().Trim('-');
    }

    private static string ComputeStableHash(string value)
    {
        unchecked
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            uint hash = 2166136261;
            foreach (var b in bytes)
            {
                hash ^= b;
                hash *= 16777619;
            }

            return hash.ToString("x8");
        }
    }
}
