namespace OmniNode.Middleware;

public sealed record ProjectNotebook(
    string ProjectKey,
    string RootPath,
    string LearningsPath,
    string DecisionsPath,
    string VerificationPath,
    string HandoffPath
);

public sealed record NotebookDocumentSnapshot(
    string Path,
    bool Exists,
    long SizeBytes,
    string UpdatedAtUtc,
    string Preview,
    string Content,
    bool ContentTruncated
);

public sealed record ProjectNotebookSnapshot(
    ProjectNotebook Notebook,
    NotebookDocumentSnapshot Learnings,
    NotebookDocumentSnapshot Decisions,
    NotebookDocumentSnapshot Verification,
    NotebookDocumentSnapshot Handoff,
    string ReadAtUtc
);

public sealed record NotebookActionResult(
    bool Ok,
    string Message,
    ProjectNotebookSnapshot? Snapshot
);
