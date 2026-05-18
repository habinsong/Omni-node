using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class GeminiCitationParserTests
{
    [Fact]
    public void ExtractUrlContextCitationsBuildsHostTitleAndStripsDuplicates()
    {
        var json = """
            {
              "candidates": [
                {
                  "urlContextMetadata": {
                    "urlMetadata": [
                      {
                        "retrievedUrl": "https://www.example.com/article",
                        "urlRetrievalStatus": "URL_RETRIEVAL_STATUS_SUCCESS"
                      },
                      {
                        "retrievedUrl": "https://www.example.com/article"
                      },
                      {
                        "retrievedUrl": "https://docs.python.org/3/library/json.html"
                      }
                    ]
                  }
                }
              ]
            }
        """;

        var citations = GeminiCitationParser.ExtractUrlContextCitations(json);

        Assert.Equal(2, citations.Length);
        Assert.Equal("urlctx-1", citations[0].CitationId);
        Assert.Equal("example.com", citations[0].Title);
        Assert.Equal("https://www.example.com/article", citations[0].Url);
        Assert.Equal("URL_RETRIEVAL_STATUS_SUCCESS", citations[0].Snippet);
        Assert.Equal("url_context", citations[0].SourceType);

        Assert.Equal("urlctx-2", citations[1].CitationId);
        Assert.Equal("docs.python.org", citations[1].Title);
        Assert.Equal("URL_CONTEXT", citations[1].Snippet);
    }

    [Fact]
    public void ExtractUrlContextCitationsReturnsEmptyWhenMetadataMissing()
    {
        var citations = GeminiCitationParser.ExtractUrlContextCitations("{\"candidates\":[{}]}");

        Assert.Empty(citations);
    }

    [Fact]
    public void ExtractUrlContextCitationsReturnsEmptyForCorruptJson()
    {
        Assert.Empty(GeminiCitationParser.ExtractUrlContextCitations("{broken"));
    }

    [Fact]
    public void ExtractGroundingCitationsPrefersTitleOverHostFallback()
    {
        var json = """
            {
              "candidates": [
                {
                  "groundingMetadata": {
                    "groundingChunks": [
                      {
                        "web": {
                          "uri": "https://news.example.com/story",
                          "title": "Breaking story"
                        }
                      },
                      {
                        "web": {
                          "uri": "https://www.weather.example.com/forecast"
                        }
                      }
                    ]
                  }
                }
              ]
            }
        """;

        var citations = GeminiCitationParser.ExtractGroundingCitations(json);

        Assert.Equal(2, citations.Length);
        Assert.Equal("gsearch-1", citations[0].CitationId);
        Assert.Equal("Breaking story", citations[0].Title);
        Assert.Equal("GOOGLE_SEARCH", citations[0].Snippet);
        Assert.Equal("google_search", citations[0].SourceType);

        Assert.Equal("gsearch-2", citations[1].CitationId);
        Assert.Equal("weather.example.com", citations[1].Title);
    }

    [Fact]
    public void ExtractGroundingCitationsSkipsChunksWithoutWebUri()
    {
        var json = """
            {
              "candidates": [
                {
                  "groundingMetadata": {
                    "groundingChunks": [
                      { "web": { "title": "no uri" } },
                      { "web": { "uri": "https://valid.example.com" } }
                    ]
                  }
                }
              ]
            }
        """;

        var citations = GeminiCitationParser.ExtractGroundingCitations(json);

        Assert.Single(citations);
        Assert.Equal("https://valid.example.com", citations[0].Url);
    }

    [Fact]
    public void BuildDedupKeyPrefersUrlOverTitle()
    {
        var citation = new SearchCitationReference(
            "c1",
            "Title",
            "https://example.com/x",
            string.Empty,
            string.Empty,
            "url_context"
        );

        Assert.Equal("https://example.com/x", GeminiCitationParser.BuildDedupKey(citation));
    }

    [Fact]
    public void BuildDedupKeyFallsBackToTitleWhenUrlMissing()
    {
        var citation = new SearchCitationReference(
            "c1",
            "Title only",
            string.Empty,
            string.Empty,
            string.Empty,
            "url_context"
        );

        Assert.Equal("Title only", GeminiCitationParser.BuildDedupKey(citation));
    }
}
