namespace OmniNode.Middleware;

internal static class MemoryNoteSelectionPolicy
{
    public static IReadOnlyList<string> NormalizeExplicitNames(IReadOnlyList<string>? names)
    {
        if (names == null || names.Count == 0)
        {
            return Array.Empty<string>();
        }

        return names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<string> MergeNames(IReadOnlyList<string> baseNames, IReadOnlyList<string>? requestNames)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in baseNames)
        {
            if (!string.IsNullOrWhiteSpace(item))
            {
                set.Add(item.Trim());
            }
        }

        if (requestNames != null)
        {
            foreach (var item in requestNames)
            {
                if (!string.IsNullOrWhiteSpace(item))
                {
                    set.Add(item.Trim());
                }
            }
        }

        return set.ToArray();
    }
}
