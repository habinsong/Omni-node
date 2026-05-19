using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class CodingFallbackDecisionPolicyTests
{
    [Fact]
    public void ShouldPreferFileBundleFallbackWhenMultiplePathsRequested()
    {
        Assert.True(CodingFallbackDecisionPolicy.ShouldPreferFileBundleFallback(
            "코드 작성",
            new[] { "src/app.js", "src/index.html" },
            isExplicitSingleFile: false,
            projectPrefersMultiFile: false
        ));
    }

    [Fact]
    public void ShouldNotPreferFileBundleFallbackForExplicitSingleFile()
    {
        Assert.False(CodingFallbackDecisionPolicy.ShouldPreferFileBundleFallback(
            "파일 하나 작성",
            new[] { "main.py" },
            isExplicitSingleFile: true,
            projectPrefersMultiFile: true,
            profilePrefersBundle: true,
            frontendLike: true,
            gameLike: true,
            includeExplicitMultiFileTextSignals: true
        ));
    }

    [Fact]
    public void ShouldPreferFileBundleFallbackForProfileFrontendOrGameSignals()
    {
        Assert.True(CodingFallbackDecisionPolicy.ShouldPreferFileBundleFallback("웹앱 작성", null, false, false, frontendLike: true));
        Assert.True(CodingFallbackDecisionPolicy.ShouldPreferFileBundleFallback("게임 작성", null, false, false, gameLike: true));
        Assert.True(CodingFallbackDecisionPolicy.ShouldPreferFileBundleFallback("프로젝트 작성", null, false, false, profilePrefersBundle: true));
        Assert.True(CodingFallbackDecisionPolicy.ShouldPreferFileBundleFallback("프로젝트 작성", null, false, true));
    }

    [Fact]
    public void ShouldHonorExplicitMultiFileTextSignalsWhenEnabled()
    {
        Assert.True(CodingFallbackDecisionPolicy.ShouldPreferFileBundleFallback(
            "여러 파일로 작성",
            null,
            isExplicitSingleFile: false,
            projectPrefersMultiFile: false,
            includeExplicitMultiFileTextSignals: true
        ));
        Assert.False(CodingFallbackDecisionPolicy.ShouldPreferFileBundleFallback(
            "여러 파일로 작성",
            null,
            isExplicitSingleFile: false,
            projectPrefersMultiFile: false,
            includeExplicitMultiFileTextSignals: false
        ));
    }
}
