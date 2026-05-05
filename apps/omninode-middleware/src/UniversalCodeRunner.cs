using System.Diagnostics;

namespace OmniNode.Middleware;

public sealed class UniversalCodeRunner
{
    private readonly string _runsRootDir;
    private readonly int _timeoutSec;
    private readonly string _pythonBinary;

    public UniversalCodeRunner(string runsRootDir, int timeoutSec, string? pythonBinary = null)
    {
        _runsRootDir = Path.GetFullPath(runsRootDir);
        _timeoutSec = Math.Max(10, timeoutSec);
        _pythonBinary = string.IsNullOrWhiteSpace(pythonBinary)
            ? (OperatingSystem.IsWindows() ? "python" : "python3")
            : pythonBinary.Trim();
        Directory.CreateDirectory(_runsRootDir);
    }

    public async Task<CodeExecutionResult> ExecuteAsync(string language, string code, CancellationToken cancellationToken)
    {
        var normalizedLanguage = NormalizeLanguage(language);
        var runDir = Path.Combine(_runsRootDir, DateTimeOffset.UtcNow.ToString("yyyyMMdd"), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runDir);

        return normalizedLanguage switch
        {
            "python" => await RunScriptAsync(runDir, "main.py", code, $"{QuoteShellCommand(_pythonBinary)} main.py", normalizedLanguage, cancellationToken),
            "javascript" => await RunScriptAsync(runDir, "main.js", code, "node main.js", normalizedLanguage, cancellationToken),
            "bash" => await RunScriptAsync(runDir, "main.sh", code, "bash main.sh", normalizedLanguage, cancellationToken, chmodX: true),
            "c" => await RunCompiledAsync(runDir, "main.c", code, BuildNativeCompileCommand("cc", "main.c"), BuildNativeRunCommand(), normalizedLanguage, cancellationToken),
            "cpp" => await RunCompiledAsync(runDir, "main.cpp", code, BuildNativeCompileCommand("c++", "main.cpp", "-std=c++17"), BuildNativeRunCommand(), normalizedLanguage, cancellationToken),
            "csharp" => await RunCSharpAsync(runDir, code, cancellationToken),
            "java" => await RunCompiledAsync(runDir, "Main.java", code, "javac Main.java", "java Main", normalizedLanguage, cancellationToken),
            "kotlin" => await RunCompiledAsync(runDir, "Main.kt", code, "kotlinc Main.kt -include-runtime -d app.jar", "java -jar app.jar", normalizedLanguage, cancellationToken),
            "html" => await SaveStaticAsync(runDir, "index.html", code, normalizedLanguage, "HTML/CSS/JS는 실행 대신 파일로 저장했습니다."),
            "css" => await SaveStaticAsync(runDir, "styles.css", code, normalizedLanguage, "CSS는 실행 대신 파일로 저장했습니다."),
            _ => await RunScriptAsync(runDir, "main.txt", code, "bash main.txt", "bash", cancellationToken, chmodX: true)
        };
    }

    private async Task<CodeExecutionResult> RunCSharpAsync(string runDir, string code, CancellationToken cancellationToken)
    {
        var csprojPath = Path.Combine(runDir, "App.csproj");
        var programPath = Path.Combine(runDir, "Program.cs");

        var csproj = """
                     <Project Sdk=\"Microsoft.NET.Sdk\"> 
                       <PropertyGroup>
                         <OutputType>Exe</OutputType>
                         <TargetFramework>net9.0</TargetFramework>
                         <ImplicitUsings>enable</ImplicitUsings>
                         <Nullable>enable</Nullable>
                       </PropertyGroup>
                     </Project>
                     """;

        await File.WriteAllTextAsync(csprojPath, csproj, cancellationToken);
        await File.WriteAllTextAsync(programPath, code ?? string.Empty, cancellationToken);

        var runResult = await RunShellAsync("dotnet run --project App.csproj --configuration Release", runDir, cancellationToken);
        return new CodeExecutionResult(
            "csharp",
            runDir,
            programPath,
            "dotnet run --project App.csproj --configuration Release",
            runResult.ExitCode,
            runResult.StdOut,
            runResult.StdErr,
            runResult.TimedOut ? "timeout" : (runResult.ExitCode == 0 ? "ok" : "error")
        );
    }

