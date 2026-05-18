using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    private async Task<ShellRunResult> RunWorkspaceCommandWithAutoInstallAsync(
        string command,
        string workDir,
        CancellationToken cancellationToken,
        string? standardInput = null
    )
    {
        if (!IsDynamicCodeExecutionEnabled())
        {
            return new ShellRunResult(126, string.Empty, BuildDynamicCodeDisabledMessage(), false);
        }

        var installLogs = new List<string>();
        var installErrors = new List<string>();

        if (_execution.EnableAutoInstall)
        {
            await EnsureWorkspaceDependenciesAsync(command, workDir, installLogs, installErrors, cancellationToken);
        }
        var shell = await RunWorkspaceCommandAsync(command, workDir, cancellationToken, standardInput);

        if (_execution.EnableAutoInstall && !shell.TimedOut && shell.ExitCode != 0)
        {
            var retried = await TryInstallMissingDependencyFromErrorAsync(command, workDir, shell.StdErr, installLogs, installErrors, cancellationToken);
            if (retried)
            {
                shell = await RunWorkspaceCommandAsync(command, workDir, cancellationToken, standardInput);
            }
        }

        if (installLogs.Count == 0 && installErrors.Count == 0)
        {
            return shell;
        }

        var mergedStdOut = MergeInstallLogs("[auto-install]", installLogs, shell.StdOut);
        var mergedStdErr = MergeInstallLogs("[auto-install]", installErrors, shell.StdErr);
        return new ShellRunResult(shell.ExitCode, mergedStdOut, mergedStdErr, shell.TimedOut);
    }

    private async Task EnsureWorkspaceDependenciesAsync(
        string command,
        string workDir,
        List<string> logs,
        List<string> errors,
        CancellationToken cancellationToken
    )
    {
        try
        {
            if (LooksLikePythonCommand(command))
            {
                if (IsSyntaxOnlyPythonVerificationCommand(command))
                {
                    return;
                }

                var pythonBaseDir = ResolveDependencyBaseDirectory(command, workDir, ".py");
                var requirementsPath = FindRequirementsFile(pythonBaseDir, workDir);
                var pythonEnvironmentReady = await EnsureWorkspacePythonEnvironmentAsync(workDir, logs, errors, cancellationToken);

                if (!string.IsNullOrWhiteSpace(requirementsPath) && File.Exists(requirementsPath))
                {
                    var pipCommand = BuildPipRequirementsInstallCommand(requirementsPath);
                    var installResult = await RunWorkspaceCommandAsync(pipCommand, workDir, cancellationToken);
                    AppendInstallOutcome("requirements.txt 설치", pipCommand, installResult, logs, errors);
                }

                var pythonSourceFiles = EnumeratePythonDependencySourceFiles(command, pythonBaseDir, workDir).ToArray();
                if (pythonSourceFiles.Length > 0)
                {
                    var packages = CollectPythonThirdPartyPackagesFromSources(pythonSourceFiles);
                    if (packages.Count > 0)
                    {
                        var pipCommand = BuildPipPackageInstallCommand(packages);
                        var installResult = await RunWorkspaceCommandAsync(pipCommand, workDir, cancellationToken);
                        AppendInstallOutcome(
                            pythonEnvironmentReady ? "Python import 패키지 설치(.venv)" : "Python import 패키지 설치",
                            pipCommand,
                            installResult,
                            logs,
                            errors
                        );
                    }
                }
            }

            if (LooksLikeNodeCommand(command))
            {
                var nodeBaseDir = ResolveDependencyBaseDirectory(command, workDir, ".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx");
                var packageJsonPath = Path.Combine(nodeBaseDir, "package.json");
                if (File.Exists(packageJsonPath))
                {
                    var nodeModulesPath = Path.Combine(nodeBaseDir, "node_modules");
                    if (!Directory.Exists(nodeModulesPath))
                    {
                        var npmInstallCommand = "npm install --no-fund --no-audit";
                        var installResult = await RunWorkspaceCommandAsync(npmInstallCommand, nodeBaseDir, cancellationToken);
                        AppendInstallOutcome("package.json 의존성 설치", npmInstallCommand, installResult, logs, errors);
                    }
                }
                else if (HasNodeWorkspaceSignals(command, workDir))
                {
                    var initCommand = "npm init -y >/dev/null";
                    var initResult = await RunWorkspaceCommandAsync(initCommand, nodeBaseDir, cancellationToken);
                    AppendInstallOutcome("Node 임시 package.json 생성", initCommand, initResult, logs, errors);
                }

                var nodeScriptPath = TryExtractScriptPath(command, workDir, ".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx");
                if (!string.IsNullOrWhiteSpace(nodeScriptPath) && File.Exists(nodeScriptPath))
                {
                    var packages = ExtractNodePackagesFromSource(nodeScriptPath);
                    if (packages.Count > 0)
                    {
                        var npmCommand = $"npm install --no-save --no-fund --no-audit {string.Join(" ", packages.Select(EscapeShellArg))}";
                        var installResult = await RunWorkspaceCommandAsync(npmCommand, nodeBaseDir, cancellationToken);
                        AppendInstallOutcome("Node import 패키지 설치", npmCommand, installResult, logs, errors);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"의존성 자동 설치 파이프라인 내부 오류: {ex.Message}");
        }
    }

    private static IEnumerable<string> EnumeratePythonDependencySourceFiles(string command, string primaryDir, string workDir)
    {
        var scriptPath = TryExtractScriptPath(command, workDir, ".py");
        if (!string.IsNullOrWhiteSpace(scriptPath) && File.Exists(scriptPath))
        {
            yield return scriptPath;
        }

        var roots = new[] { primaryDir, workDir }
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var root in roots)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.py", SearchOption.AllDirectories)
                    .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.venv{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                                   && !path.Contains($"{Path.DirectorySeparatorChar}__pycache__{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                    .Take(80)
                    .ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }
    }

    private static bool IsSyntaxOnlyPythonVerificationCommand(string command)
    {
        var normalized = Regex.Replace((command ?? string.Empty).Trim(), @"\s+", " ");
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return normalized.Contains("python -m py_compile", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("python3 -m py_compile", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("python -m compileall", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("python3 -m compileall", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> TryInstallMissingDependencyFromErrorAsync(
        string command,
        string workDir,
        string stdErr,
        List<string> logs,
        List<string> errors,
        CancellationToken cancellationToken
    )
    {
        try
        {
            if (LooksLikePythonCommand(command))
            {
                var missingModule = ExtractPythonMissingModule(stdErr);
                if (IsPythonSystemModule(missingModule))
                {
                    var installedSystemModule = await TryInstallPythonSystemModuleAsync(missingModule!, workDir, logs, errors, cancellationToken);
                    if (installedSystemModule)
                    {
                        return true;
                    }
                }

                var pythonPackage = ResolvePythonPackageName(missingModule);
                if (!string.IsNullOrWhiteSpace(pythonPackage))
                {
                    await EnsureWorkspacePythonEnvironmentAsync(workDir, logs, errors, cancellationToken);
                    var pipCommand = BuildPipPackageInstallCommand(new[] { pythonPackage });
                    var installResult = await RunWorkspaceCommandAsync(pipCommand, workDir, cancellationToken);
                    AppendInstallOutcome($"Python 누락 모듈 설치({pythonPackage})", pipCommand, installResult, logs, errors);
                    if (installResult.ExitCode == 0)
                    {
                        return true;
                    }
                }
            }

            if (LooksLikeNodeCommand(command))
            {
                var missingSpecifier = ExtractNodeMissingModule(stdErr);
                var nodePackage = ResolveNodePackageName(missingSpecifier);
                if (!string.IsNullOrWhiteSpace(nodePackage))
                {
                    var nodeBaseDir = ResolveDependencyBaseDirectory(command, workDir, ".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx");
                    var npmCommand = $"npm install --no-save --no-fund --no-audit {EscapeShellArg(nodePackage)}";
                    var installResult = await RunWorkspaceCommandAsync(npmCommand, nodeBaseDir, cancellationToken);
                    AppendInstallOutcome($"Node 누락 모듈 설치({nodePackage})", npmCommand, installResult, logs, errors);
                    if (installResult.ExitCode == 0)
                    {
                        return true;
                    }
                }
            }

            var missingProgram = ExtractMissingProgram(stdErr);
            if (!string.IsNullOrWhiteSpace(missingProgram))
            {
                var installedProgram = await TryInstallMissingProgramAsync(command, workDir, missingProgram, logs, errors, cancellationToken);
                if (installedProgram)
                {
                    return true;
                }

                if (!ProgramInstallDenyList.Contains(missingProgram))
                {
                    var installed = await TryInstallProgramAsync(missingProgram, workDir, logs, errors, cancellationToken);
                    if (installed)
                    {
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"누락 의존성 자동 설치 오류: {ex.Message}");
        }

        return false;
    }

    private static string SafeReadAllText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task<bool> TryInstallMissingProgramAsync(
        string command,
        string workDir,
        string program,
        List<string> logs,
        List<string> errors,
        CancellationToken cancellationToken
    )
    {
        var safeProgram = SanitizeProgramName(program);
        if (string.IsNullOrWhiteSpace(safeProgram))
        {
            return false;
        }

        if (await TryInstallRuntimeToolchainAsync(safeProgram, workDir, logs, errors, cancellationToken))
        {
            return true;
        }

        if (await TryInstallNodeCliPackageAsync(command, workDir, safeProgram, logs, errors, cancellationToken))
        {
            return true;
        }

        if (await TryInstallPythonCliPackageAsync(command, workDir, safeProgram, logs, errors, cancellationToken))
        {
            return true;
        }

        return false;
    }

    private async Task<bool> TryInstallRuntimeToolchainAsync(
        string program,
        string workDir,
        List<string> logs,
        List<string> errors,
        CancellationToken cancellationToken
    )
    {
        var safeProgram = SanitizeProgramName(program);
        if (string.IsNullOrWhiteSpace(safeProgram))
        {
            return false;
        }

        if (IsNodeRuntimeProgram(safeProgram))
        {
            return await InstallNodeRuntimeToolchainAsync(safeProgram, workDir, logs, errors, cancellationToken);
        }

        if (IsPythonRuntimeProgram(safeProgram))
        {
            return await InstallPythonRuntimeToolchainAsync(safeProgram, workDir, logs, errors, cancellationToken);
        }

        return false;
    }

    private async Task<bool> TryInstallNodeCliPackageAsync(
        string command,
        string workDir,
        string program,
        List<string> logs,
        List<string> errors,
        CancellationToken cancellationToken
    )
    {
        var nodePackage = ResolveNodeCliPackageName(program);
        if (string.IsNullOrWhiteSpace(nodePackage)
            || (!LooksLikeNodeCommand(command) && !HasNodeWorkspaceSignals(command, workDir)))
        {
            return false;
        }

        var nodeBaseDir = ResolveDependencyBaseDirectory(command, workDir, ".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx");
        if (!await EnsureNodeToolchainAsync(nodeBaseDir, logs, errors, cancellationToken))
        {
            return false;
        }

        var packageJsonPath = Path.Combine(nodeBaseDir, "package.json");
        if (File.Exists(packageJsonPath))
        {
            var npmInstallCommand = "npm install --no-fund --no-audit";
            var packageInstallResult = await RunWorkspaceCommandAsync(npmInstallCommand, nodeBaseDir, cancellationToken);
            AppendInstallOutcome("package.json 의존성 설치", npmInstallCommand, packageInstallResult, logs, errors);
            if (packageInstallResult.ExitCode == 0 && File.Exists(Path.Combine(nodeBaseDir, "node_modules", ".bin", program)))
            {
                return true;
            }
        }

        var npmCommand = $"npm install --no-save --no-fund --no-audit {EscapeShellArg(nodePackage)}";
        var installResult = await RunWorkspaceCommandAsync(npmCommand, nodeBaseDir, cancellationToken);
        AppendInstallOutcome($"Node CLI 패키지 설치({nodePackage})", npmCommand, installResult, logs, errors);
        return installResult.ExitCode == 0;
    }

    private async Task<bool> TryInstallPythonCliPackageAsync(
        string command,
        string workDir,
        string program,
        List<string> logs,
        List<string> errors,
        CancellationToken cancellationToken
    )
    {
        var pythonPackage = ResolvePythonCliPackageName(program);
        if (string.IsNullOrWhiteSpace(pythonPackage)
            || (!LooksLikePythonCommand(command) && !HasPythonWorkspaceSignals(command, workDir)))
        {
            return false;
        }

        if (!await EnsurePythonToolchainAsync(workDir, logs, errors, cancellationToken))
        {
            return false;
        }

        await EnsureWorkspacePythonEnvironmentAsync(workDir, logs, errors, cancellationToken);
        var pipCommand = BuildPipPackageInstallCommand(new[] { pythonPackage });
        var installResult = await RunWorkspaceCommandAsync(pipCommand, workDir, cancellationToken);
        AppendInstallOutcome($"Python CLI 패키지 설치({pythonPackage})", pipCommand, installResult, logs, errors);
        return installResult.ExitCode == 0;
    }

    private async Task<bool> EnsureNodeToolchainAsync(
        string workDir,
        List<string> logs,
        List<string> errors,
        CancellationToken cancellationToken
    )
    {
        var checkResult = await RunWorkspaceCommandAsync("command -v node >/dev/null 2>&1 && command -v npm >/dev/null 2>&1", workDir, cancellationToken);
        if (checkResult.ExitCode == 0)
        {
            return true;
        }

        return await InstallNodeRuntimeToolchainAsync("npm", workDir, logs, errors, cancellationToken);
    }

    private async Task<bool> EnsurePythonToolchainAsync(
        string workDir,
        List<string> logs,
        List<string> errors,
        CancellationToken cancellationToken
    )
    {
        var checkCommand = OperatingSystem.IsWindows()
            ? "python --version >NUL 2>NUL && python -m pip --version >NUL 2>NUL"
            : "command -v python3 >/dev/null 2>&1 && python3 -m pip --version >/dev/null 2>&1";
        var checkResult = await RunWorkspaceCommandAsync(checkCommand, workDir, cancellationToken);
        if (checkResult.ExitCode == 0)
        {
            return true;
        }

        return await InstallPythonRuntimeToolchainAsync(
            OperatingSystem.IsWindows() ? "python" : "python3",
            workDir,
            logs,
            errors,
            cancellationToken
        );
    }

    private async Task<bool> EnsureWorkspacePythonEnvironmentAsync(
        string workDir,
        List<string> logs,
        List<string> errors,
        CancellationToken cancellationToken
    )
    {
        if (!await EnsurePythonToolchainAsync(workDir, logs, errors, cancellationToken))
        {
            return false;
        }

        var venvPython = OperatingSystem.IsWindows()
            ? Path.Combine(workDir, ".venv", "Scripts", "python.exe")
            : Path.Combine(workDir, ".venv", "bin", "python");
        if (File.Exists(venvPython))
        {
            return true;
        }

        var createVenv = OperatingSystem.IsWindows() ? "python -m venv .venv" : "python3 -m venv .venv";
        var venvResult = await RunWorkspaceCommandAsync(createVenv, workDir, cancellationToken);
        AppendInstallOutcome("Python 가상환경 생성(.venv)", createVenv, venvResult, logs, errors);
        if (venvResult.ExitCode != 0 || !File.Exists(venvPython))
        {
            return false;
        }

        var upgradePip = OperatingSystem.IsWindows()
            ? ".venv\\Scripts\\python.exe -m pip install --disable-pip-version-check --upgrade pip setuptools wheel"
            : ".venv/bin/python -m pip install --disable-pip-version-check --upgrade pip setuptools wheel";
        var pipResult = await RunWorkspaceCommandAsync(upgradePip, workDir, cancellationToken);
        AppendInstallOutcome("Python 가상환경 pip 준비(.venv)", upgradePip, pipResult, logs, errors);
        return pipResult.ExitCode == 0;
    }

    private async Task<bool> InstallNodeRuntimeToolchainAsync(
        string program,
        string workDir,
        List<string> logs,
        List<string> errors,
        CancellationToken cancellationToken
    )
    {
        if (OperatingSystem.IsMacOS())
        {
            var brewCheck = await RunWorkspaceCommandAsync("command -v brew >/dev/null 2>&1", workDir, cancellationToken);
            if (brewCheck.ExitCode != 0)
            {
                errors.Add($"Node 런타임 자동 설치 건너뜀: brew 없음 ({program})");
                return false;
            }

            var brewInstall = "brew install node";
            var installResult = await RunWorkspaceCommandAsync(brewInstall, workDir, cancellationToken);
            AppendInstallOutcome($"Node 런타임 설치({program})", brewInstall, installResult, logs, errors);
            if (installResult.ExitCode != 0)
            {
                return false;
            }

            if (string.Equals(program, "pnpm", StringComparison.OrdinalIgnoreCase)
                || string.Equals(program, "yarn", StringComparison.OrdinalIgnoreCase))
            {
                return await InstallNodePackageManagerAsync(program, workDir, logs, errors, cancellationToken);
            }

            return true;
        }

        if (OperatingSystem.IsLinux())
        {
            var linuxPackageManager = await DetectLinuxPackageManagerAsync(workDir, cancellationToken);
            if (linuxPackageManager != "apt-get")
            {
                errors.Add($"Node 런타임 자동 설치 건너뜀: apt-get 없음 ({program}, 감지됨: {linuxPackageManager})");
                return false;
            }

            var aptInstall = "if [ \"$(id -u)\" -eq 0 ]; then apt-get update && apt-get install -y nodejs npm; elif command -v sudo >/dev/null 2>&1; then sudo -n apt-get update && sudo -n apt-get install -y nodejs npm; else exit 126; fi";
            var installResult = await RunWorkspaceCommandAsync(aptInstall, workDir, cancellationToken);
            AppendInstallOutcome($"Node 런타임 설치({program})", aptInstall, installResult, logs, errors);
            if (installResult.ExitCode != 0)
            {
                return false;
            }

            if (string.Equals(program, "pnpm", StringComparison.OrdinalIgnoreCase)
                || string.Equals(program, "yarn", StringComparison.OrdinalIgnoreCase))
            {
                return await InstallNodePackageManagerAsync(program, workDir, logs, errors, cancellationToken);
            }

            return true;
        }

        errors.Add($"Node 런타임 자동 설치 미지원 OS ({program})");
        return false;
    }

    private static string BuildPipRequirementsInstallCommand(string requirementsPath)
    {
        if (OperatingSystem.IsWindows())
        {
            return $"if exist .venv\\Scripts\\python.exe (.venv\\Scripts\\python.exe -m pip install --disable-pip-version-check -r {EscapeShellArg(requirementsPath)}) else (python -m pip install --disable-pip-version-check --user -r {EscapeShellArg(requirementsPath)})";
        }

        return $"if [ -x .venv/bin/python ]; then .venv/bin/python -m pip install --disable-pip-version-check -r {EscapeShellArg(requirementsPath)}; else python3 -m pip install --disable-pip-version-check --user $(python3 -m pip help install 2>/dev/null | grep -q -- '--break-system-packages' && printf %s ' --break-system-packages') -r {EscapeShellArg(requirementsPath)}; fi";
    }

    private static string BuildPipPackageInstallCommand(IEnumerable<string> packages)
    {
        var packageArgs = string.Join(" ", packages.Select(EscapeShellArg));
        if (OperatingSystem.IsWindows())
        {
            return $"if exist .venv\\Scripts\\python.exe (.venv\\Scripts\\python.exe -m pip install --disable-pip-version-check {packageArgs}) else (python -m pip install --disable-pip-version-check --user {packageArgs})";
        }

        return $"if [ -x .venv/bin/python ]; then .venv/bin/python -m pip install --disable-pip-version-check {packageArgs}; else python3 -m pip install --disable-pip-version-check --user $(python3 -m pip help install 2>/dev/null | grep -q -- '--break-system-packages' && printf %s ' --break-system-packages') {packageArgs}; fi";
    }

    private async Task<bool> InstallPythonRuntimeToolchainAsync(
        string program,
        string workDir,
        List<string> logs,
        List<string> errors,
        CancellationToken cancellationToken
    )
    {
        if (OperatingSystem.IsMacOS())
        {
            var brewCheck = await RunWorkspaceCommandAsync("command -v brew >/dev/null 2>&1", workDir, cancellationToken);
            if (brewCheck.ExitCode != 0)
            {
                errors.Add($"Python 런타임 자동 설치 건너뜀: brew 없음 ({program})");
                return false;
            }

            var brewInstall = "brew install python";
            var installResult = await RunWorkspaceCommandAsync(brewInstall, workDir, cancellationToken);
            AppendInstallOutcome($"Python 런타임 설치({program})", brewInstall, installResult, logs, errors);
            return installResult.ExitCode == 0;
        }

        if (OperatingSystem.IsLinux())
        {
            var linuxPackageManager = await DetectLinuxPackageManagerAsync(workDir, cancellationToken);
            if (linuxPackageManager != "apt-get")
            {
                errors.Add($"Python 런타임 자동 설치 건너뜀: apt-get 없음 ({program}, 감지됨: {linuxPackageManager})");
                return false;
            }

            var aptInstall = "if [ \"$(id -u)\" -eq 0 ]; then apt-get update && apt-get install -y python3 python3-pip; elif command -v sudo >/dev/null 2>&1; then sudo -n apt-get update && sudo -n apt-get install -y python3 python3-pip; else exit 126; fi";
            var installResult = await RunWorkspaceCommandAsync(aptInstall, workDir, cancellationToken);
            AppendInstallOutcome($"Python 런타임 설치({program})", aptInstall, installResult, logs, errors);
            return installResult.ExitCode == 0;
        }

        if (OperatingSystem.IsWindows())
        {
            errors.Add($"Python 런타임 자동 설치 미지원 OS ({program}). Python 3을 설치하고 PATH에 python을 추가하세요.");
            return false;
        }

        errors.Add($"Python 런타임 자동 설치 미지원 OS ({program})");
        return false;
    }

    private async Task<bool> InstallNodePackageManagerAsync(
        string program,
        string workDir,
        List<string> logs,
        List<string> errors,
        CancellationToken cancellationToken
    )
    {
        var safeProgram = SanitizeProgramName(program);
        if (string.IsNullOrWhiteSpace(safeProgram))
        {
            return false;
        }

        var npmCommand = $"npm install -g {EscapeShellArg(safeProgram)}";
        var installResult = await RunWorkspaceCommandAsync(npmCommand, workDir, cancellationToken);
        AppendInstallOutcome($"Node 패키지 매니저 설치({safeProgram})", npmCommand, installResult, logs, errors);
        return installResult.ExitCode == 0;
    }

    private static bool IsPythonSystemModule(string? moduleName)
    {
        return string.Equals(moduleName, "tkinter", StringComparison.OrdinalIgnoreCase)
               || string.Equals(moduleName, "_tkinter", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> TryInstallPythonSystemModuleAsync(
        string moduleName,
        string workDir,
        List<string> logs,
        List<string> errors,
        CancellationToken cancellationToken
    )
    {
        if (!IsPythonSystemModule(moduleName))
        {
            return false;
        }

        if (OperatingSystem.IsMacOS())
        {
            var brewCheck = await RunWorkspaceCommandAsync("command -v brew >/dev/null 2>&1", workDir, cancellationToken);
            if (brewCheck.ExitCode != 0)
            {
                errors.Add("tkinter 자동 설치 건너뜀: brew 없음");
                return false;
            }

            var versionResult = await RunWorkspaceCommandAsync(
                "python3 - <<'PY'\nimport sys\nprint(f\"{sys.version_info.major}.{sys.version_info.minor}\")\nPY",
                workDir,
                cancellationToken
            );
            var version = versionResult.ExitCode == 0 ? versionResult.StdOut.Trim() : string.Empty;
            var formulaCandidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(version))
            {
                formulaCandidates.Add($"python-tk@{version}");
            }

            formulaCandidates.Add("python-tk");
            foreach (var formula in formulaCandidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var brewInstall = $"brew install {EscapeShellArg(formula)}";
                var installResult = await RunWorkspaceCommandAsync(brewInstall, workDir, cancellationToken);
                AppendInstallOutcome($"tkinter 시스템 패키지 설치({formula})", brewInstall, installResult, logs, errors);
                if (installResult.ExitCode == 0)
                {
                    return true;
                }
            }

            return false;
        }

        if (OperatingSystem.IsLinux())
        {
            var linuxPackageManager = await DetectLinuxPackageManagerAsync(workDir, cancellationToken);
            if (linuxPackageManager != "apt-get")
            {
                errors.Add($"tkinter 자동 설치 건너뜀: apt-get 없음 (python3-tk, 감지됨: {linuxPackageManager})");
                return false;
            }

            var aptInstall = "if [ \"$(id -u)\" -eq 0 ]; then apt-get update && apt-get install -y python3-tk; elif command -v sudo >/dev/null 2>&1; then sudo -n apt-get update && sudo -n apt-get install -y python3-tk; else exit 126; fi";
            var installResult = await RunWorkspaceCommandAsync(aptInstall, workDir, cancellationToken);
            AppendInstallOutcome("tkinter 시스템 패키지 설치(python3-tk)", aptInstall, installResult, logs, errors);
            return installResult.ExitCode == 0;
        }

        errors.Add($"tkinter 자동 설치 미지원 OS ({moduleName})");
        return false;
    }

    private async Task<bool> TryInstallProgramAsync(
        string program,
        string workDir,
        List<string> logs,
        List<string> errors,
        CancellationToken cancellationToken
    )
    {
        var safeProgram = SanitizeProgramName(program);
        if (string.IsNullOrWhiteSpace(safeProgram))
        {
            return false;
        }

        if (OperatingSystem.IsMacOS())
        {
            var brewCheck = await RunWorkspaceCommandAsync("command -v brew >/dev/null 2>&1", workDir, cancellationToken);
            if (brewCheck.ExitCode != 0)
            {
                errors.Add($"프로그램 자동 설치 건너뜀: brew 없음 ({safeProgram})");
                return false;
            }

            var brewInstall = $"brew install {EscapeShellArg(safeProgram)}";
            var installResult = await RunWorkspaceCommandAsync(brewInstall, workDir, cancellationToken);
            AppendInstallOutcome($"brew 프로그램 설치({safeProgram})", brewInstall, installResult, logs, errors);
            return installResult.ExitCode == 0;
        }

        if (OperatingSystem.IsLinux())
        {
            var linuxPackageManager = await DetectLinuxPackageManagerAsync(workDir, cancellationToken);
            if (linuxPackageManager != "apt-get")
            {
                errors.Add($"프로그램 자동 설치 건너뜀: apt-get 없음 ({safeProgram}, 감지됨: {linuxPackageManager})");
                return false;
            }

            var aptInstall = $"if [ \"$(id -u)\" -eq 0 ]; then apt-get update && apt-get install -y {EscapeShellArg(safeProgram)}; elif command -v sudo >/dev/null 2>&1; then sudo -n apt-get update && sudo -n apt-get install -y {EscapeShellArg(safeProgram)}; else exit 126; fi";
            var installResult = await RunWorkspaceCommandAsync(aptInstall, workDir, cancellationToken);
            AppendInstallOutcome($"apt 프로그램 설치({safeProgram})", aptInstall, installResult, logs, errors);
            return installResult.ExitCode == 0;
        }

        errors.Add($"프로그램 자동 설치 미지원 OS ({safeProgram})");
        return false;
    }

    private async Task<string> DetectLinuxPackageManagerAsync(string workDir, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
        {
            return "unsupported";
        }

        var result = await RunWorkspaceCommandAsync(
            "for manager in apt-get dnf yum pacman apk zypper; do if command -v \"$manager\" >/dev/null 2>&1; then printf '%s' \"$manager\"; exit 0; fi; done; printf 'none'",
            workDir,
            cancellationToken
        );
        var detected = result.StdOut.Trim();
        return string.IsNullOrWhiteSpace(detected) ? "none" : detected;
    }

    private static string? FindRequirementsFile(string primaryDir, string fallbackDir)
    {
        if (!string.IsNullOrWhiteSpace(primaryDir))
        {
            var primary = Path.Combine(primaryDir, "requirements.txt");
            if (File.Exists(primary))
            {
                return primary;
            }
        }

        var fallback = Path.Combine(fallbackDir, "requirements.txt");
        return File.Exists(fallback) ? fallback : null;
    }

    private static string ResolveDependencyBaseDirectory(string command, string workDir, params string[] extensions)
    {
        var scriptPath = TryExtractScriptPath(command, workDir, extensions);
        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            return workDir;
        }

        var candidate = scriptPath;
        if (!Path.IsPathRooted(candidate))
        {
            candidate = Path.GetFullPath(Path.Combine(workDir, candidate));
        }

        if (Directory.Exists(candidate))
        {
            return candidate;
        }

        var parent = Path.GetDirectoryName(candidate);
        return string.IsNullOrWhiteSpace(parent) ? workDir : parent;
    }

    private static string? TryExtractScriptPath(string command, string workDir, params string[] extensions)
    {
        if (string.IsNullOrWhiteSpace(command) || extensions.Length == 0)
        {
            return null;
        }

        foreach (Match match in ShellTokenRegex.Matches(command))
        {
            var token = match.Groups["sq"].Success
                ? match.Groups["sq"].Value
                : match.Groups["dq"].Success
                    ? match.Groups["dq"].Value
                    : match.Groups["bare"].Value;
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            if (!extensions.Any(ext => token.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (Path.IsPathRooted(token))
            {
                return token;
            }

            return Path.GetFullPath(Path.Combine(workDir, token));
        }

        return null;
    }

    private static List<string> ExtractPythonPackagesFromSource(string scriptPath)
    {
        string text;
        try
        {
            text = File.ReadAllText(scriptPath);
        }
        catch
        {
            return new List<string>();
        }

        var scriptDir = Path.GetDirectoryName(scriptPath) ?? string.Empty;
        var modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in PythonImportRegex.Matches(text))
        {
            var parts = (match.Groups["mods"].Value ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                var tokenParts = part.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (tokenParts.Length == 0)
                {
                    continue;
                }

                var token = tokenParts[0].Trim();
                var rootParts = token.Split('.', StringSplitOptions.RemoveEmptyEntries);
                if (rootParts.Length == 0)
                {
                    continue;
                }

                var root = rootParts[0].Trim();
                if (!string.IsNullOrWhiteSpace(root))
                {
                    modules.Add(root);
                }
            }
        }

        foreach (Match match in PythonFromImportRegex.Matches(text))
        {
            var rootParts = (match.Groups["mod"].Value ?? string.Empty)
                .Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (rootParts.Length == 0)
            {
                continue;
            }

            var root = rootParts[0].Trim();
            if (!string.IsNullOrWhiteSpace(root))
            {
                modules.Add(root);
            }
        }

        var packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in modules)
        {
            if (PythonStdlibModules.Contains(module))
            {
                continue;
            }

            var localModulePath = Path.Combine(scriptDir, module + ".py");
            var localPackagePath = Path.Combine(scriptDir, module);
            if (File.Exists(localModulePath) || Directory.Exists(localPackagePath))
            {
                continue;
            }

            var package = ResolvePythonPackageName(module);
            if (!string.IsNullOrWhiteSpace(package))
            {
                packages.Add(package);
            }
        }

        return packages.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> ExtractNodePackagesFromSource(string scriptPath)
    {
        string text;
        try
        {
            text = File.ReadAllText(scriptPath);
        }
        catch
        {
            return new List<string>();
        }

        var packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in NodeImportRegex.Matches(text))
        {
            var specifier = match.Groups["mod"].Success
                ? match.Groups["mod"].Value
                : match.Groups["mod2"].Success
                    ? match.Groups["mod2"].Value
                    : match.Groups["mod3"].Value;
            var package = ResolveNodePackageName(specifier);
            if (!string.IsNullOrWhiteSpace(package))
            {
                packages.Add(package);
            }
        }

        return packages.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? ResolvePythonPackageName(string? moduleName)
    {
        var rootParts = (moduleName ?? string.Empty)
            .Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (rootParts.Length == 0)
        {
            return null;
        }

        var root = rootParts[0].Trim();
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        if (IsPythonSystemModule(root))
        {
            return null;
        }

        if (PythonModulePackageMap.TryGetValue(root, out var mapped))
        {
            return mapped;
        }

        return Regex.IsMatch(root, "^[A-Za-z0-9._-]+$") ? root : null;
    }

    private static string? ResolveNodePackageName(string? specifier)
    {
        var raw = (specifier ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw)
            || raw.StartsWith("./", StringComparison.Ordinal)
            || raw.StartsWith("../", StringComparison.Ordinal)
            || raw.StartsWith("/", StringComparison.Ordinal)
            || raw.StartsWith("node:", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("http:", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string package;
        if (raw.StartsWith("@", StringComparison.Ordinal))
        {
            var segments = raw.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2)
            {
                return null;
            }

            package = $"{segments[0]}/{segments[1]}";
        }
        else
        {
            var segments = raw.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return null;
            }

            package = segments[0];
        }

        if (NodeBuiltinModules.Contains(package))
        {
            return null;
        }

        return Regex.IsMatch(package, "^@?[A-Za-z0-9._-]+(?:/[A-Za-z0-9._-]+)?$") ? package : null;
    }

    private static string? ResolvePythonCliPackageName(string? program)
    {
        var raw = (program ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (PythonCliPackageMap.TryGetValue(raw, out var mapped))
        {
            return mapped;
        }

        return Regex.IsMatch(raw, "^[A-Za-z0-9._-]+$") ? raw : null;
    }

    private static string? ResolveNodeCliPackageName(string? program)
    {
        var raw = (program ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (NodeCliPackageMap.TryGetValue(raw, out var mapped))
        {
            return mapped;
        }

        return Regex.IsMatch(raw, "^@?[A-Za-z0-9._-]+(?:/[A-Za-z0-9._-]+)?$") ? raw : null;
    }

    private static bool HasPythonWorkspaceSignals(string command, string workDir)
    {
        var baseDir = ResolveDependencyBaseDirectory(command, workDir, ".py");
        return File.Exists(Path.Combine(baseDir, "requirements.txt"))
               || Directory.EnumerateFiles(baseDir, "*.py", SearchOption.TopDirectoryOnly).Any();
    }

    private static bool HasNodeWorkspaceSignals(string command, string workDir)
    {
        var baseDir = ResolveDependencyBaseDirectory(command, workDir, ".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx");
        if (File.Exists(Path.Combine(baseDir, "package.json")))
        {
            return true;
        }

        foreach (var pattern in new[] { "*.js", "*.mjs", "*.cjs", "*.ts", "*.tsx", "*.jsx" })
        {
            if (Directory.EnumerateFiles(baseDir, pattern, SearchOption.TopDirectoryOnly).Any())
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNodeRuntimeProgram(string program)
    {
        return string.Equals(program, "node", StringComparison.OrdinalIgnoreCase)
               || string.Equals(program, "npm", StringComparison.OrdinalIgnoreCase)
               || string.Equals(program, "npx", StringComparison.OrdinalIgnoreCase)
               || string.Equals(program, "pnpm", StringComparison.OrdinalIgnoreCase)
               || string.Equals(program, "yarn", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPythonRuntimeProgram(string program)
    {
        return string.Equals(program, "python", StringComparison.OrdinalIgnoreCase)
               || string.Equals(program, "python3", StringComparison.OrdinalIgnoreCase)
               || string.Equals(program, "pip", StringComparison.OrdinalIgnoreCase)
               || string.Equals(program, "pip3", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractPythonMissingModule(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return null;
        }

        var moduleNotFoundMatch = PythonModuleNotFoundRegex.Match(stderr);
        if (moduleNotFoundMatch.Success)
        {
            return moduleNotFoundMatch.Groups["module"].Value;
        }

        var importErrorMatch = PythonImportErrorRegex.Match(stderr);
        return importErrorMatch.Success ? importErrorMatch.Groups["module"].Value : null;
    }

    private static string? ExtractNodeMissingModule(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return null;
        }

        var match = NodeModuleNotFoundRegex.Match(stderr);
        if (!match.Success)
        {
            return null;
        }

        return match.Groups["module"].Success ? match.Groups["module"].Value : match.Groups["module2"].Value;
    }

    private static string? ExtractMissingProgram(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return null;
        }

        var match = CommandNotFoundRegex.Match(stderr);
        if (!match.Success)
        {
            return null;
        }

        var candidate = match.Groups["cmd"].Success ? match.Groups["cmd"].Value : match.Groups["cmd2"].Value;
        return SanitizeProgramName(candidate);
    }

    private static bool LooksLikePythonCommand(string command)
    {
        var lowered = (command ?? string.Empty).ToLowerInvariant();
        if (lowered.Contains("python", StringComparison.Ordinal))
        {
            return true;
        }

        var executable = ExtractCommandExecutableToken(command ?? string.Empty);
        return !string.IsNullOrWhiteSpace(executable) && PythonCommandPrefixes.Contains(executable);
    }

    private static bool LooksLikeNodeCommand(string command)
    {
        var lowered = (command ?? string.Empty).ToLowerInvariant();
        if (lowered.Contains("node", StringComparison.Ordinal)
            || lowered.Contains("npm", StringComparison.Ordinal)
            || lowered.Contains("pnpm", StringComparison.Ordinal)
            || lowered.Contains("yarn", StringComparison.Ordinal)
            || lowered.Contains("tsx", StringComparison.Ordinal)
            || lowered.Contains("ts-node", StringComparison.Ordinal))
        {
            return true;
        }

        var executable = ExtractCommandExecutableToken(command ?? string.Empty);
        return !string.IsNullOrWhiteSpace(executable) && NodeCommandPrefixes.Contains(executable);
    }

    private static string? ExtractCommandExecutableToken(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var awaitingEnvValue = false;
        foreach (Match match in ShellTokenRegex.Matches(command))
        {
            var token = match.Groups["sq"].Success
                ? match.Groups["sq"].Value
                : match.Groups["dq"].Success
                    ? match.Groups["dq"].Value
                    : match.Groups["bare"].Value;
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            if (awaitingEnvValue)
            {
                awaitingEnvValue = false;
                continue;
            }

            if (string.Equals(token, "env", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(token, "-i", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "--ignore-environment", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(token, "-u", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "--unset", StringComparison.OrdinalIgnoreCase))
            {
                awaitingEnvValue = true;
                continue;
            }

            if (Regex.IsMatch(token, "^[A-Za-z_][A-Za-z0-9_]*=.*$"))
            {
                continue;
            }

            return Path.GetFileName(token);
        }

        return null;
    }

    private static string? SanitizeProgramName(string? value)
    {
        var token = (value ?? string.Empty).Trim();
        return Regex.IsMatch(token, "^[A-Za-z0-9][A-Za-z0-9+._-]{1,63}$") ? token : null;
    }

    private static void AppendInstallOutcome(
        string title,
        string installCommand,
        ShellRunResult result,
        List<string> logs,
        List<string> errors
    )
    {
        if (result.ExitCode == 0)
        {
            logs.Add($"{title}: ok");
            var stdout = TrimInstallLog(result.StdOut, 1000);
            if (!string.IsNullOrWhiteSpace(stdout))
            {
                logs.Add(stdout);
            }
            return;
        }

        errors.Add($"{title}: error(exit={result.ExitCode})");
        errors.Add($"command={installCommand}");
        var stderr = TrimInstallLog(result.StdErr, 1400);
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            errors.Add(stderr);
        }
    }

    private static string MergeInstallLogs(string header, IReadOnlyList<string> installLogs, string originalText)
    {
        if (installLogs.Count == 0)
        {
            return originalText ?? string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine(header);
        foreach (var line in installLogs)
        {
            builder.AppendLine(line);
        }

        if (!string.IsNullOrWhiteSpace(originalText))
        {
            builder.AppendLine();
            builder.Append(originalText.Trim());
        }

        return builder.ToString().Trim();
    }

    private static string TrimInstallLog(string text, int maxLength)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength] + "\n...(truncated)";
    }

    private static string EscapeShellArg(string value)
    {
        if (OperatingSystem.IsWindows())
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        }

        return $"'{(value ?? string.Empty).Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";
    }

    private async Task<ShellRunResult> RunWorkspaceCommandAsync(
        string command,
        string workDir,
        CancellationToken cancellationToken,
        string? standardInput = null
    )
    {
        var shellPath = ResolveWorkspaceShellPath();
        var startInfo = new ProcessStartInfo
        {
            FileName = shellPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workDir
        };
        ApplyWorkspaceExecutablePath(startInfo, command, workDir);

        var normalizedCommand = NormalizePythonCommandForShell(command);
        if (OperatingSystem.IsWindows())
        {
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(normalizedCommand);
        }
        else
        {
            startInfo.ArgumentList.Add("-lc");
            startInfo.ArgumentList.Add(normalizedCommand);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
            try
            {
                var normalizedInput = NormalizeWorkspaceCommandStandardInput(standardInput);
                if (!string.IsNullOrEmpty(normalizedInput))
                {
                    process.StandardInput.Write(normalizedInput);
                    process.StandardInput.Flush();
                }

                process.StandardInput.Close();
            }
            catch
            {
            }
        }
        catch (Exception ex)
        {
            return new ShellRunResult(127, string.Empty, ex.Message, false);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(20, _execution.CodeExecutionTimeoutSec)));
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
            return new ShellRunResult(process.ExitCode, await stdoutTask, await stderrTask, false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            string stdout;
            string stderr;
            try
            {
                stdout = await stdoutTask;
            }
            catch
            {
                stdout = string.Empty;
            }

            try
            {
                stderr = await stderrTask;
            }
            catch
            {
                stderr = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(stderr))
            {
                stderr = "execution timed out";
            }

            return new ShellRunResult(124, stdout, stderr, true);
        }
    }

    private static string NormalizeWorkspaceCommandStandardInput(string? standardInput)
    {
        var normalized = (standardInput ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        return normalized.EndsWith('\n') ? normalized : normalized + "\n";
    }

    private static string ResolveWorkspaceShellPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return "cmd.exe";
        }

        foreach (var candidate in new[] { "/bin/zsh", "/usr/bin/zsh", "/bin/bash", "/usr/bin/bash", "/bin/sh" })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "/bin/sh";
    }

    private static void ApplyWorkspaceExecutablePath(ProcessStartInfo startInfo, string command, string workDir)
    {
        var pathPrefixes = new List<string>();

        var workspacePythonBin = Path.Combine(workDir, ".venv", "bin");
        if (Directory.Exists(workspacePythonBin))
        {
            pathPrefixes.Add(workspacePythonBin);
        }

        var workspaceWindowsPythonBin = Path.Combine(workDir, ".venv", "Scripts");
        if (Directory.Exists(workspaceWindowsPythonBin))
        {
            pathPrefixes.Add(workspaceWindowsPythonBin);
        }

        var workspaceNodeBin = Path.Combine(workDir, "node_modules", ".bin");
        if (Directory.Exists(workspaceNodeBin))
        {
            pathPrefixes.Add(workspaceNodeBin);
        }

        var nodeBaseDir = ResolveDependencyBaseDirectory(command, workDir, ".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx");
        var nodeBaseBin = Path.Combine(nodeBaseDir, "node_modules", ".bin");
        if (Directory.Exists(nodeBaseBin))
        {
            pathPrefixes.Add(nodeBaseBin);
        }

        var runtimeBin = Path.Combine(workDir, ".runtime", "bin");
        if (Directory.Exists(runtimeBin))
        {
            pathPrefixes.Add(runtimeBin);
        }

        foreach (var pythonBin in EnumeratePythonUserBinDirectories())
        {
            pathPrefixes.Add(pythonBin);
        }

        if (pathPrefixes.Count == 0)
        {
            return;
        }

        var existing = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var merged = pathPrefixes
            .Distinct(StringComparer.Ordinal)
            .Concat(string.IsNullOrWhiteSpace(existing) ? Array.Empty<string>() : new[] { existing });
        startInfo.Environment["PATH"] = string.Join(Path.PathSeparator, merged);
    }

    private static IEnumerable<string> EnumeratePythonUserBinDirectories()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            yield break;
        }

        if (OperatingSystem.IsMacOS())
        {
            var libraryPython = Path.Combine(home, "Library", "Python");
            if (!Directory.Exists(libraryPython))
            {
                yield break;
            }

            foreach (var versionDir in Directory.EnumerateDirectories(libraryPython))
            {
                var binDir = Path.Combine(versionDir, "bin");
                if (Directory.Exists(binDir))
                {
                    yield return binDir;
                }
            }

            yield break;
        }

        if (OperatingSystem.IsLinux())
        {
            var localBin = Path.Combine(home, ".local", "bin");
            if (Directory.Exists(localBin))
            {
                yield return localBin;
            }
        }
    }
}
