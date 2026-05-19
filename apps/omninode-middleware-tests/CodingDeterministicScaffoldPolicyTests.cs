using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class CodingDeterministicScaffoldPolicyTests
{
    [Fact]
    public void TryGenerateUiCloneScaffoldUsesDomainFolderAndThreeFiles()
    {
        var ok = CodingDeterministicScaffoldPolicy.TryGenerateUiCloneScaffold(
            "example.com 랜딩 페이지 UI 클론 html/css/js로 만들어줘",
            out var files
        );

        Assert.True(ok);
        Assert.Equal(new[] { "example.com/index.html", "example.com/styles.css", "example.com/script.js" }, files.Select(file => file.Path));
        Assert.Contains("<title>example.com UI Clone</title>", files[0].Content, StringComparison.Ordinal);
        Assert.Contains(".topbar", files[1].Content, StringComparison.Ordinal);
        Assert.Contains("UI clone rendered.", files[2].Content, StringComparison.Ordinal);
    }

    [Fact]
    public void TryGenerateUiCloneScaffoldFallsBackToDefaultFolder()
    {
        var ok = CodingDeterministicScaffoldPolicy.TryGenerateUiCloneScaffold(
            "간단한 html 페이지 레이아웃 클론 만들어줘",
            out var files
        );

        Assert.True(ok);
        Assert.Equal("web-clone/index.html", files[0].Path);
    }

    [Fact]
    public void TryGenerateUiCloneScaffoldRejectsGameRequests()
    {
        var ok = CodingDeterministicScaffoldPolicy.TryGenerateUiCloneScaffold(
            "html canvas 슈팅 게임 페이지 만들어줘",
            out var files
        );

        Assert.False(ok);
        Assert.Empty(files);
    }

    [Fact]
    public void TryGenerateUiCloneScaffoldRejectsNonWebExplicitLanguage()
    {
        var ok = CodingDeterministicScaffoldPolicy.TryGenerateUiCloneScaffold(
            "python으로 랜딩 페이지 UI 클론 만들어줘",
            out var files
        );

        Assert.False(ok);
        Assert.Empty(files);
    }

    [Fact]
    public void TryGenerateWebShooterScaffoldCreatesPlayableSingleHtmlFile()
    {
        var ok = CodingDeterministicScaffoldPolicy.TryGenerateWebShooterScaffold(
            "html canvas 웹 비행기 슈팅 게임 만들어줘",
            "auto",
            out var files
        );

        Assert.True(ok);
        var file = Assert.Single(files);
        Assert.Equal("index.html", file.Path);
        Assert.Contains("<title>Sky Patrol</title>", file.Content, StringComparison.Ordinal);
        Assert.Contains("<canvas id=\"game\"", file.Content, StringComparison.Ordinal);
        Assert.Contains("function resetGame()", file.Content, StringComparison.Ordinal);
        Assert.Contains("requestAnimationFrame(loop)", file.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void TryGenerateWebShooterScaffoldRejectsNonWebLanguageHint()
    {
        var ok = CodingDeterministicScaffoldPolicy.TryGenerateWebShooterScaffold(
            "웹 비행기 슈팅 게임 만들어줘",
            "python",
            out var files
        );

        Assert.False(ok);
        Assert.Empty(files);
    }

    [Fact]
    public void TryGenerateWebShooterScaffoldRequiresGameSignal()
    {
        var ok = CodingDeterministicScaffoldPolicy.TryGenerateWebShooterScaffold(
            "html canvas shooter landing page 만들어줘",
            "html",
            out var files
        );

        Assert.False(ok);
        Assert.Empty(files);
    }
}
