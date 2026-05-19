using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class CitationAccumulatorTests
{
    [Fact]
    public void MergeDeduplicatesByUrlAndPreservesLatestEntry()
    {
        var accumulator = new CitationAccumulator();
        var first = new SearchCitationReference(
            "id-1",
            "First Title",
            "https://example.com/article",
            string.Empty,
            "URL_CONTEXT",
            "url_context"
        );
        var duplicate = new SearchCitationReference(
            "id-2",
            "Duplicate Title",
            "https://example.com/article",
            string.Empty,
            "URL_CONTEXT",
            "url_context"
        );
        var second = new SearchCitationReference(
            "id-3",
            "Second Title",
            "https://example.com/other",
            string.Empty,
            "GOOGLE_SEARCH",
            "google_search"
        );

        accumulator.Merge(new[] { first, duplicate, second });

        var result = accumulator.ToArray();
        Assert.Equal(2, result.Length);

        var byUrl = result.ToDictionary(item => item.Url, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Duplicate Title", byUrl["https://example.com/article"].Title);
        Assert.Equal("Second Title", byUrl["https://example.com/other"].Title);
    }

    [Fact]
    public void MergeFromGeminiPayloadCombinesUrlContextAndGroundingCitations()
    {
        var payload = """
            {
              "candidates": [
                {
                  "urlContextMetadata": {
                    "urlMetadata": [
                      { "retrievedUrl": "https://example.com/a" }
                    ]
                  },
                  "groundingMetadata": {
                    "groundingChunks": [
                      { "web": { "uri": "https://example.com/b", "title": "B" } }
                    ]
                  }
                }
              ]
            }
            """;

        var accumulator = new CitationAccumulator();
        accumulator.MergeFromGeminiPayload(payload);

        Assert.Equal(2, accumulator.Count);
        var urls = accumulator.ToArray().Select(item => item.Url).OrderBy(item => item).ToArray();
        Assert.Equal(new[] { "https://example.com/a", "https://example.com/b" }, urls);
    }

    [Fact]
    public void EmptyAccumulatorReturnsEmptyArrayReference()
    {
        var accumulator = new CitationAccumulator();

        var result = accumulator.ToArray();

        Assert.Empty(result);
        Assert.Same(Array.Empty<SearchCitationReference>(), result);
    }

    [Fact]
    public void MergeIgnoresNullEnumerableAndEmptyDedupKey()
    {
        var accumulator = new CitationAccumulator();
        accumulator.Merge(null);

        accumulator.Add(new SearchCitationReference(
            "id",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty
        ));

        Assert.Equal(0, accumulator.Count);
    }

    [Fact]
    public void MergeFromGeminiPayloadIgnoresBlankInput()
    {
        var accumulator = new CitationAccumulator();
        accumulator.MergeFromGeminiPayload(string.Empty);
        accumulator.MergeFromGeminiPayload("   ");
        accumulator.MergeFromGeminiPayload(null!);

        Assert.Equal(0, accumulator.Count);
    }
}
