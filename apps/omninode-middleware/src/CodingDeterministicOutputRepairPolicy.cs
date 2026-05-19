using System.Text;

namespace OmniNode.Middleware;

internal static class CodingDeterministicOutputRepairPolicy
{
    public static bool ShouldTrySingleFileOutputRepair(
        string languageHint,
        IReadOnlyList<string> requestedPaths,
        string expectedOutput
    )
    {
        if (requestedPaths.Count != 1 || string.IsNullOrWhiteSpace(expectedOutput))
        {
            return false;
        }

        var normalizedLanguage = CodingLanguagePolicy.NormalizeLanguageForCode(languageHint);
        if (normalizedLanguage == "auto")
        {
            normalizedLanguage = CodingLanguagePolicy.GuessLanguageFromPath(requestedPaths[0], normalizedLanguage);
        }

        return string.Equals(normalizedLanguage, "python", StringComparison.OrdinalIgnoreCase);
    }

    public static string BuildPythonPrintCode(string expectedOutput)
    {
        return $"print({BuildPythonStringLiteral(expectedOutput)})\n";
    }

    public static string BuildPythonStringLiteral(string value)
    {
        var builder = new StringBuilder();
        builder.Append('"');
        foreach (var ch in value ?? string.Empty)
        {
            switch (ch)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (char.IsControl(ch))
                    {
                        builder.Append($"\\u{(int)ch:x4}");
                    }
                    else
                    {
                        builder.Append(ch);
                    }
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }
}
