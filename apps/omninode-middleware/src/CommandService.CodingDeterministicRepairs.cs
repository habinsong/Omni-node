namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    private async Task<(bool Applied, string Language, string Code, string[] ChangedPaths, CodeExecutionResult Execution, string Note)> TryApplyDeterministicStructuredMultiFileRepairAsync(
        string objective,
        string languageHint,
        string workspaceRoot,
        IReadOnlyList<string> requestedPaths,
        CancellationToken cancellationToken
    )
    {
        if (!CodingDeterministicStructuredRepairPolicy.TryBuildPlan(objective, languageHint, requestedPaths, out var plan))
        {
            return (
                false,
                CodingLanguagePolicy.NormalizeLanguageForCode(languageHint),
                string.Empty,
                Array.Empty<string>(),
                new CodeExecutionResult("bash", workspaceRoot, "-", "(skipped)", 0, string.Empty, string.Empty, "skipped"),
                string.Empty
            );
        }

        var changedPaths = new List<string>();
        var primaryCode = string.Empty;
        foreach (var file in plan.Files)
        {
            var writeResult = await ExecuteCodingLoopActionAsync(
                new CodingLoopAction("write_file", file.Path, file.Content, string.Empty),
                workspaceRoot,
                requestedPaths,
                string.Empty,
                cancellationToken
            );
            if (writeResult.Changed && !string.IsNullOrWhiteSpace(writeResult.ChangedPath))
            {
                changedPaths.Add(writeResult.ChangedPath);
            }

            if (string.IsNullOrWhiteSpace(primaryCode)
                && string.Equals(Path.GetFileName(file.Path), Path.GetFileName(plan.PrimaryPath), StringComparison.OrdinalIgnoreCase))
            {
                primaryCode = file.Content;
            }
        }

        if (changedPaths.Count == 0)
        {
            return (
                false,
                plan.Language,
                primaryCode,
                Array.Empty<string>(),
                new CodeExecutionResult("bash", workspaceRoot, "-", "(skipped)", 0, string.Empty, string.Empty, "skipped"),
                plan.Note
            );
        }

        var expectedOutput = CodingFallbackPolicy.ExtractExpectedConsoleOutput(objective);
        var displayCommand = BuildVerificationDisplayCommand(plan.Language, changedPaths, workspaceRoot, objective, requestedPaths, expectedOutput);
        var command = BuildVerificationCommand(plan.Language, changedPaths, workspaceRoot, objective, requestedPaths, expectedOutput);
        if (string.IsNullOrWhiteSpace(command))
        {
            return (
                true,
                plan.Language,
                primaryCode,
                changedPaths.ToArray(),
                new CodeExecutionResult(
                    "bash",
                    workspaceRoot,
                    "-",
                    string.IsNullOrWhiteSpace(displayCommand) ? "(skipped)" : displayCommand,
                    0,
                    "결정론적 복구 후 검증 명령을 만들지 못했습니다.",
                    string.Empty,
                    "skipped"
                ),
                plan.Note
            );
        }

        var shell = await RunWorkspaceCommandWithAutoInstallAsync(command, workspaceRoot, cancellationToken);
        var execution = new CodeExecutionResult(
            "bash",
            workspaceRoot,
            "-",
            displayCommand,
            shell.ExitCode,
            shell.StdOut,
            shell.StdErr,
            shell.TimedOut ? "timeout" : (shell.ExitCode == 0 ? "ok" : "error")
        );
        return (true, plan.Language, primaryCode, changedPaths.ToArray(), execution, plan.Note);
    }

    private async Task<AutonomousCodingOutcome?> TryRecoverCodingLoopExceptionAsync(
        Exception exception,
        string objective,
        string languageHint,
        string workspaceRoot,
        IReadOnlyList<string> requestedPaths,
        CancellationToken cancellationToken
    )
    {
        var deterministicRepair = await TryApplyDeterministicStructuredMultiFileRepairAsync(
            objective,
            languageHint,
            workspaceRoot,
            requestedPaths,
            cancellationToken
        );
        if (deterministicRepair.Applied && string.Equals(deterministicRepair.Execution.Status, "ok", StringComparison.OrdinalIgnoreCase))
        {
            return new AutonomousCodingOutcome(
                deterministicRepair.Language,
                deterministicRepair.Code,
                $"[exception-recovery]\n{exception.Message}",
                deterministicRepair.Execution,
                deterministicRepair.ChangedPaths,
                BuildAutonomousCodingSummary(
                    new[] { "exception_recovery=structured_multi_file" },
                    deterministicRepair.ChangedPaths,
                    deterministicRepair.Execution,
                    1
                )
            );
        }

        var recoveredFiles = CollectWorkspaceMaterializedFiles(workspaceRoot);
        if (recoveredFiles.Count == 0)
        {
            return null;
        }

        var resolvedLanguage = CodingLanguagePolicy.ResolveFinalResultLanguage(
            CodingLanguagePolicy.ResolveInitialCodingLanguage(languageHint, objective),
            languageHint,
            objective,
            recoveredFiles
        );
        var expectedOutput = CodingFallbackPolicy.ExtractExpectedConsoleOutput(objective);
        var displayCommand = BuildVerificationDisplayCommand(
            resolvedLanguage,
            recoveredFiles,
            workspaceRoot,
            objective,
            requestedPaths,
            expectedOutput
        );
        var command = BuildVerificationCommand(
            resolvedLanguage,
            recoveredFiles,
            workspaceRoot,
            objective,
            requestedPaths,
            expectedOutput
        );
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var shell = await RunWorkspaceCommandWithAutoInstallAsync(command, workspaceRoot, cancellationToken);
        var execution = new CodeExecutionResult(
            "bash",
            workspaceRoot,
            "-",
            displayCommand,
            shell.ExitCode,
            shell.StdOut,
            shell.StdErr,
            shell.TimedOut ? "timeout" : (shell.ExitCode == 0 ? "ok" : "error")
        );
        if (!string.Equals(execution.Status, "ok", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var preferredPath = requestedPaths
            .Select(path => ResolveWorkspacePath(workspaceRoot, path))
            .FirstOrDefault(path => File.Exists(path))
            ?? recoveredFiles.FirstOrDefault(path => File.Exists(path))
            ?? string.Empty;
        var recoveredCode = string.Empty;
        if (!string.IsNullOrWhiteSpace(preferredPath))
        {
            try
            {
                recoveredCode = await File.ReadAllTextAsync(preferredPath, cancellationToken);
            }
            catch
            {
            }
        }

        return new AutonomousCodingOutcome(
            resolvedLanguage,
            recoveredCode,
            $"[exception-recovery]\n{exception.Message}",
            execution,
            recoveredFiles,
            BuildAutonomousCodingSummary(
                new[] { "exception_recovery=workspace_verification" },
                recoveredFiles,
                execution,
                1
            )
        );
    }
}
