namespace OmniNode.Middleware;

internal sealed class CitationAccumulator
{
    private readonly Dictionary<string, SearchCitationReference> _byKey =
        new(StringComparer.OrdinalIgnoreCase);

    public int Count => _byKey.Count;

    public void Merge(IEnumerable<SearchCitationReference>? citations)
    {
        if (citations == null)
        {
            return;
        }

        foreach (var citation in citations)
        {
            Add(citation);
        }
    }

    public void Add(SearchCitationReference citation)
    {
        var key = GeminiCitationParser.BuildDedupKey(citation);
        if (key.Length == 0)
        {
            return;
        }

        _byKey[key] = citation;
    }

    public void MergeFromGeminiPayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return;
        }

        Merge(GeminiCitationParser.ExtractUrlContextCitations(payload));
        Merge(GeminiCitationParser.ExtractGroundingCitations(payload));
    }

    public SearchCitationReference[] ToArray()
    {
        if (_byKey.Count == 0)
        {
            return Array.Empty<SearchCitationReference>();
        }

        var result = new SearchCitationReference[_byKey.Count];
        var index = 0;
        foreach (var value in _byKey.Values)
        {
            result[index++] = value;
        }

        return result;
    }
}
