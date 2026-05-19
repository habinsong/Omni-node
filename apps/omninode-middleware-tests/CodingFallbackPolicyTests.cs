using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class CodingFallbackPolicyTests
{
    [Fact]
    public void BuildCodeOnlyPromptIncludesLanguageProjectAndRules()
    {
        var prompt = CodingFallbackPolicy.BuildCodeOnlyPrompt(
            "hello.py 파일 하나에 'ok' 출력",
            "python",
            "python-script",
            new[] { "- Python rule" }
        );

        Assert.Contains("LANGUAGE=<언어>", prompt);
        Assert.Contains("단 하나의 코드블록", prompt);
        Assert.Contains("언어 힌트: python", prompt);
        Assert.Contains("프로젝트 프로파일: python-script", prompt);
        Assert.Contains("- Python rule", prompt);
    }

    [Fact]
    public void BuildFileBundlePromptIncludesProjectProfileAndRequestedPaths()
    {
        var prompt = CodingFallbackPolicy.BuildFileBundlePrompt(
            "Python CLI 프로젝트 작성",
            "python",
            new CodingFallbackProjectProfile(
                "python-cli",
                new[] { "main.py", "requirements.txt" },
                "python3 -m compileall -q .",
                "python3 -m pytest -q",
                new[] { "python3 main.py" }
            ),
            new[] { "- CLI rule" },
            new[] { "main.py" }
        );

        Assert.Contains("반드시 JSON 객체만 출력", prompt);
        Assert.Contains("프로젝트 프로파일: python-cli", prompt);
        Assert.Contains("권장 엔트리: main.py, requirements.txt", prompt);
        Assert.Contains("- python3 main.py", prompt);
        Assert.Contains("- main.py", prompt);
    }

    [Fact]
    public void ExtractFallbackCodeUsesLastCodeFenceLanguage()
    {
        var parsed = CodingFallbackPolicy.ExtractFallbackCode(
            """
            설명
            ```text
            ignore
            ```
            ```ts
            console.log("ok")
            ```
            """,
            "python",
            "TypeScript 코드"
        );

        Assert.Equal("typescript", parsed.Language);
        Assert.Equal("console.log(\"ok\")", parsed.Code);
    }

    [Fact]
    public void ExtractFallbackCodeReadsJsonContentAndPathLanguage()
    {
        var parsed = CodingFallbackPolicy.ExtractFallbackCode(
            """{"files":[{"path":"src/app.js","content":"console.log(\"ok\")"}]}""",
            "python",
            "코드 작성"
        );

        Assert.Equal("javascript", parsed.Language);
        Assert.Equal("console.log(\"ok\")", parsed.Code);
    }

    [Fact]
    public void ExtractFallbackFileBundleNormalizesFilesAndRunCommand()
    {
        var bundle = CodingFallbackPolicy.ExtractFallbackFileBundle(
            """
            ```json
            {
              "language": "py",
              "run": "python3 app.py [stdout 포함: ok]",
              "files": [
                {"path": "workspace/coding/runs/abc/app.py", "content": "  print('ok')\n"},
                {"path": "../bad.py", "content": "print('bad')"}
              ]
            }
            ```
            """,
            "auto",
            "app.py 작성"
        );

        Assert.Equal("python", bundle.Language);
        Assert.Equal("python3 app.py", bundle.RunCommand);
        Assert.Single(bundle.Files);
        Assert.Equal("app.py", bundle.Files[0].Path);
        Assert.Equal("print('ok')", bundle.Files[0].Content);
    }

    [Fact]
    public void ExtractRequestedCodingPathsFiltersUnsafeAndOrdersLanguageMatch()
    {
        var paths = CodingFallbackPolicy.ExtractRequestedCodingPaths(
            "main.py 와 src/app.js 를 만들고 ../bad.py 는 무시",
            "javascript"
        );

        Assert.Equal(new[] { "src/app.js", "main.py" }, paths);
    }

    [Fact]
    public void SuggestFallbackEntryPathUsesRequestedPathOrDomainFolder()
    {
        Assert.Equal(
            "src/main.ts",
            CodingFallbackPolicy.SuggestFallbackEntryPath("typescript", "아무거나", new[] { "src/main.ts" })
        );

        Assert.Equal(
            "example.com/index.html",
            CodingFallbackPolicy.SuggestFallbackEntryPath("html", "example.com 클론 페이지")
        );
    }

    [Fact]
    public void ExtractExpectedConsoleOutputIgnoresPaths()
    {
        var output = CodingFallbackPolicy.ExtractExpectedConsoleOutput("main.py 파일에서 'ok'를 출력");

        Assert.Equal("ok", output);
    }

    [Fact]
    public void BuildRepairObjectivePromptIncludesFailureSpecificRules()
    {
        var prompt = CodingFallbackPolicy.BuildRepairObjectivePrompt(new CodingRepairObjectivePromptRequest(
            "pygame 게임 작성",
            "python",
            "/tmp/work",
            new CodeExecutionResult(
                "python",
                "/tmp/work",
                "main.py",
                "/tmp/work/.venv/bin/python main.py",
                1,
                "out",
                "Module not found: vite\n_tkinter missing\n[quality-gate]",
                "quality_failed"
            ),
            new[] { "/tmp/work/main.py" },
            new[] { "- language rule" },
            IsInteractivePythonObjective: true
        ));

        Assert.Contains("실패 명령: ./.venv/bin/python main.py", prompt);
        Assert.Contains("OMNI_HEADLESS_TEST=1", prompt);
        Assert.Contains("package.json 의 dependencies/devDependencies", prompt);
        Assert.Contains("[quality-gate] 미충족 항목", prompt);
        Assert.Contains("- main.py", prompt);
        Assert.Contains("- language rule", prompt);
    }
}
