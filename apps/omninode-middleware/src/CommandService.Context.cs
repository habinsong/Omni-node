namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    public Task<ProjectContextSnapshot> ScanProjectContextAsync(CancellationToken cancellationToken)
        => _contextAppService.ScanProjectContextAsync(cancellationToken);

    public Task<SkillManifestListResult> ListSkillsAsync(CancellationToken cancellationToken)
        => _contextAppService.ListSkillsAsync(cancellationToken);

    public Task<CommandTemplateListResult> ListCommandsAsync(CancellationToken cancellationToken)
        => _contextAppService.ListCommandsAsync(cancellationToken);
}
