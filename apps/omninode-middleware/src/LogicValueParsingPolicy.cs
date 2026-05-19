using System.Globalization;

namespace OmniNode.Middleware;

internal static class LogicValueParsingPolicy
{
    public static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    public static IReadOnlyList<string>? ParseCsvValues(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var values = raw
            .Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return values.Length == 0 ? null : values;
    }

    public static IReadOnlyList<string>? ParseMultilineValues(string? raw)
    {
        return ParseCsvValues(raw);
    }

    public static int ParsePositiveInt(string? raw, int fallbackValue, int maxValue)
    {
        if (!int.TryParse((raw ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return fallbackValue;
        }

        return Math.Clamp(value, 0, maxValue);
    }

    public static int? ParseOptionalInt(string? raw, int maxValue)
    {
        var normalized = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (!int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return Math.Clamp(value, 0, maxValue);
    }

    public static double ParseDouble(string? raw, double fallbackValue)
    {
        if (!double.TryParse((raw ?? string.Empty).Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value))
        {
            return fallbackValue;
        }

        return value;
    }

    public static bool ParseBool(string? raw, bool fallbackValue)
    {
        var normalized = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return fallbackValue;
        }

        return normalized.Equals("1", StringComparison.Ordinal)
               || normalized.Equals("true", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("yes", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("y", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    public static bool? ParseOptionalBool(string? raw)
    {
        var normalized = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return ParseBool(normalized, false);
    }

    public static int CompareNumbers(string left, string right)
    {
        if (!double.TryParse(left, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var leftValue))
        {
            leftValue = 0;
        }

        if (!double.TryParse(right, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var rightValue))
        {
            rightValue = 0;
        }

        return leftValue.CompareTo(rightValue);
    }

    public static bool EvaluateCondition(string left, string? op, string? rightValue)
    {
        var normalizedOperator = LogicGraphValidationPolicy.NormalizeOperator(op);
        var right = rightValue ?? string.Empty;
        return normalizedOperator switch
        {
            "equals" => string.Equals(left, right, StringComparison.Ordinal),
            "not_equals" => !string.Equals(left, right, StringComparison.Ordinal),
            "contains" => left.Contains(right, StringComparison.OrdinalIgnoreCase),
            "not_contains" => !left.Contains(right, StringComparison.OrdinalIgnoreCase),
            "starts_with" => left.StartsWith(right, StringComparison.OrdinalIgnoreCase),
            "ends_with" => left.EndsWith(right, StringComparison.OrdinalIgnoreCase),
            "gt" => CompareNumbers(left, right) > 0,
            "gte" => CompareNumbers(left, right) >= 0,
            "lt" => CompareNumbers(left, right) < 0,
            "lte" => CompareNumbers(left, right) <= 0,
            "is_truthy" => ParseBool(left, false),
            "is_falsy" => !ParseBool(left, false),
            _ => false
        };
    }
}
