namespace OmniNode.Middleware;

public sealed class ContextApplicationService : IContextApplicationService
{
    private readonly ProjectContextLoader _projectContextLoader;

    public ContextApplicationService(ProjectContextLoader projectContextLoader)
    {
        _projectContextLoader = projectContextLoader;
    }

    public Task<ProjectContextSnapshot> ScanProjectContextAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_projectContextLoader.LoadSnapshot());
    }

    public Task<SkillManifestListResult> ListSkillsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = _projectContextLoader.LoadSnapshot();
        return Task.FromResult(new SkillManifestListResult(
            snapshot.ProjectRoot,
            snapshot.CurrentDirectory,
            snapshot.Skills,
            snapshot.ScannedAtUtc
        ));
    }

    public Task<CommandTemplateListResult> ListCommandsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = _projectContextLoader.LoadSnapshot();
        return Task.FromResult(new CommandTemplateListResult(
            snapshot.ProjectRoot,
            snapshot.CurrentDirectory,
            snapshot.Commands,
            snapshot.ScannedAtUtc
        ));
    }
}
