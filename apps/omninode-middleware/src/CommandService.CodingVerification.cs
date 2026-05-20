using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    private static string WrapCommandWithExpectedOutputAssertion(
        string command,
        string? expectedOutput,
        IReadOnlyList<string>? expectedOutputLines = null
    )
    {
        var normalizedCommand = CodingFallbackPolicy.NormalizeGeneratedRunCommand(command);
        var resolvedExpectedLines = ResolveExpectedOutputAssertionLines(expectedOutput, expectedOutputLines);
        if (string.IsNullOrWhiteSpace(normalizedCommand) || resolvedExpectedLines.Count == 0)
        {
            return normalizedCommand;
        }

        if (OperatingSystem.IsWindows())
        {
            return BuildWindowsExpectedOutputAssertionCommand(normalizedCommand, resolvedExpectedLines);
        }

        var builder = new StringBuilder();
        builder.Append("tmp_out=$(mktemp); tmp_err=$(mktemp); __omni_status=0; ");
        builder.Append($"({normalizedCommand}) >\"$tmp_out\" 2>\"$tmp_err\" || __omni_status=$?; ");
        builder.Append("cat \"$tmp_out\"; cat \"$tmp_err\" >&2; ");
        builder.Append("if [ $__omni_status -ne 0 ]; then rm -f \"$tmp_out\" \"$tmp_err\"; exit $__omni_status; fi; ");
        AppendExpectedOutputAssertions(builder, resolvedExpectedLines);
        builder.Append("rm -f \"$tmp_out\" \"$tmp_err\"");
        return builder.ToString();
    }

    private static string WrapCommandWithVerificationAssertions(
        string command,
        string? expectedOutput,
        IReadOnlyCollection<string> verifiedFiles,
        IReadOnlyList<string>? expectedOutputLines = null
    )
    {
        var normalizedCommand = CodingFallbackPolicy.NormalizeGeneratedRunCommand(command);
        var resolvedExpectedLines = ResolveExpectedOutputAssertionLines(expectedOutput, expectedOutputLines);
        var files = (verifiedFiles ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (string.IsNullOrWhiteSpace(normalizedCommand))
        {
            return normalizedCommand;
        }

        if (files.Length == 0 && resolvedExpectedLines.Count == 0)
        {
            return normalizedCommand;
        }

        if (OperatingSystem.IsWindows())
        {
            var assertedCommand = BuildWindowsExpectedOutputAssertionCommand(normalizedCommand, resolvedExpectedLines);
            var fileChecks = files.Select(path => $"if not exist {EscapeShellArg(path)} (echo verified file missing: {path} 1>&2 && exit /b 1)");
            return string.Join(" && ", new[] { assertedCommand }.Concat(fileChecks));
        }

        var builder = new StringBuilder();
        builder.Append("tmp_out=$(mktemp); tmp_err=$(mktemp); __omni_status=0; ");
        builder.Append($"({normalizedCommand}) >\"$tmp_out\" 2>\"$tmp_err\" || __omni_status=$?; ");
        builder.Append("cat \"$tmp_out\"; cat \"$tmp_err\" >&2; ");
        builder.Append("if [ $__omni_status -ne 0 ]; then rm -f \"$tmp_out\" \"$tmp_err\"; exit $__omni_status; fi; ");
        foreach (var path in files)
        {
            var safePath = EscapeShellArg(path);
            var failureMessageArg = EscapeShellArg($"verified file missing: {path}");
            builder.Append($"if [ ! -f {safePath} ]; then printf '%s\\n' {failureMessageArg} >&2; rm -f \"$tmp_out\" \"$tmp_err\"; exit 1; fi; ");
        }

        AppendExpectedOutputAssertions(builder, resolvedExpectedLines);

        builder.Append("rm -f \"$tmp_out\" \"$tmp_err\"");
        return builder.ToString();
    }

    private static string DescribeCommandWithExpectedOutput(
        string command,
        string? expectedOutput,
        IReadOnlyList<string>? expectedOutputLines = null
    )
    {
        var normalizedCommand = CodingFallbackPolicy.NormalizeGeneratedRunCommand(command);
        var resolvedExpectedLines = ResolveExpectedOutputAssertionLines(expectedOutput, expectedOutputLines);
        if (resolvedExpectedLines.Count == 0)
        {
            return normalizedCommand;
        }

        var compactExpected = string.Join(" | ", resolvedExpectedLines.Take(2));
        compactExpected = Regex.Replace(compactExpected.Trim(), @"\s+", " ");
        if (compactExpected.Length > 80)
        {
            compactExpected = compactExpected[..80] + "...";
        }

        return $"{normalizedCommand} [stdout 포함: {compactExpected}]";
    }

    private static string DescribeVerificationCommand(
        string command,
        string? expectedOutput,
        IReadOnlyCollection<string> verifiedFiles,
        IReadOnlyList<string>? expectedOutputLines = null
    )
    {
        var description = DescribeCommandWithExpectedOutput(command, expectedOutput, expectedOutputLines);
        var fileCount = (verifiedFiles ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        return fileCount > 0 ? $"{description} [파일 확인: {fileCount}개]" : description;
    }

    private static string BuildVerificationCommand(
        string language,
        IReadOnlyCollection<string> changedFiles,
        string workspaceRoot,
        string objective,
        IReadOnlyList<string>? requestedPaths = null,
        string? expectedOutput = null
    )
    {
        var baseCommand = BuildVerificationBaseCommand(language, changedFiles, workspaceRoot, objective, requestedPaths, expectedOutput);
        var expectedOutputLines = ExtractExpectedConsoleOutputLines(objective);
        return WrapCommandWithVerificationAssertions(baseCommand, expectedOutput, changedFiles, expectedOutputLines);
    }

    private static string BuildVerificationDisplayCommand(
        string language,
        IReadOnlyCollection<string> changedFiles,
        string workspaceRoot,
        string objective,
        IReadOnlyList<string>? requestedPaths = null,
        string? expectedOutput = null
    )
    {
        var baseCommand = BuildVerificationBaseCommand(language, changedFiles, workspaceRoot, objective, requestedPaths, expectedOutput);
        var expectedOutputLines = ExtractExpectedConsoleOutputLines(objective);
        return DescribeVerificationCommand(baseCommand, expectedOutput, changedFiles, expectedOutputLines);
    }

    private static string BuildVerificationBaseCommand(
        string language,
        IReadOnlyCollection<string> changedFiles,
        string workspaceRoot,
        string objective,
        IReadOnlyList<string>? requestedPaths = null,
        string? expectedOutput = null
    )
    {
        var objectiveText = ExtractLatestCodingRequestText(WebUtility.HtmlDecode(objective ?? string.Empty));
        var normalizedLanguage = NormalizeLanguageForCode(language);
        var expectedOutputLines = ExtractExpectedConsoleOutputLines(objectiveText);
        var hasExpectedOutput = !string.IsNullOrWhiteSpace(expectedOutput) || expectedOutputLines.Count > 0;
        var shouldRunProgram = ShouldPreferProgramExecutionForVerification(
            objectiveText,
            normalizedLanguage,
            hasExpectedOutput ? (expectedOutput ?? string.Join("\n", expectedOutputLines)) : expectedOutput
        );
        var explicitCommand = TryBuildExplicitVerificationCommand(objectiveText, workspaceRoot, changedFiles);
        if (!string.IsNullOrWhiteSpace(explicitCommand))
        {
            return explicitCommand;
        }

        string? firstFile = null;
        if (shouldRunProgram)
        {
            firstFile = TrySelectExplicitExecutionTargetPath(objectiveText, workspaceRoot, changedFiles);
        }

        if (requestedPaths != null)
        {
            foreach (var requestedPath in requestedPaths)
            {
                if (string.IsNullOrWhiteSpace(requestedPath))
                {
                    continue;
                }

                var candidate = ResolveWorkspacePath(workspaceRoot, requestedPath);
                if (changedFiles.Contains(candidate, StringComparer.OrdinalIgnoreCase) && File.Exists(candidate))
                {
                    firstFile = candidate;
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(firstFile) && shouldRunProgram)
        {
            firstFile = SelectEntryLikeChangedFile(normalizedLanguage, changedFiles);
        }

        firstFile ??= changedFiles
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
        if (string.IsNullOrWhiteSpace(firstFile))
        {
            return string.Empty;
        }

        var safePath = EscapeShellArg(firstFile);
        var cdWorkspace = BuildChangeDirectoryCommand(workspaceRoot);
        if (normalizedLanguage == "python")
        {
            var sourceFiles = SelectChangedFilesByExtension(workspaceRoot, changedFiles, ".py");
            var sourceArgs = JoinShellArgs(sourceFiles.Length > 0 ? sourceFiles : new[] { ToWorkspaceRelativePath(workspaceRoot, firstFile) });
            var pythonRunnerPrefix = BuildPythonRunnerPrefixCommand();
            var pythonRunner = BuildPythonRunnerToken();
            var relativeFirstFile = ToWorkspaceRelativePath(workspaceRoot, firstFile);
            var interactivePython = IsInteractiveProgramObjective(objectiveText, normalizedLanguage);
            var projectCommand = interactivePython
                ? string.Empty
                : TryBuildPythonProjectVerificationCommand(workspaceRoot, changedFiles, hasExpectedOutput || shouldRunProgram);
            if (!string.IsNullOrWhiteSpace(projectCommand))
            {
                return projectCommand;
            }

            if (hasExpectedOutput || shouldRunProgram)
            {
                return $"{cdWorkspace} && {pythonRunnerPrefix} && {pythonRunner} {EscapeShellArg(relativeFirstFile)}";
            }

            if (interactivePython)
            {
                var pythonFiles = sourceFiles.Length > 0
                    ? sourceFiles.Select(path => ResolveWorkspacePath(workspaceRoot, path)).Where(File.Exists).ToArray()
                    : new[] { firstFile };
                var gameQualityCommand = BuildPythonGameStaticQualityCommand(objectiveText, pythonFiles);
                if (!LooksLikeInteractivePythonGameSource(pythonFiles))
                {
                    var message = "interactive python game must include real event handling/render loop; print-only simulation is insufficient";
                    return $"printf '%s\\n' {EscapeShellArg(message)} >&2; exit 1";
                }

                var interactiveModules = CollectInteractivePythonModulesFromSources(pythonFiles);
                if (interactiveModules.Count > 0)
                {
                    var envPrefix = BuildInlineEnvironmentPrefix(
                        interactiveModules.Contains("pygame", StringComparer.OrdinalIgnoreCase)
                            ? new Dictionary<string, string> { ["SDL_VIDEODRIVER"] = "dummy", ["OMNI_HEADLESS_TEST"] = "1" }
                            : new Dictionary<string, string> { ["OMNI_HEADLESS_TEST"] = "1" }
                    );
                    return $"{cdWorkspace} && {pythonRunnerPrefix} && {pythonRunner} -m py_compile {sourceArgs} && {BuildInteractivePythonModuleAvailabilityCommand(interactiveModules)} && {gameQualityCommand} && {envPrefix}{pythonRunner} {EscapeShellArg(relativeFirstFile)}";
                }

                return $"{cdWorkspace} && {pythonRunnerPrefix} && {pythonRunner} -m py_compile {sourceArgs} && {gameQualityCommand}";
            }

            return $"{cdWorkspace} && {pythonRunnerPrefix} && {pythonRunner} -m py_compile {sourceArgs}";
        }

        if (normalizedLanguage is "javascript" or "typescript" or "react-vite")
        {
            var projectCommand = TryBuildNodeProjectVerificationCommand(workspaceRoot, objectiveText, changedFiles, hasExpectedOutput || shouldRunProgram);
            if (!string.IsNullOrWhiteSpace(projectCommand))
            {
                return projectCommand;
            }

            if (normalizedLanguage == "typescript" || normalizedLanguage == "react-vite")
            {
                var sourceFiles = SelectChangedFilesByExtension(workspaceRoot, changedFiles, ".ts", ".tsx");
                if (sourceFiles.Length > 0)
                {
                    return $"{cdWorkspace} && {BuildRequiredProgramCommand("npx", "npx tsc --noEmit", "npx/tsc 없음, TypeScript 파일 생성 확인", allowMissing: true)}";
                }
            }

            if (hasExpectedOutput || shouldRunProgram)
            {
                return BuildRequiredProgramCommand("node", $"node {safePath}", "node 없음");
            }

            return BuildRequiredProgramCommand("node", $"node --check {safePath}", "node 없음, 파일 생성 확인", allowMissing: true);
        }

        if (normalizedLanguage == "go")
        {
            var projectRoot = FindNearestExistingParentWithFile(workspaceRoot, changedFiles, "go.mod");
            if (!string.IsNullOrWhiteSpace(projectRoot))
            {
                var root = EscapeShellArg(projectRoot);
                return $"{BuildChangeDirectoryCommand(projectRoot)} && {BuildRequiredProgramCommand("go", $"go test ./... && go build ./...{(hasExpectedOutput || shouldRunProgram ? " && go run ." : string.Empty)}", "go 없음")}";
            }

            var sourceFiles = SelectChangedFilesByExtension(workspaceRoot, changedFiles, ".go");
            var sourceArgs = JoinShellArgs(sourceFiles.Length > 0 ? sourceFiles : new[] { ToWorkspaceRelativePath(workspaceRoot, firstFile) });
            return hasExpectedOutput || shouldRunProgram
                ? $"{cdWorkspace} && {BuildRequiredProgramCommand("go", $"go run {sourceArgs}", "go 없음")}"
                : $"{cdWorkspace} && {BuildRequiredProgramCommand("go", $"go test {sourceArgs}", "go 없음, 파일 생성 확인", allowMissing: true)}";
        }

        if (normalizedLanguage == "rust")
        {
            var projectRoot = FindNearestExistingParentWithFile(workspaceRoot, changedFiles, "Cargo.toml");
            if (!string.IsNullOrWhiteSpace(projectRoot))
            {
                return $"{BuildChangeDirectoryCommand(projectRoot)} && {BuildRequiredProgramCommand("cargo", $"cargo test && cargo build{(hasExpectedOutput || shouldRunProgram ? " && cargo run" : string.Empty)}", "cargo 없음")}";
            }

            var sourceFiles = SelectChangedFilesByExtension(workspaceRoot, changedFiles, ".rs");
            var sourceArgs = JoinShellArgs(sourceFiles.Length > 0 ? sourceFiles : new[] { ToWorkspaceRelativePath(workspaceRoot, firstFile) });
            return hasExpectedOutput || shouldRunProgram
                ? $"{cdWorkspace} && {BuildRequiredProgramCommand("rustc", $"rustc {sourceArgs} -o {NativeOutputName("app")} && {RunLocalBinaryCommand("app")}", "rustc 없음")}"
                : $"{cdWorkspace} && {BuildRequiredProgramCommand("rustc", $"rustc {sourceArgs} -o {NativeOutputName("app")}", "rustc 없음, 파일 생성 확인", allowMissing: true)}";
        }

        if (normalizedLanguage == "php")
        {
            var sourceFiles = SelectChangedFilesByExtension(workspaceRoot, changedFiles, ".php");
            var sourceArgs = JoinShellArgs(sourceFiles.Length > 0 ? sourceFiles : new[] { ToWorkspaceRelativePath(workspaceRoot, firstFile) });
            return hasExpectedOutput || shouldRunProgram
                ? $"{cdWorkspace} && {BuildRequiredProgramCommand("php", $"php {EscapeShellArg(ToWorkspaceRelativePath(workspaceRoot, firstFile))}", "php 없음")}"
                : $"{cdWorkspace} && {BuildRequiredProgramCommand("php", $"php -l {sourceArgs}", "php 없음, 파일 생성 확인", allowMissing: true)}";
        }

        if (normalizedLanguage == "ruby")
        {
            var sourceFiles = SelectChangedFilesByExtension(workspaceRoot, changedFiles, ".rb");
            var sourceArgs = JoinShellArgs(sourceFiles.Length > 0 ? sourceFiles : new[] { ToWorkspaceRelativePath(workspaceRoot, firstFile) });
            return hasExpectedOutput || shouldRunProgram
                ? $"{cdWorkspace} && {BuildRequiredProgramCommand("ruby", $"ruby {EscapeShellArg(ToWorkspaceRelativePath(workspaceRoot, firstFile))}", "ruby 없음")}"
                : $"{cdWorkspace} && {BuildRequiredProgramCommand("ruby", $"ruby -c {sourceArgs}", "ruby 없음, 파일 생성 확인", allowMissing: true)}";
        }

        if (normalizedLanguage == "swift")
        {
            var packageRoot = FindNearestExistingParentWithFile(workspaceRoot, changedFiles, "Package.swift");
            if (!string.IsNullOrWhiteSpace(packageRoot))
            {
                return $"{BuildChangeDirectoryCommand(packageRoot)} && {BuildRequiredProgramCommand("swift", $"swift test && swift build{(hasExpectedOutput || shouldRunProgram ? " && swift run" : string.Empty)}", "swift 없음")}";
            }

            var sourceFiles = SelectChangedFilesByExtension(workspaceRoot, changedFiles, ".swift");
            var sourceArgs = JoinShellArgs(sourceFiles.Length > 0 ? sourceFiles : new[] { ToWorkspaceRelativePath(workspaceRoot, firstFile) });
            return hasExpectedOutput || shouldRunProgram
                ? $"{cdWorkspace} && {BuildRequiredProgramCommand("swift", $"swift {sourceArgs}", "swift 없음")}"
                : $"{cdWorkspace} && {BuildRequiredProgramCommand("swiftc", $"swiftc -parse {sourceArgs}", "swiftc 없음, 파일 생성 확인", allowMissing: true)}";
        }

        if (normalizedLanguage == "c")
        {
            var projectCommand = TryBuildNativeProjectVerificationCommand(workspaceRoot, changedFiles, "c", hasExpectedOutput || shouldRunProgram);
            if (!string.IsNullOrWhiteSpace(projectCommand))
            {
                return projectCommand;
            }

            var sourceFiles = SelectChangedFilesByExtension(workspaceRoot, changedFiles, ".c");
            var sourceArgs = JoinShellArgs(sourceFiles.Length > 0 ? sourceFiles : new[] { ToWorkspaceRelativePath(workspaceRoot, firstFile) });
            if (hasExpectedOutput || shouldRunProgram)
            {
                return $"{cdWorkspace} && {BuildRequiredProgramCommand("cc", $"cc -std=c11 -O2 {sourceArgs} -o {NativeOutputName("app")} && {RunLocalBinaryCommand("app")}", "cc 없음")}";
            }

            return $"{cdWorkspace} && {BuildRequiredProgramCommand("cc", $"cc -std=c11 -fsyntax-only {sourceArgs}", "cc 없음, 파일 생성 확인", allowMissing: true)}";
        }

        if (normalizedLanguage == "cpp")
        {
            var projectCommand = TryBuildNativeProjectVerificationCommand(workspaceRoot, changedFiles, "cpp", hasExpectedOutput || shouldRunProgram);
            if (!string.IsNullOrWhiteSpace(projectCommand))
            {
                return projectCommand;
            }

            var sourceFiles = SelectChangedFilesByExtension(workspaceRoot, changedFiles, ".cc", ".cpp", ".cxx");
            var sourceArgs = JoinShellArgs(sourceFiles.Length > 0 ? sourceFiles : new[] { ToWorkspaceRelativePath(workspaceRoot, firstFile) });
            if (hasExpectedOutput || shouldRunProgram)
            {
                return $"{cdWorkspace} && {BuildCppCompileCommand(sourceArgs, runAfterBuild: true)}";
            }

            return $"{cdWorkspace} && {BuildCppCompileCommand(sourceArgs, runAfterBuild: false)}";
        }

        if (normalizedLanguage == "csharp")
        {
            var projectCommand = TryBuildDotnetProjectVerificationCommand(workspaceRoot, changedFiles, hasExpectedOutput || shouldRunProgram);
            if (!string.IsNullOrWhiteSpace(projectCommand))
            {
                return projectCommand;
            }

            var sourceFiles = SelectChangedFilesByExtension(workspaceRoot, changedFiles, ".cs");
            var sourceArgs = JoinShellArgs(sourceFiles.Length > 0 ? sourceFiles : new[] { ToWorkspaceRelativePath(workspaceRoot, firstFile) });
            var runSuffix = hasExpectedOutput || shouldRunProgram ? " && dotnet run --no-restore --project \"$tmp_proj\" --no-launch-profile" : string.Empty;
            return OperatingSystem.IsWindows()
                ? $"{cdWorkspace} && where dotnet >NUL 2>NUL && dotnet new console --force --output omni-csharp-verify >NUL && del /q omni-csharp-verify\\Program.cs && copy {sourceArgs} omni-csharp-verify\\ >NUL && dotnet build omni-csharp-verify --nologo --verbosity quiet{(hasExpectedOutput || shouldRunProgram ? " && dotnet run --no-restore --project omni-csharp-verify --no-launch-profile" : string.Empty)}"
                : $"{cdWorkspace} && if command -v dotnet >/dev/null 2>&1; then tmp_proj=$(mktemp -d \"$PWD/omni-csharp-XXXXXX\"); dotnet new console --force --output \"$tmp_proj\" >/dev/null && rm -f \"$tmp_proj\"/Program.cs && cp {sourceArgs} \"$tmp_proj\"/ && dotnet build \"$tmp_proj\" --nologo --verbosity quiet{runSuffix}; status=$?; rm -rf \"$tmp_proj\"; exit $status; else echo 'dotnet 없음'; exit 1; fi";
        }

        if (normalizedLanguage == "java")
        {
            var projectCommand = TryBuildJavaProjectVerificationCommand(workspaceRoot, changedFiles, hasExpectedOutput || shouldRunProgram);
            if (!string.IsNullOrWhiteSpace(projectCommand))
            {
                return projectCommand;
            }

            var sourceFiles = SelectChangedFilesByExtension(workspaceRoot, changedFiles, ".java");
            var sourceArgs = JoinShellArgs(sourceFiles.Length > 0 ? sourceFiles : new[] { ToWorkspaceRelativePath(workspaceRoot, firstFile) });
            var mainClass = EscapeShellArg(Path.GetFileNameWithoutExtension(firstFile));
            if (hasExpectedOutput || shouldRunProgram)
            {
                return $"{cdWorkspace} && {BuildRequiredProgramCommand("javac", $"javac -Xlint:none {sourceArgs} && java {mainClass}", "javac 없음")}";
            }

            return $"{cdWorkspace} && {BuildRequiredProgramCommand("javac", $"javac -Xlint:none {sourceArgs}", "javac 없음, 파일 생성 확인", allowMissing: true)}";
        }

        if (normalizedLanguage == "kotlin")
        {
            var projectCommand = TryBuildKotlinProjectVerificationCommand(workspaceRoot, changedFiles, hasExpectedOutput || shouldRunProgram);
            if (!string.IsNullOrWhiteSpace(projectCommand))
            {
                return projectCommand;
            }

            var sourceFiles = SelectChangedFilesByExtension(workspaceRoot, changedFiles, ".kt");
            var sourceArgs = JoinShellArgs(sourceFiles.Length > 0 ? sourceFiles : new[] { ToWorkspaceRelativePath(workspaceRoot, firstFile) });
            if (hasExpectedOutput || shouldRunProgram)
            {
                return $"{cdWorkspace} && {BuildRequiredProgramCommand("kotlinc", $"kotlinc {sourceArgs} -include-runtime -d app.jar && java -jar app.jar", "kotlinc 없음")}";
            }

            return $"{cdWorkspace} && {BuildRequiredProgramCommand("kotlinc", $"kotlinc {sourceArgs} -d app.jar", "kotlinc 없음, 파일 생성 확인", allowMissing: true)}";
        }

        if (normalizedLanguage == "html" || normalizedLanguage == "css")
        {
            var structuredHtmlCommand = TryBuildStructuredHtmlVerificationCommand(workspaceRoot, objectiveText, changedFiles, requestedPaths);
            if (!string.IsNullOrWhiteSpace(structuredHtmlCommand))
            {
                return structuredHtmlCommand;
            }

            var htmlSmoke = TryBuildStaticHtmlPlaywrightSmokeCommand(workspaceRoot, changedFiles);
            if (!string.IsNullOrWhiteSpace(htmlSmoke))
            {
                return htmlSmoke;
            }

            return OperatingSystem.IsWindows()
                ? $"if exist {safePath} (echo 정적 파일 생성 확인: {Path.GetFileName(firstFile)}) else (exit /b 1)"
                : $"test -f {safePath} && echo '정적 파일 생성 확인: {Path.GetFileName(firstFile)}'";
        }

        if (normalizedLanguage == "bash")
        {
            if (hasExpectedOutput || shouldRunProgram)
            {
                return BuildRequiredProgramCommand("bash", $"bash {safePath}", "bash 없음");
            }

            return BuildRequiredProgramCommand("bash", $"bash -n {safePath}", "bash 없음, 파일 생성 확인", allowMissing: true);
        }

        return OperatingSystem.IsWindows() ? $"{cdWorkspace} && dir" : $"{cdWorkspace} && ls -la";
    }

    private static string BuildChangeDirectoryCommand(string directory)
    {
        return OperatingSystem.IsWindows()
            ? $"cd /d {EscapeShellArg(directory)}"
            : $"cd {EscapeShellArg(directory)}";
    }

    private static string BuildPythonRunnerPrefixCommand()
    {
        return OperatingSystem.IsWindows()
            ? "if exist .venv\\Scripts\\python.exe (set \"__omni_py=.venv\\Scripts\\python.exe\") else (set \"__omni_py=python\")"
            : "__omni_py=.venv/bin/python; if [ ! -x \"$__omni_py\" ]; then __omni_py=python3; fi";
    }

    private static string BuildPythonRunnerToken()
    {
        return OperatingSystem.IsWindows() ? "%__omni_py%" : "\"$__omni_py\"";
    }

    private static string BuildInlineEnvironmentPrefix(IReadOnlyDictionary<string, string> values)
    {
        if (values == null || values.Count == 0)
        {
            return string.Empty;
        }

        if (OperatingSystem.IsWindows())
        {
            return string.Concat(values.Select(pair => $"set \"{pair.Key}={pair.Value}\" && "));
        }

        return string.Concat(values.Select(pair => $"{pair.Key}={EscapeShellArg(pair.Value)} "));
    }

    private static string BuildRequiredProgramCommand(string program, string command, string missingMessage, bool allowMissing = false)
    {
        if (OperatingSystem.IsWindows())
        {
            var missing = allowMissing
                ? $"echo {missingMessage}"
                : $"echo {missingMessage} 1>&2 && exit /b 1";
            return $"where {program} >NUL 2>NUL && ({command}) || ({missing})";
        }

        var posixMissing = allowMissing
            ? $"echo {EscapeShellArg(missingMessage)}"
            : $"echo {EscapeShellArg(missingMessage)} >&2; exit 1";
        return $"if command -v {program} >/dev/null 2>&1; then {command}; else {posixMissing}; fi";
    }

    private static string NativeOutputName(string baseName)
    {
        return OperatingSystem.IsWindows() ? $"{baseName}.exe" : baseName;
    }

    private static string RunLocalBinaryCommand(string baseName)
    {
        return OperatingSystem.IsWindows() ? $".\\{baseName}.exe" : $"./{baseName}";
    }

    private static string BuildCppCompileCommand(string sourceArgs, bool runAfterBuild)
    {
        var output = NativeOutputName("app");
        if (OperatingSystem.IsWindows())
        {
            var compileWithCxx = $"where c++ >NUL 2>NUL && c++ -std=c++17 {(runAfterBuild ? "-O2" : "-fsyntax-only")} {sourceArgs}{(runAfterBuild ? $" -o {output}" : string.Empty)}";
            var compileWithGxx = $"where g++ >NUL 2>NUL && g++ -std=c++17 {(runAfterBuild ? "-O2" : "-fsyntax-only")} {sourceArgs}{(runAfterBuild ? $" -o {output}" : string.Empty)}";
            var compile = $"({compileWithCxx}) || ({compileWithGxx})";
            return runAfterBuild ? $"{compile} && {RunLocalBinaryCommand("app")}" : compile;
        }

        return runAfterBuild
            ? $"compiler=$(command -v c++ || command -v g++ || true); if [ -n \"$compiler\" ]; then \"$compiler\" -std=c++17 -O2 {sourceArgs} -o {output} && {RunLocalBinaryCommand("app")}; else echo 'c++ 없음'; exit 1; fi"
            : $"compiler=$(command -v c++ || command -v g++ || true); if [ -n \"$compiler\" ]; then \"$compiler\" -std=c++17 -fsyntax-only {sourceArgs}; else echo 'c++ 없음, 파일 생성 확인'; fi";
    }

    private static IReadOnlyList<string> ResolveExpectedOutputAssertionLines(
        string? expectedOutput,
        IReadOnlyList<string>? expectedOutputLines
    )
    {
        if (expectedOutputLines != null && expectedOutputLines.Count > 0)
        {
            return expectedOutputLines
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim())
                .ToArray();
        }

        var trimmed = expectedOutput?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(trimmed) ? Array.Empty<string>() : new[] { trimmed };
    }

    private static void AppendExpectedOutputAssertions(StringBuilder builder, IReadOnlyList<string> expectedLines)
    {
        if (builder == null || expectedLines == null || expectedLines.Count == 0)
        {
            return;
        }

        builder.Append("__omni_line_count=$(awk 'NF{count++} END{print count+0}' \"$tmp_out\"); ");
        builder.Append($"if [ \"$__omni_line_count\" -lt {expectedLines.Count} ]; then printf '%s\\n' {EscapeShellArg($"expected at least {expectedLines.Count} stdout lines")} >&2; rm -f \"$tmp_out\" \"$tmp_err\"; exit 1; fi; ");

        for (var index = 0; index < expectedLines.Count; index++)
        {
            var expectedLine = expectedLines[index].Trim();
            if (string.IsNullOrWhiteSpace(expectedLine))
            {
                continue;
            }

            var lineArg = EscapeShellArg(expectedLine);
            var failureMessageArg = EscapeShellArg($"expected line {index + 1} mismatch: {expectedLine}");
            builder.Append($"if [ \"$(awk 'NF{{seen++; if (seen=={index + 1}) {{ print; exit }} }}' \"$tmp_out\")\" != {lineArg} ]; then printf '%s\\n' {failureMessageArg} >&2; rm -f \"$tmp_out\" \"$tmp_err\"; exit 1; fi; ");
        }
    }

    private static string BuildWindowsExpectedOutputAssertionCommand(string command, IReadOnlyList<string> expectedLines)
    {
        if (expectedLines == null || expectedLines.Count == 0)
        {
            return command;
        }

        var expectedJson = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("\n", expectedLines.Select(line => line.Trim()))));
        var commandJson = Convert.ToBase64String(Encoding.UTF8.GetBytes(command));
        var script = """
$command = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($env:OMNI_COMMAND_B64))
$expected = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($env:OMNI_EXPECTED_B64)).Split("`n")
$outFile = [IO.Path]::GetTempFileName()
$errFile = [IO.Path]::GetTempFileName()
cmd.exe /c $command 1> $outFile 2> $errFile
$status = $LASTEXITCODE
$stdout = Get-Content -Raw -ErrorAction SilentlyContinue $outFile
$stderr = Get-Content -Raw -ErrorAction SilentlyContinue $errFile
if ($stdout) { [Console]::Out.Write($stdout) }
if ($stderr) { [Console]::Error.Write($stderr) }
Remove-Item -Force -ErrorAction SilentlyContinue $outFile, $errFile
if ($status -ne 0) { exit $status }
$lines = @()
if ($stdout) { $lines = $stdout -split "`r?`n" | Where-Object { $_.Trim().Length -gt 0 } }
if ($lines.Count -lt $expected.Count) { [Console]::Error.WriteLine("expected at least {0} stdout lines" -f $expected.Count); exit 1 }
for ($i = 0; $i -lt $expected.Count; $i++) {
  if ($lines[$i].Trim() -ne $expected[$i].Trim()) {
    [Console]::Error.WriteLine("expected line {0} mismatch: {1}" -f ($i + 1), $expected[$i])
    exit 1
  }
}
""";
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return $"set \"OMNI_COMMAND_B64={commandJson}\" && set \"OMNI_EXPECTED_B64={expectedJson}\" && powershell -NoProfile -ExecutionPolicy Bypass -EncodedCommand {encodedScript}";
    }

    private static string TryBuildStructuredHtmlVerificationCommand(
        string workspaceRoot,
        string objectiveText,
        IReadOnlyCollection<string> changedFiles,
        IReadOnlyList<string>? requestedPaths
    )
    {
        var indexPath = TryResolveStructuredRelativePath(workspaceRoot, changedFiles, requestedPaths, "index.html");
        var cssPath = TryResolveStructuredRelativePath(workspaceRoot, changedFiles, requestedPaths, "styles.css");
        var scriptPath = TryResolveStructuredRelativePath(workspaceRoot, changedFiles, requestedPaths, "app.js");
        if (string.IsNullOrWhiteSpace(indexPath) || string.IsNullOrWhiteSpace(cssPath) || string.IsNullOrWhiteSpace(scriptPath))
        {
            return string.Empty;
        }

        if (OperatingSystem.IsWindows())
        {
            return BuildWindowsStructuredHtmlVerificationCommand(workspaceRoot, objectiveText, indexPath, cssPath, scriptPath);
        }

        var workspaceSafe = workspaceRoot.Replace("'", "'\"'\"'", StringComparison.Ordinal);
        var commands = new List<string>
        {
            $"cd '{workspaceSafe}'",
            $"ls -1 {EscapeShellArg(indexPath)} {EscapeShellArg(cssPath)} {EscapeShellArg(scriptPath)}",
            $"__omni_first_html=$(awk 'NF{{print tolower($0); exit}}' {EscapeShellArg(indexPath)}); case \"$__omni_first_html\" in '<!doctype html>'*|'<html'*) : ;; *) echo 'index.html must start with html' >&2; exit 1 ;; esac",
            $"! grep -Fq -- '#!/usr/bin/env bash' {EscapeShellArg(indexPath)} {EscapeShellArg(cssPath)} {EscapeShellArg(scriptPath)}",
            $"! grep -Fq -- 'cat > ' {EscapeShellArg(indexPath)} {EscapeShellArg(cssPath)} {EscapeShellArg(scriptPath)}",
            $"grep -Fq -- 'dashboard-root' {EscapeShellArg(indexPath)}",
            $"if command -v node >/dev/null 2>&1; then node --check {EscapeShellArg(scriptPath)}; else echo 'node 없음'; exit 1; fi"
        };

        if (objectiveText.Contains("bucket-card", StringComparison.OrdinalIgnoreCase))
        {
            commands.Add($"grep -Fq -- 'bucket-card' {EscapeShellArg(scriptPath)}");
        }

        if (objectiveText.Contains("border-radius: 16px", StringComparison.OrdinalIgnoreCase))
        {
            commands.Add($"grep -Fq -- 'border-radius: 16px' {EscapeShellArg(cssPath)}");
        }

        if (objectiveText.Contains("domcontentloaded", StringComparison.OrdinalIgnoreCase)
            || objectiveText.Contains("document.addeventlistener", StringComparison.OrdinalIgnoreCase))
        {
            commands.Add($"grep -Fq -- 'DOMContentLoaded' {EscapeShellArg(scriptPath)}");
        }

        return string.Join(" && ", commands);
    }

    private static string TryBuildStaticHtmlPlaywrightSmokeCommand(string workspaceRoot, IReadOnlyCollection<string> changedFiles)
    {
        var indexFile = (changedFiles ?? Array.Empty<string>())
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path)
                                    && File.Exists(path)
                                    && Path.GetFileName(path).Equals("index.html", StringComparison.OrdinalIgnoreCase))
            ?? (File.Exists(Path.Combine(workspaceRoot, "index.html")) ? Path.Combine(workspaceRoot, "index.html") : string.Empty);
        if (string.IsNullOrWhiteSpace(indexFile))
        {
            return string.Empty;
        }

        var packageJsonPath = Path.Combine(workspaceRoot, "package.json");
        var packageText = File.Exists(packageJsonPath) ? SafeReadAllText(packageJsonPath) : "{}";
        return $"{BuildChangeDirectoryCommand(workspaceRoot)} && {BuildPlaywrightProjectSmokeCommand(workspaceRoot, packageText)}";
    }

    private static string BuildWindowsStructuredHtmlVerificationCommand(
        string workspaceRoot,
        string objectiveText,
        string indexPath,
        string cssPath,
        string scriptPath
    )
    {
        var checks = new List<string>
        {
            "if (!(Test-Path $index) -or !(Test-Path $css) -or !(Test-Path $script)) { throw 'structured html files missing' }",
            "$first = (Get-Content -Raw $index).TrimStart().ToLowerInvariant(); if (!($first.StartsWith('<!doctype html>') -or $first.StartsWith('<html'))) { throw 'index.html must start with html' }",
            "$merged = (Get-Content -Raw $index) + \"`n\" + (Get-Content -Raw $css) + \"`n\" + (Get-Content -Raw $script); if ($merged.Contains('#!/usr/bin/env bash') -or $merged.Contains('cat > ')) { throw 'shell script leaked into frontend files' }",
            "if ((Get-Content -Raw $index) -notmatch 'dashboard-root') { throw 'dashboard-root missing' }",
            "node --check $script"
        };
        if (objectiveText.Contains("bucket-card", StringComparison.OrdinalIgnoreCase))
        {
            checks.Add("if ((Get-Content -Raw $script) -notmatch 'bucket-card') { throw 'bucket-card missing' }");
        }

        if (objectiveText.Contains("border-radius: 16px", StringComparison.OrdinalIgnoreCase))
        {
            checks.Add("if ((Get-Content -Raw $css) -notmatch 'border-radius:\\s*16px') { throw 'border-radius 16px missing' }");
        }

        if (objectiveText.Contains("domcontentloaded", StringComparison.OrdinalIgnoreCase)
            || objectiveText.Contains("document.addeventlistener", StringComparison.OrdinalIgnoreCase))
        {
            checks.Add("if ((Get-Content -Raw $script) -notmatch 'DOMContentLoaded') { throw 'DOMContentLoaded missing' }");
        }

        var script = "$ErrorActionPreference='Stop'\n"
            + $"Set-Location {EscapePowerShellString(workspaceRoot)}\n"
            + $"$index={EscapePowerShellString(indexPath)}\n"
            + $"$css={EscapePowerShellString(cssPath)}\n"
            + $"$script={EscapePowerShellString(scriptPath)}\n"
            + string.Join("\n", checks);
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return $"powershell -NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}";
    }

    private static string EscapePowerShellString(string value)
    {
        return "'" + (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    private static string TryBuildNodeProjectVerificationCommand(
        string workspaceRoot,
        string objectiveText,
        IReadOnlyCollection<string> changedFiles,
        bool shouldRunProgram
    )
    {
        var projectRoot = FindNearestExistingParentWithFile(workspaceRoot, changedFiles, "package.json");
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return string.Empty;
        }

        var packageJson = Path.Combine(projectRoot, "package.json");
        var packageText = SafeReadAllText(packageJson);
        var rootSafe = EscapeShellArg(projectRoot);
        if (OperatingSystem.IsWindows())
        {
            var windowsCommands = new List<string>
            {
                $"cd /d {rootSafe}",
                "npm install --no-fund --no-audit"
            };
            if (packageText.Contains("\"build\"", StringComparison.OrdinalIgnoreCase))
            {
                windowsCommands.Add("npm run build");
            }
            else if (Directory.EnumerateFiles(projectRoot, "*.ts", SearchOption.AllDirectories).Any()
                     || Directory.EnumerateFiles(projectRoot, "*.tsx", SearchOption.AllDirectories).Any())
            {
                windowsCommands.Add("if exist node_modules\\.bin\\tsc.cmd (npx tsc --noEmit) else (for /r %f in (*.js *.mjs *.cjs) do (node --check \"%f\" & goto :omni_node_checked) & :omni_node_checked)");
            }
            else
            {
                var jsFile = SelectChangedFilesByExtension(workspaceRoot, changedFiles, ".js", ".mjs", ".cjs").FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(jsFile))
                {
                    windowsCommands.Add($"node --check {EscapeShellArg(jsFile)}");
                }
            }

            if (shouldRunProgram && packageText.Contains("\"start\"", StringComparison.OrdinalIgnoreCase)
                && !ContainsAny(objectiveText.ToLowerInvariant(), "react", "vite", "브라우저", "web", "웹"))
            {
                windowsCommands.Add("npm start");
            }
            else if (LooksLikeBrowserAppObjective(objectiveText))
            {
                var smoke = BuildPlaywrightProjectSmokeCommand(projectRoot, packageText);
                if (!string.IsNullOrWhiteSpace(smoke))
                {
                    windowsCommands.Add(smoke);
                }
            }

            return string.Join(" && ", windowsCommands.Where(item => !string.IsNullOrWhiteSpace(item)));
        }

        var commands = new List<string>
        {
            $"cd {rootSafe}",
            "npm install --no-fund --no-audit"
        };
        if (packageText.Contains("\"build\"", StringComparison.OrdinalIgnoreCase))
        {
            commands.Add("npm run build");
        }
        else if (Directory.EnumerateFiles(projectRoot, "*.ts", SearchOption.AllDirectories).Any()
                 || Directory.EnumerateFiles(projectRoot, "*.tsx", SearchOption.AllDirectories).Any())
        {
            commands.Add("if [ -x node_modules/.bin/tsc ]; then npx tsc --noEmit; else node --check $(find . -maxdepth 3 -type f \\( -name '*.js' -o -name '*.mjs' -o -name '*.cjs' \\) | head -1); fi");
        }
        else
        {
            var jsFile = SelectChangedFilesByExtension(workspaceRoot, changedFiles, ".js", ".mjs", ".cjs").FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(jsFile))
            {
                commands.Add($"node --check {EscapeShellArg(jsFile)}");
            }
        }

        if (shouldRunProgram && packageText.Contains("\"start\"", StringComparison.OrdinalIgnoreCase)
            && !ContainsAny(objectiveText.ToLowerInvariant(), "react", "vite", "브라우저", "web", "웹"))
        {
            commands.Add("npm start");
        }
        else if (LooksLikeBrowserAppObjective(objectiveText))
        {
            var smoke = BuildPlaywrightProjectSmokeCommand(projectRoot, packageText);
            if (!string.IsNullOrWhiteSpace(smoke))
            {
                commands.Add(smoke);
            }
        }

        return string.Join(" && ", commands.Where(command => !string.IsNullOrWhiteSpace(command)));
    }

    private static string BuildPlaywrightProjectSmokeCommand(string projectRoot, string packageText)
    {
        var hasDev = packageText.Contains("\"dev\"", StringComparison.OrdinalIgnoreCase);
        var hasStart = packageText.Contains("\"start\"", StringComparison.OrdinalIgnoreCase);
        if (!hasDev && !hasStart && !File.Exists(Path.Combine(projectRoot, "index.html")))
        {
            return string.Empty;
        }

        var script = """
const { chromium } = require('playwright');
const http = require('http');
const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');

async function waitFor(url, deadlineMs = 12000) {
  const end = Date.now() + deadlineMs;
  while (Date.now() < end) {
    try {
      await new Promise((resolve, reject) => {
        const req = http.get(url, (res) => { res.resume(); resolve(); });
        req.on('error', reject);
        req.setTimeout(1200, () => { req.destroy(new Error('timeout')); });
      });
      return;
    } catch (_) {
      await new Promise((resolve) => setTimeout(resolve, 300));
    }
  }
  throw new Error('dev server did not become ready');
}

(async () => {
  let child = null;
  let url = 'http://127.0.0.1:41739';
  if (fs.existsSync(path.join(process.cwd(), 'package.json'))) {
    const pkg = JSON.parse(fs.readFileSync('package.json', 'utf8'));
    const script = pkg.scripts && (pkg.scripts.dev ? 'dev' : pkg.scripts.start ? 'start' : '');
    if (script) {
      child = spawn(process.platform === 'win32' ? 'npm.cmd' : 'npm', ['run', script, '--', '--host', '127.0.0.1', '--port', '41739'], { stdio: 'ignore' });
      await waitFor(url);
    }
  }
  if (!child) {
    url = 'file://' + path.resolve('index.html');
  }
  const browser = await chromium.launch({ headless: true });
  const page = await browser.newPage({ viewport: { width: 390, height: 844 } });
  const errors = [];
  page.on('console', (msg) => { if (msg.type() === 'error') errors.push(msg.text()); });
  await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 15000 });
  const bodyText = (await page.locator('body').innerText({ timeout: 5000 }).catch(() => '')).trim();
  const canvasVisible = await page.locator('canvas').first().evaluate((canvas) => {
    const box = canvas.getBoundingClientRect();
    return box.width > 0 && box.height > 0;
  }).catch(() => false);
  await browser.close();
  if (child) child.kill();
  if (!bodyText && !canvasVisible) throw new Error('browser smoke failed: empty body and no visible canvas');
  if (errors.length) throw new Error('browser console errors: ' + errors.slice(0, 3).join(' | '));
  console.log('playwright smoke ok');
})().catch((err) => {
  if (child) child.kill();
  console.error(err.message || err);
  process.exit(1);
});
""";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(script));
        return $"node -e \"eval(Buffer.from('{encoded}','base64').toString('utf8'))\"";
    }

    private static string TryBuildPythonProjectVerificationCommand(
        string workspaceRoot,
        IReadOnlyCollection<string> changedFiles,
        bool shouldRunProgram
    )
    {
        var projectRoot = FindNearestExistingParentWithFile(workspaceRoot, changedFiles, "pyproject.toml")
            ?? FindNearestExistingParentWithFile(workspaceRoot, changedFiles, "requirements.txt");
        var hasTests = Directory.Exists(Path.Combine(workspaceRoot, "tests"))
            || Directory.EnumerateFiles(workspaceRoot, "test_*.py", SearchOption.AllDirectories).Any()
            || Directory.EnumerateFiles(workspaceRoot, "*_test.py", SearchOption.AllDirectories).Any();
        if (string.IsNullOrWhiteSpace(projectRoot) && !hasTests)
        {
            return string.Empty;
        }

        projectRoot ??= workspaceRoot;
        var rootSafe = EscapeShellArg(projectRoot);
        var pythonRunnerPrefix = BuildPythonRunnerPrefixCommand();
        var pythonRunner = BuildPythonRunnerToken();
        var commands = new List<string> { BuildChangeDirectoryCommand(projectRoot), pythonRunnerPrefix };
        if (File.Exists(Path.Combine(projectRoot, "requirements.txt")))
        {
            commands.Add($"{pythonRunner} -m pip install --disable-pip-version-check -r requirements.txt");
        }

        if (File.Exists(Path.Combine(projectRoot, "pyproject.toml")))
        {
            commands.Add($"{pythonRunner} -m pip install --disable-pip-version-check -e .");
        }

        commands.Add($"{pythonRunner} -m compileall -q .");

        if (Directory.Exists(Path.Combine(projectRoot, "tests"))
            || Directory.EnumerateFiles(projectRoot, "test_*.py", SearchOption.AllDirectories).Any()
            || Directory.EnumerateFiles(projectRoot, "*_test.py", SearchOption.AllDirectories).Any())
        {
            commands.Add($"{pythonRunner} -m pytest -q");
        }
        else if (shouldRunProgram)
        {
            var entry = SelectEntryLikeChangedFile("python", changedFiles);
            if (!string.IsNullOrWhiteSpace(entry) && IsPathUnderRoot(entry, projectRoot))
            {
                commands.Add($"{pythonRunner} {EscapeShellArg(ToWorkspaceRelativePath(projectRoot, entry))}");
            }
        }

        return string.Join(" && ", commands.Where(command => !string.IsNullOrWhiteSpace(command)));
    }

    private static string TryBuildDotnetProjectVerificationCommand(
        string workspaceRoot,
        IReadOnlyCollection<string> changedFiles,
        bool shouldRunProgram
    )
    {
        var projectFile = FindNearestChangedFileByExtension(changedFiles, ".csproj");
        if (string.IsNullOrWhiteSpace(projectFile))
        {
            projectFile = Directory.EnumerateFiles(workspaceRoot, "*.csproj", SearchOption.AllDirectories).FirstOrDefault() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(projectFile))
        {
            return string.Empty;
        }

        var safeProject = EscapeShellArg(projectFile);
        return shouldRunProgram
            ? $"dotnet restore {safeProject} && dotnet build {safeProject} --nologo --verbosity quiet && dotnet run --no-restore --project {safeProject} --no-launch-profile"
            : $"dotnet restore {safeProject} && dotnet build {safeProject} --nologo --verbosity quiet";
    }

    private static string TryBuildJavaProjectVerificationCommand(
        string workspaceRoot,
        IReadOnlyCollection<string> changedFiles,
        bool shouldRunProgram
    )
    {
        var gradlew = FindNearestExistingParentWithFile(workspaceRoot, changedFiles, "gradlew");
        if (!string.IsNullOrWhiteSpace(gradlew))
        {
            var gradleCommand = OperatingSystem.IsWindows()
                ? "gradlew.bat"
                : "chmod +x ./gradlew && ./gradlew";
            return $"{BuildChangeDirectoryCommand(gradlew)} && {gradleCommand} --no-daemon {(shouldRunProgram ? "run" : "build")}";
        }

        var gradleRoot = FindNearestExistingParentWithFile(workspaceRoot, changedFiles, "build.gradle");
        if (string.IsNullOrWhiteSpace(gradleRoot))
        {
            gradleRoot = FindNearestExistingParentWithFile(workspaceRoot, changedFiles, "build.gradle.kts");
        }
        if (!string.IsNullOrWhiteSpace(gradleRoot))
        {
            return $"{BuildChangeDirectoryCommand(gradleRoot)} && {BuildRequiredProgramCommand("gradle", $"gradle --no-daemon {(shouldRunProgram ? "run" : "build")}", "gradle 없음")}";
        }

        var mavenRoot = FindNearestExistingParentWithFile(workspaceRoot, changedFiles, "pom.xml");
        if (!string.IsNullOrWhiteSpace(mavenRoot))
        {
            return $"{BuildChangeDirectoryCommand(mavenRoot)} && {BuildRequiredProgramCommand("mvn", $"mvn -q {(shouldRunProgram ? "compile exec:java" : "test -DskipTests")}", "mvn 없음")}";
        }

        return string.Empty;
    }

    private static string TryBuildKotlinProjectVerificationCommand(
        string workspaceRoot,
        IReadOnlyCollection<string> changedFiles,
        bool shouldRunProgram
    )
    {
        var gradleRoot = FindNearestExistingParentWithFile(workspaceRoot, changedFiles, "gradlew")
            ?? FindNearestExistingParentWithFile(workspaceRoot, changedFiles, "build.gradle.kts")
            ?? FindNearestExistingParentWithFile(workspaceRoot, changedFiles, "build.gradle");
        if (string.IsNullOrWhiteSpace(gradleRoot))
        {
            return string.Empty;
        }

        if (File.Exists(Path.Combine(gradleRoot, "gradlew")))
        {
            var command = OperatingSystem.IsWindows()
                ? "gradlew.bat"
                : "chmod +x ./gradlew && ./gradlew";
            return $"{BuildChangeDirectoryCommand(gradleRoot)} && {command} --no-daemon {(shouldRunProgram ? "run" : "build")}";
        }

        return $"{BuildChangeDirectoryCommand(gradleRoot)} && {BuildRequiredProgramCommand("gradle", $"gradle --no-daemon {(shouldRunProgram ? "run" : "build")}", "gradle 없음")}";
    }

    private static string TryBuildNativeProjectVerificationCommand(
        string workspaceRoot,
        IReadOnlyCollection<string> changedFiles,
        string language,
        bool shouldRunProgram
    )
    {
        var cmakeRoot = FindNearestExistingParentWithFile(workspaceRoot, changedFiles, "CMakeLists.txt");
        if (!string.IsNullOrWhiteSpace(cmakeRoot))
        {
            var runCommand = OperatingSystem.IsWindows()
                ? "for /r build %f in (*.exe) do (%f & exit /b %ERRORLEVEL%) & echo 실행 파일을 찾지 못했습니다. 1>&2 & exit /b 1"
                : "__omni_bin=$(find build -type f -perm -111 | head -1); if [ -n \"$__omni_bin\" ]; then \"$__omni_bin\"; else echo '실행 파일을 찾지 못했습니다.'; exit 1; fi";
            return $"{BuildChangeDirectoryCommand(cmakeRoot)} && cmake -S . -B build && cmake --build build && {(shouldRunProgram ? runCommand : (OperatingSystem.IsWindows() ? "echo native build ok" : "true"))}";
        }

        var makeRoot = FindNearestExistingParentWithFile(workspaceRoot, changedFiles, "Makefile");
        if (!string.IsNullOrWhiteSpace(makeRoot))
        {
            var runCommand = OperatingSystem.IsWindows()
                ? "for /r . %f in (*.exe) do (%f & exit /b %ERRORLEVEL%) & echo 실행 파일을 찾지 못했습니다. 1>&2 & exit /b 1"
                : "__omni_bin=$(find . -maxdepth 2 -type f -perm -111 | grep -v '/\\.' | head -1); if [ -n \"$__omni_bin\" ]; then \"$__omni_bin\"; else echo '실행 파일을 찾지 못했습니다.'; exit 1; fi";
            return $"{BuildChangeDirectoryCommand(makeRoot)} && make && {(shouldRunProgram ? runCommand : (OperatingSystem.IsWindows() ? "echo native build ok" : "true"))}";
        }

        return string.Empty;
    }

    private static string? FindNearestExistingParentWithFile(string workspaceRoot, IReadOnlyCollection<string> changedFiles, string fileName)
    {
        foreach (var changedFile in changedFiles ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(changedFile))
            {
                continue;
            }

            var dir = File.Exists(changedFile) ? Path.GetDirectoryName(changedFile) : changedFile;
            while (!string.IsNullOrWhiteSpace(dir) && IsPathUnderRoot(dir, workspaceRoot))
            {
                if (File.Exists(Path.Combine(dir, fileName)))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }
        }

        return File.Exists(Path.Combine(workspaceRoot, fileName)) ? workspaceRoot : null;
    }

    private static string FindNearestChangedFileByExtension(IReadOnlyCollection<string> changedFiles, string extension)
    {
        return (changedFiles ?? Array.Empty<string>())
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path)
                                    && File.Exists(path)
                                    && path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
    }

    private static string TryResolveStructuredRelativePath(
        string workspaceRoot,
        IReadOnlyCollection<string> changedFiles,
        IReadOnlyList<string>? requestedPaths,
        string targetFileName
    )
    {
        if (requestedPaths != null)
        {
            var requested = requestedPaths.FirstOrDefault(path =>
                !string.IsNullOrWhiteSpace(path)
                && string.Equals(Path.GetFileName(path), targetFileName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(requested))
            {
                return requested.Replace('\\', '/');
            }
        }

        var changed = changedFiles.FirstOrDefault(path =>
            !string.IsNullOrWhiteSpace(path)
            && File.Exists(path)
            && string.Equals(Path.GetFileName(path), targetFileName, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(changed))
        {
            return string.Empty;
        }

        return ToWorkspaceRelativePath(workspaceRoot, changed);
    }

    private static string[] SelectChangedFilesByExtension(string workspaceRoot, IReadOnlyCollection<string> changedFiles, params string[] extensions)
    {
        if (changedFiles == null || changedFiles.Count == 0 || extensions == null || extensions.Length == 0)
        {
            return Array.Empty<string>();
        }

        var normalizedExtensions = new HashSet<string>(
            extensions
                .Where(extension => !string.IsNullOrWhiteSpace(extension))
                .Select(extension => extension.StartsWith(".", StringComparison.Ordinal) ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase
        );

        return changedFiles
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Where(path => normalizedExtensions.Contains(Path.GetExtension(path)))
            .Select(path => ToWorkspaceRelativePath(workspaceRoot, path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> CollectPythonThirdPartyPackagesFromSources(IEnumerable<string> sourceFiles)
    {
        var packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceFile in sourceFiles ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(sourceFile) || !File.Exists(sourceFile))
            {
                continue;
            }

            foreach (var package in ExtractPythonPackagesFromSource(sourceFile))
            {
                if (!string.IsNullOrWhiteSpace(package))
                {
                    packages.Add(package);
                }
            }
        }

        return packages.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool LooksLikeInteractivePythonGameSource(IEnumerable<string> sourceFiles)
    {
        foreach (var sourceFile in sourceFiles ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(sourceFile) || !File.Exists(sourceFile))
            {
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(sourceFile);
            }
            catch
            {
                continue;
            }

            if (Regex.IsMatch(text, @"\bimport\s+(pygame|pyglet|arcade|panda3d|kivy|tkinter|curses|turtle)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || Regex.IsMatch(text, @"\bfrom\s+(pygame|pyglet|arcade|panda3d|kivy|tkinter|curses|turtle)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || Regex.IsMatch(text, @"\bpygame\.(display|event|draw|time)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || Regex.IsMatch(text, @"\bmainloop\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || Regex.IsMatch(text, @"\bgetch\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || Regex.IsMatch(text, @"\bnodelay\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || Regex.IsMatch(text, @"\bCanvas\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || Regex.IsMatch(text, @"\bScreen\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || Regex.IsMatch(text, @"\b(onkey|bind|bind_all)\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> CollectInteractivePythonModulesFromSources(IEnumerable<string> sourceFiles)
    {
        var modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceFile in sourceFiles ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(sourceFile) || !File.Exists(sourceFile))
            {
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(sourceFile);
            }
            catch
            {
                continue;
            }

            if (Regex.IsMatch(text, @"\bimport\s+tkinter\b|\bfrom\s+tkinter\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                modules.Add("tkinter");
            }

            if (Regex.IsMatch(text, @"\bimport\s+pygame\b|\bfrom\s+pygame\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                modules.Add("pygame");
            }

            if (Regex.IsMatch(text, @"\bimport\s+pyglet\b|\bfrom\s+pyglet\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                modules.Add("pyglet");
            }

            if (Regex.IsMatch(text, @"\bimport\s+arcade\b|\bfrom\s+arcade\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                modules.Add("arcade");
            }

            if (Regex.IsMatch(text, @"\bimport\s+curses\b|\bfrom\s+curses\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                modules.Add("curses");
            }

            if (Regex.IsMatch(text, @"\bimport\s+turtle\b|\bfrom\s+turtle\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                modules.Add("turtle");
            }
        }

        return modules.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string BuildInteractivePythonModuleAvailabilityCommand(IEnumerable<string> moduleNames)
    {
        var modules = (moduleNames ?? Array.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (modules.Length == 0)
        {
            return "python3 -c 'import sys; sys.exit(0)'";
        }

        const string script = "import importlib, sys; missing = []; exec(\"for mod in sys.argv[1:]:\\n    try:\\n        importlib.import_module(mod)\\n    except Exception as ex:\\n        missing.append(f\\\"{mod}: {ex.__class__.__name__}: {ex}\\\")\"); missing and print(\"interactive python game unavailable modules: \" + \" | \".join(missing), file=sys.stderr); sys.exit(1 if missing else 0)";
        return $"python3 -c {EscapeShellArg(script)} {JoinShellArgs(modules)}";
    }

    private static string BuildPythonGameStaticQualityCommand(string objectiveText, IEnumerable<string> sourceFiles)
    {
        var requiredMarkers = new List<string>
        {
            "loop",
            "events",
            "render"
        };
        var text = ExtractLatestCodingRequestText(WebUtility.HtmlDecode(objectiveText ?? string.Empty)).ToLowerInvariant();
        if (ContainsAny(text, "tetris", "테트리스"))
        {
            requiredMarkers.AddRange(new[] { "board", "pieces", "rotation", "collision", "line_clear", "score", "level", "game_over" });
        }

        const string script = """
import pathlib, re, sys
markers = set(sys.argv[1].split(",")) if len(sys.argv) > 1 and sys.argv[1] else set()
paths = [pathlib.Path(p) for p in sys.argv[2:]]
source = "\n".join(p.read_text(encoding="utf-8", errors="ignore") for p in paths if p.exists()).lower()
fail = []
checks = {
    "loop": r"while\s+[^:\n]+:|for\s+\w+\s+in\s+range\s*\(",
    "events": r"pygame\.event\.get|key\.get_pressed|getch\s*\(|bind\s*\(|onkey\s*\(",
    "render": r"pygame\.display\.flip|pygame\.display\.update|pygame\.draw\.|canvas|screen\.update|refresh\s*\(",
    "board": r"10\s*,\s*20|cols\s*=\s*10|rows\s*=\s*20|width\s*=\s*10|height\s*=\s*20|board",
    "pieces": r"shape|tetromino|pieces?|blocks?|블록",
    "rotation": r"rotat|회전",
    "collision": r"collid|collision|충돌|valid_position|check_",
    "line_clear": r"clear.*line|line.*clear|remove.*line|라인",
    "score": r"score|점수",
    "level": r"level|레벨",
    "game_over": r"game_over|game over|게임\s*오버"
}
if source.count("print(") > 0 and not re.search(checks["loop"], source):
    fail.append("print-only simulation is insufficient")
for marker in sorted(markers):
    if marker in checks and not re.search(checks[marker], source):
        fail.append(f"missing game feature marker: {marker}")
if fail:
    print("; ".join(fail), file=sys.stderr)
    sys.exit(1)
""";
        return $"python3 -c {EscapeShellArg(script)} {EscapeShellArg(string.Join(",", requiredMarkers.Distinct(StringComparer.OrdinalIgnoreCase)))} {JoinShellArgs(sourceFiles.Select(path => path))}";
    }

    private static string ToWorkspaceRelativePath(string workspaceRoot, string path)
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

    private static string JoinShellArgs(IEnumerable<string> values)
    {
        return string.Join(
            " ",
            (values ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(EscapeShellArg)
        );
    }
}
