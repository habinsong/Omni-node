using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class CodingQualityBriefPolicyTests
{
    [Fact]
    public void BuildIncludesLanguageRequestedFilesAndExpectedStdout()
    {
        var brief = CodingQualityBriefPolicy.Build(
            "main.py 파일에서 'ok'를 출력",
            "python",
            (_, _) => false,
            (_, _) => false
        );

        Assert.Contains("- language=python", brief);
        Assert.Contains("- required_files=main.py", brief);
        Assert.Contains("- expected_stdout=ok", brief);
        Assert.Contains("- acceptance=실행 가능", brief);
        Assert.Contains("- verification=마지막 액션", brief);
    }

    [Fact]
    public void BuildUsesDefaultEntryGuidanceWhenNoPathIsRequested()
    {
        var brief = CodingQualityBriefPolicy.Build(
            "간단한 CLI 작성",
            "python",
            (_, _) => false,
            (_, _) => false
        );

        Assert.Contains("요청에서 명시한 파일이 없으면 표준 엔트리 파일을 만들 것", brief);
    }

    [Fact]
    public void BuildUsesFrontendAcceptanceBeforeGameAcceptance()
    {
        var brief = CodingQualityBriefPolicy.Build(
            "웹 슈팅 게임 작성",
            "html",
            (_, _) => true,
            (_, _) => true
        );

        Assert.Contains("- ui_acceptance=첫 화면", brief);
        Assert.Contains("- file_boundary=index.html/styles.css/app.js", brief);
        Assert.DoesNotContain("- game_acceptance=", brief);
    }

    [Fact]
    public void BuildUsesGameAcceptanceWhenNotFrontend()
    {
        var brief = CodingQualityBriefPolicy.Build(
            "pygame 게임 작성",
            "python",
            (_, _) => false,
            (_, _) => true
        );

        Assert.Contains("- game_acceptance=입력 처리", brief);
        Assert.DoesNotContain("- ui_acceptance=", brief);
    }
}
