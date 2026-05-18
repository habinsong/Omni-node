using System.Text;

namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    public Task<NotebookActionResult> GetNotebookAsync(string? projectKey, CancellationToken cancellationToken)
        => _notebookAppService.GetNotebookAsync(projectKey, cancellationToken);

    public Task<NotebookActionResult> AppendLearningAsync(string? projectKey, string content, CancellationToken cancellationToken)
        => _notebookAppService.AppendLearningAsync(projectKey, content, cancellationToken);

    public Task<NotebookActionResult> AppendDecisionAsync(string? projectKey, string content, CancellationToken cancellationToken)
        => _notebookAppService.AppendDecisionAsync(projectKey, content, cancellationToken);

    public Task<NotebookActionResult> AppendVerificationAsync(string? projectKey, string content, CancellationToken cancellationToken)
        => _notebookAppService.AppendVerificationAsync(projectKey, content, cancellationToken);

    public Task<NotebookActionResult> CreateHandoffAsync(string? projectKey, CancellationToken cancellationToken)
        => _notebookAppService.CreateHandoffAsync(projectKey, cancellationToken);

    public Task<string> BuildNotebookContextBlockAsync(string? projectKey, CancellationToken cancellationToken)
        => _notebookAppService.BuildNotebookContextBlockAsync(projectKey, cancellationToken);

    private async Task<string> ExecuteNotebookSlashCommandAsync(
        IReadOnlyList<string> tokens,
        string source,
        CancellationToken cancellationToken
    )
    {
        _ = source;
        if (tokens.Count == 1 || tokens[1].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            return """
                   [노트북 명령]
                   자연어 예시:
                   - "노트북 보여줘"
                   - "노트북에 decision 계획은 task graph로 실행한다고 기록해줘"
                   - "인수인계 문서 만들어줘"

                   정확히 제어할 때:
                   /notebook show [project-key]
                   /notebook append <learning|decision|verification> <내용>
                   /handoff [project-key]
                   """;
        }

        var action = tokens[1].Trim().ToLowerInvariant();
        if (action is "show" or "get")
        {
            var result = await GetNotebookAsync(tokens.Count >= 3 ? tokens[2] : null, cancellationToken);
            return FormatNotebookActionResult(result);
        }

        if (action == "append")
        {
            if (tokens.Count < 4)
            {
                return "사용법: /notebook append <learning|decision|verification> <내용>";
            }

            var kind = tokens[2].Trim().ToLowerInvariant();
            var content = string.Join(' ', tokens.Skip(3)).Trim();
            NotebookActionResult result = kind switch
            {
                "learning" => await AppendLearningAsync(null, content, cancellationToken),
                "decision" => await AppendDecisionAsync(null, content, cancellationToken),
                "verification" => await AppendVerificationAsync(null, content, cancellationToken),
                _ => new NotebookActionResult(false, "kind는 learning, decision, verification 중 하나여야 합니다.", null)
            };
            return FormatNotebookActionResult(result);
        }

        return "알 수 없는 /notebook 명령입니다. /notebook help를 확인하세요.";
    }

    private async Task<string> ExecuteHandoffSlashCommandAsync(
        IReadOnlyList<string> tokens,
        string source,
        CancellationToken cancellationToken
    )
    {
        _ = source;
        if (tokens.Count >= 2 && tokens[1].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            return """
                   [인수인계 명령]
                   /handoff [project-key]
                   """;
        }

        var projectKey = tokens.Count >= 2 ? tokens[1] : null;
        var result = await CreateHandoffAsync(projectKey, cancellationToken);
        return FormatNotebookActionResult(result);
    }

    private static string FormatNotebookActionResult(NotebookActionResult result)
    {
        if (!result.Ok)
        {
            return $"error: {result.Message}";
        }

        if (result.Snapshot == null)
        {
            return result.Message;
        }

        return $"{result.Message}\n{FormatNotebookSnapshot(result.Snapshot)}";
    }

    private static string FormatNotebookSnapshot(ProjectNotebookSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"projectKey={snapshot.Notebook.ProjectKey}");
        builder.AppendLine($"rootPath={snapshot.Notebook.RootPath}");
        builder.AppendLine($"readAt={snapshot.ReadAtUtc}");
        AppendDocumentSummary(builder, "learnings", snapshot.Learnings);
        AppendDocumentSummary(builder, "decisions", snapshot.Decisions);
        AppendDocumentSummary(builder, "verification", snapshot.Verification);
        AppendDocumentSummary(builder, "handoff", snapshot.Handoff);
        return builder.ToString().TrimEnd();
    }

    private static void AppendDocumentSummary(StringBuilder builder, string label, NotebookDocumentSnapshot document)
    {
        builder.AppendLine(
            $"- {label}: exists={(document.Exists ? "yes" : "no")} size={document.SizeBytes} updated={document.UpdatedAtUtc}"
        );
        if (!string.IsNullOrWhiteSpace(document.Preview))
        {
            builder.AppendLine($"  preview: {TrimPlanText(document.Preview.Replace('\n', ' '), 220)}");
        }
    }
}