    private async Task<CodeExecutionResult> RunScriptAsync(
        string runDir,
        string fileName,
        string code,
        string runCommand,
        string language,
        CancellationToken cancellationToken,
        bool chmodX = false
    )
    {
        var path = Path.Combine(runDir, fileName);
        await File.WriteAllTextAsync(path, code ?? string.Empty, cancellationToken);
        if (chmodX && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        var runResult = await RunShellAsync(runCommand, runDir, cancellationToken);
        return new CodeExecutionResult(
            language,
            runDir,
            path,
            runCommand,
            runResult.ExitCode,
            runResult.StdOut,
            runResult.StdErr,
            runResult.TimedOut ? "timeout" : (runResult.ExitCode == 0 ? "ok" : "error")
        );
    }

    private async Task<CodeExecutionResult> RunCompiledAsync(
        string runDir,
        string fileName,
        string code,
        string compileCommand,
        string runCommand,
        string language,
        CancellationToken cancellationToken
    )
    {
        var sourcePath = Path.Combine(runDir, fileName);
        await File.WriteAllTextAsync(sourcePath, code ?? string.Empty, cancellationToken);

        var compileResult = await RunShellAsync(compileCommand, runDir, cancellationToken);
        if (compileResult.ExitCode != 0 || compileResult.TimedOut)
        {
            return new CodeExecutionResult(
                language,
                runDir,
                sourcePath,
                compileCommand,
                compileResult.ExitCode,
                compileResult.StdOut,
                compileResult.StdErr,
                compileResult.TimedOut ? "timeout" : "error"
            );
        }

        var runResult = await RunShellAsync(runCommand, runDir, cancellationToken);
        var outText = (compileResult.StdOut + "\n" + runResult.StdOut).Trim();
        var errText = (compileResult.StdErr + "\n" + runResult.StdErr).Trim();
        return new CodeExecutionResult(
            language,
            runDir,
            sourcePath,
            $"{compileCommand} && {runCommand}",
            runResult.ExitCode,
            outText,
            errText,
            runResult.TimedOut ? "timeout" : (runResult.ExitCode == 0 ? "ok" : "error")
        );
    }

    private async Task<CodeExecutionResult> SaveStaticAsync(
        string runDir,
        string fileName,
        string content,
        string language,
        string message
    )
    {
        var path = Path.Combine(runDir, fileName);
        await File.WriteAllTextAsync(path, content ?? string.Empty);
        return new CodeExecutionResult(
            language,
            runDir,
            path,
            "(skipped)",
            0,
            message,
            string.Empty,
            "skipped"
        );
    }

    private async Task<ShellRunResult> RunShellAsync(string command, string workDir, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/zsh",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workDir
        };

        if (OperatingSystem.IsWindows())
        {
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(command);
        }
        else
        {
            startInfo.ArgumentList.Add("-lc");
            startInfo.ArgumentList.Add(command);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new ShellRunResult(127, string.Empty, ex.Message, false);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_timeoutSec));

        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
            return new ShellRunResult(
                process.ExitCode,
                await stdoutTask,
                await stderrTask,
                false
            );
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

            var stdout = string.Empty;
            var stderr = string.Empty;
            try
            {
                stdout = await stdoutTask;
            }
            catch
            {
            }

            try
            {
                stderr = await stderrTask;
            }
            catch
            {
            }

            return new ShellRunResult(124, stdout, string.IsNullOrWhiteSpace(stderr) ? "execution timed out" : stderr, true);
        }
    }

    private static string BuildNativeCompileCommand(string compiler, string sourceFile, string? standardFlag = null)
    {
        var output = OperatingSystem.IsWindows() ? "app.exe" : "app";
        var parts = new List<string> { compiler, "-O2" };
        if (!string.IsNullOrWhiteSpace(standardFlag))
        {
            parts.Add(standardFlag);
        }

        parts.Add(sourceFile);
        parts.Add("-o");
        parts.Add(output);
        return string.Join(" ", parts);
    }

    private static string BuildNativeRunCommand()
    {
        return OperatingSystem.IsWindows() ? "app.exe" : "./app";
    }

    private static string QuoteShellCommand(string value)
    {
        if (!value.Any(char.IsWhiteSpace))
        {
            return value;
        }

        if (OperatingSystem.IsWindows())
        {
            return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        }

        return "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    }

    private static string NormalizeLanguage(string raw)
    {
        var value = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            "py" => "python",
            "python3" => "python",
            "js" => "javascript",
            "node" => "javascript",
            "shell" => "bash",
            "sh" => "bash",
            "c++" => "cpp",
            "cc" => "cpp",
            "c#" => "csharp",
            "cs" => "csharp",
            "kt" => "kotlin",
            "htm" => "html",
            _ => string.IsNullOrWhiteSpace(value) || value == "auto" ? "python" : value
        };
    }

    private sealed record ShellRunResult(int ExitCode, string StdOut, string StdErr, bool TimedOut);
}

public sealed record CodeExecutionResult(
    string Language,
    string RunDirectory,
    string EntryFile,
    string Command,
    int ExitCode,
    string StdOut,
    string StdErr,
    string Status
);
