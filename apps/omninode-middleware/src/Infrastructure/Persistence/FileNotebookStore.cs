using System.Text;

namespace OmniNode.Middleware;

public sealed class FileNotebookStore
{
    private readonly IStatePathResolver _pathResolver;

    public FileNotebookStore(IStatePathResolver pathResolver)
    {
        _pathResolver = pathResolver;
    }

    public ProjectNotebook CreateNotebook(string projectKey, string rootPath)
    {
        var notebookRoot = _pathResolver.GetNotebookProjectRoot(projectKey);
        return new ProjectNotebook(
            projectKey,
            rootPath,
            Path.Combine(notebookRoot, "learnings.md"),
            Path.Combine(notebookRoot, "decisions.md"),
            Path.Combine(notebookRoot, "verification.md"),
            Path.Combine(notebookRoot, "handoff.md")
        );
    }

    public ProjectNotebookSnapshot LoadSnapshot(string projectKey, string rootPath)
    {
        var notebook = CreateNotebook(projectKey, rootPath);
        return new ProjectNotebookSnapshot(
            notebook,
            ReadDocument(notebook.LearningsPath),
            ReadDocument(notebook.DecisionsPath),
            ReadDocument(notebook.VerificationPath),
            ReadDocument(notebook.HandoffPath),
            DateTimeOffset.UtcNow.ToString("O")
        );
    }

    public void AppendDocument(ProjectNotebook notebook, string kind, string entry)
    {
        var path = ResolvePath(notebook, kind);
        var existing = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : string.Empty;
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(existing))
        {
            builder.Append(existing.TrimEnd());
            builder.AppendLine();
            builder.AppendLine();
        }

        builder.Append(entry.Trim());
        builder.AppendLine();
        AtomicFileStore.WriteAllText(path, builder.ToString(), ownerOnly: true);
    }

    public void WriteHandoff(ProjectNotebook notebook, string content)
    {
        AtomicFileStore.WriteAllText(notebook.HandoffPath, content.TrimEnd() + Environment.NewLine, ownerOnly: true);
    }

    private static string ResolvePath(ProjectNotebook notebook, string kind)
    {
        return kind switch
        {
            "learning" => notebook.LearningsPath,
            "decision" => notebook.DecisionsPath,
            "verification" => notebook.VerificationPath,
            _ => throw new InvalidOperationException("지원하지 않는 notebook kind입니다.")
        };
    }

    private static NotebookDocumentSnapshot ReadDocument(string path)
    {
        if (!File.Exists(path))
        {
            return new NotebookDocumentSnapshot(path, false, 0, string.Empty, string.Empty, string.Empty, false);
        }

        try
        {
            var content = File.ReadAllText(path, Encoding.UTF8);
            var info = new FileInfo(path);
            var contentForUi = BuildContent(content);
            return new NotebookDocumentSnapshot(
                path,
                true,
                info.Exists ? info.Length : Encoding.UTF8.GetByteCount(content),
                info.Exists ? info.LastWriteTimeUtc.ToString("O") : string.Empty,
                BuildPreview(content),
                contentForUi.Content,
                contentForUi.Truncated
            );
        }
        catch
        {
            return new NotebookDocumentSnapshot(path, true, 0, string.Empty, string.Empty, string.Empty, false);
        }
    }

    private static string BuildPreview(string content, int maxChars = 1800)
    {
        var normalized = (content ?? string.Empty).Trim();
        if (normalized.Length <= maxChars)
        {
            return normalized;
        }

        return "...(truncated)\n" + normalized[^maxChars..].TrimStart();
    }

    private static (string Content, bool Truncated) BuildContent(string content, int maxChars = 20000)
    {
        var normalized = (content ?? string.Empty).Trim();
        if (normalized.Length <= maxChars)
        {
            return (normalized, false);
        }

        return (normalized[^maxChars..].TrimStart(), true);
    }
}
