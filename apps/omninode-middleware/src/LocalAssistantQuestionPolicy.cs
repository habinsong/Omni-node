using System.Text;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

internal static class LocalAssistantQuestionPolicy
{
    public static bool LooksLikeSkillInventoryQuestion(string normalized, string compact)
    {
        var hasSkillKeyword = ContainsAny(normalized, "스킬", "skill", "skills", "skill.md")
                              || compact.Contains("스킬", StringComparison.Ordinal)
                              || compact.Contains("skill", StringComparison.Ordinal);
        if (!hasSkillKeyword)
        {
            return false;
        }

        var inventoryMarker = @"(뭐|무엇|무슨|어떤|어떠한|어떻|종류|목록|리스트|보여|알려|available|list)";
        var skillToken = @"(스킬|skill|skills|skill\.md)";
        var particle = @"(을|를|이|가|은|는|에|의|에는|들|들이|들은|들을)?";

        if (Regex.IsMatch(normalized, $@"(?i){skillToken}\s*{particle}\s*{inventoryMarker}"))
        {
            return true;
        }
        if (Regex.IsMatch(normalized, $@"(?i){inventoryMarker}\s*{skillToken}"))
        {
            return true;
        }
        if (Regex.IsMatch(normalized, $@"(?i){skillToken}\s*{particle}\s*(있|가지고|사용\s*가능)"))
        {
            return true;
        }

        var compactPatterns = new[]
        {
            "스킬뭐", "스킬어떤", "스킬어떠", "스킬어떻", "스킬무슨", "스킬무엇",
            "스킬목록", "스킬리스트", "스킬종류", "스킬보여", "스킬알려",
            "스킬있", "스킬가지고", "스킬사용가능",
            "어떤스킬", "어떠한스킬", "어떻스킬", "무슨스킬", "무엇스킬",
            "skill뭐", "skill어떤", "skill목록", "skill리스트",
        };
        foreach (var pattern in compactPatterns)
        {
            if (compact.Contains(pattern, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static bool LooksLikeLimitationQuestion(string normalized, string compact)
    {
        if (!AsksAssistantSubject(normalized))
        {
            return false;
        }

        return ContainsAny(
            normalized,
            "할 수 없는",
            "할수 없는",
            "못 하는",
            "못하는",
            "못 해",
            "못해",
            "한계",
            "제한",
            "limitations",
            "cannot")
            || compact.Contains("할수없는", StringComparison.Ordinal)
            || compact.Contains("할수없", StringComparison.Ordinal);
    }

    public static bool LooksLikeCapabilityQuestion(string normalized, string compact)
    {
        if (!AsksAssistantSubject(normalized))
        {
            return false;
        }

        return ContainsAny(
            normalized,
            "할 수",
            "할수",
            "뭘 할",
            "뭐 할",
            "무엇을 할",
            "기능",
            "도움",
            "가능",
            "능력",
            "capability",
            "capabilities")
            || compact.Contains("할수있는", StringComparison.Ordinal)
            || compact.Contains("할수있", StringComparison.Ordinal);
    }

    public static bool LooksLikeIdentityQuestion(string normalized, string compact)
    {
        return ContainsAny(
            normalized,
            "너는 누구",
            "넌 누구",
            "너 누구",
            "너가 누구",
            "네가 누구",
            "니가 누구",
            "당신은 누구",
            "당신 누구",
            "정체",
            "자기소개",
            "who are you")
            || compact.Contains("너는누구", StringComparison.Ordinal)
            || compact.Contains("넌누구", StringComparison.Ordinal)
            || compact.Contains("너누구", StringComparison.Ordinal)
            || compact.Contains("너뭐야", StringComparison.Ordinal)
            || compact.Contains("넌뭐야", StringComparison.Ordinal);
    }

    public static int IndexOfSkillNameWithBoundary(string text, string skillName)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(skillName))
        {
            return -1;
        }

        var startSearch = 0;
        while (startSearch <= text.Length - skillName.Length)
        {
            var idx = text.IndexOf(skillName, startSearch, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return -1;
            }

            var leftOk = idx == 0 || !IsSkillNameBoundaryInside(text[idx - 1]);
            var rightIdx = idx + skillName.Length;
            var rightOk = rightIdx >= text.Length || !IsSkillNameBoundaryInside(text[rightIdx]);
            if (leftOk && rightOk)
            {
                return idx;
            }

            startSearch = idx + 1;
        }

        return -1;
    }

    public static bool IsSkillNameBoundaryInside(char c)
    {
        if (c == '-' || c == '_')
        {
            return true;
        }

        return (c >= 'a' && c <= 'z')
               || (c >= 'A' && c <= 'Z')
               || (c >= '0' && c <= '9');
    }

    public static string TrimAssistantInfoText(string text, int maxChars)
    {
        var normalized = Regex.Replace((text ?? string.Empty).Trim(), @"\s+", " ");
        if (normalized.Length <= maxChars)
        {
            return string.IsNullOrWhiteSpace(normalized) ? "설명이 없습니다." : normalized;
        }

        return normalized[..maxChars].TrimEnd() + "...";
    }

    public static string TrimToUtf8ByteCount(string value, int maxBytes)
    {
        if (string.IsNullOrEmpty(value) || maxBytes <= 0)
        {
            return string.Empty;
        }

        if (Encoding.UTF8.GetByteCount(value) <= maxBytes)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        var used = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var bytes = rune.Utf8SequenceLength;
            if (used + bytes > maxBytes)
            {
                break;
            }

            builder.Append(rune);
            used += bytes;
        }

        return builder.ToString();
    }

    private static bool AsksAssistantSubject(string normalized)
    {
        return ContainsAny(
            normalized,
            "너",
            "넌",
            "네가",
            "니가",
            "당신",
            "어시스턴트",
            "ai",
            "봇",
            "omni",
            "옴니");
    }

    private static bool ContainsAny(string text, params string[] patterns)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var pattern in patterns)
        {
            if (!string.IsNullOrWhiteSpace(pattern)
                && text.Contains(pattern, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
