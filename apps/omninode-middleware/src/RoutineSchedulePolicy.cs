using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

internal static class RoutineSchedulePolicy
{
    public static TimeZoneInfo ResolveTimeZone(string? timezoneId)
    {
        if (!string.IsNullOrWhiteSpace(timezoneId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timezoneId.Trim());
            }
            catch
            {
            }
        }

        return TimeZoneInfo.Local;
    }

    public static string NormalizeScheduleKind(string? kind)
    {
        var normalized = (kind ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "weekly" => "weekly",
            "monthly" => "monthly",
            _ => "daily"
        };
    }

    public static int[] NormalizeWeekdays(IReadOnlyList<int>? weekdays)
    {
        if (weekdays == null || weekdays.Count == 0)
        {
            return Array.Empty<int>();
        }

        return weekdays
            .Select(static value => value == 7 ? 0 : value)
            .Where(static value => value >= 0 && value <= 6)
            .Distinct()
            .OrderBy(static value => value == 0 ? 7 : value)
            .ToArray();
    }

    public static int NormalizeRetryCount(int? retryCount)
    {
        return Math.Clamp(retryCount ?? 1, 0, 5);
    }

    public static int NormalizeRetryDelaySeconds(int? retryDelaySeconds)
    {
        return Math.Clamp(retryDelaySeconds ?? 15, 0, 300);
    }

    public static string NormalizeNotifyPolicy(string? notifyPolicy)
    {
        var normalized = (notifyPolicy ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "on_change" => "on_change",
            "error_only" => "error_only",
            "never" => "never",
            _ => "always"
        };
    }

    public static bool IsRetryableStatus(string? status)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "error" or "timeout";
    }

    public static string ComputeOutputFingerprint(string text)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes);
    }

    public static string FormatWeekday(int value)
    {
        return value switch
        {
            0 => "일",
            1 => "월",
            2 => "화",
            3 => "수",
            4 => "목",
            5 => "금",
            6 => "토",
            _ => "월"
        };
    }

    public static string FormatWeekdays(IReadOnlyList<int> weekdays)
    {
        if (weekdays == null || weekdays.Count == 0)
        {
            return "월";
        }

        return string.Join(", ", weekdays.Select(FormatWeekday));
    }

    public static string BuildScheduleDisplay(
        string kind,
        int hour,
        int minute,
        string timezoneId,
        int? dayOfMonth,
        IReadOnlyList<int> weekdays
    )
    {
        var suffix = string.Equals(timezoneId, TimeZoneInfo.Local.Id, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : $" ({timezoneId})";
        if (string.Equals(kind, "weekly", StringComparison.Ordinal))
        {
            return $"매주 {FormatWeekdays(weekdays)} {hour:D2}:{minute:D2}{suffix}";
        }

        if (string.Equals(kind, "monthly", StringComparison.Ordinal))
        {
            return $"매월 {Math.Clamp(dayOfMonth ?? 1, 1, 31)}일 {hour:D2}:{minute:D2}{suffix}";
        }

        return $"매일 {hour:D2}:{minute:D2}{suffix}";
    }

    public static RoutineScheduleConfig BuildDailyConfig(int hour, int minute, string timezoneId)
    {
        var resolvedTimezoneId = ResolveTimeZone(timezoneId).Id;
        return new RoutineScheduleConfig(
            "daily",
            hour,
            minute,
            BuildScheduleDisplay("daily", hour, minute, resolvedTimezoneId, null, Array.Empty<int>()),
            resolvedTimezoneId,
            $"{minute} {hour} * * *",
            null,
            Array.Empty<int>()
        );
    }

    public static bool TryParseTimeOfDay(string? rawTime, out int hour, out int minute, out string error)
    {
        hour = 8;
        minute = 0;
        error = string.Empty;
        var normalized = (rawTime ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return true;
        }

        var match = Regex.Match(normalized, @"^(?<hour>\d{1,2})\s*:\s*(?<minute>\d{1,2})$");
        if (!match.Success
            || !int.TryParse(match.Groups["hour"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out hour)
            || !int.TryParse(match.Groups["minute"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out minute)
            || hour < 0
            || hour > 23
            || minute < 0
            || minute > 59)
        {
            error = "시간 형식은 HH:MM 이어야 합니다.";
            return false;
        }

        return true;
    }

    public static int? ParseWeekdayToken(string token)
    {
        return (token ?? string.Empty).Trim() switch
        {
            "월" => 1,
            "화" => 2,
            "수" => 3,
            "목" => 4,
            "금" => 5,
            "토" => 6,
            "일" => 0,
            _ => null
        };
    }

    public static bool TryExtractNaturalTime(string text, out int hour, out int minute)
    {
        hour = 8;
        minute = 0;
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        var hhmm = Regex.Match(normalized, @"(?<!\d)(?<hour>\d{1,2})\s*:\s*(?<minute>\d{1,2})(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (hhmm.Success
            && int.TryParse(hhmm.Groups["hour"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hhmmHour)
            && int.TryParse(hhmm.Groups["minute"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hhmmMinute))
        {
            hour = Math.Clamp(hhmmHour, 0, 23);
            minute = Math.Clamp(hhmmMinute, 0, 59);
            return true;
        }

        var korean = Regex.Match(
            normalized,
            @"(?:(?<period>아침|오전|오후|저녁|밤|새벽)\s*)?(?<hour>\d{1,2})\s*시(?:\s*(?:(?<minute>\d{1,2})\s*분|(?<half>반)))?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );
        if (!korean.Success
            || !int.TryParse(korean.Groups["hour"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedHour))
        {
            return false;
        }

        hour = Math.Clamp(parsedHour, 0, 23);
        if (korean.Groups["half"].Success)
        {
            minute = 30;
        }
        else if (korean.Groups["minute"].Success
                 && int.TryParse(korean.Groups["minute"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedMinute))
        {
            minute = Math.Clamp(parsedMinute, 0, 59);
        }
        else
        {
            minute = 0;
        }

        var period = korean.Groups["period"].Value.Trim();
        if ((period == "오후" || period == "저녁" || period == "밤") && hour < 12)
        {
            hour += 12;
        }
        else if ((period == "오전" || period == "아침" || period == "새벽") && hour == 12)
        {
            hour = 0;
        }

        return true;
    }

    public static bool TryExtractMonthlyDay(string text, out int dayOfMonth)
    {
        dayOfMonth = 1;
        var match = Regex.Match(
            text ?? string.Empty,
            @"매(?:월|달)\s*(?<day>\d{1,2})\s*일(?:마다)?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );
        if (!match.Success
            || !int.TryParse(match.Groups["day"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        dayOfMonth = Math.Clamp(parsed, 1, 31);
        return true;
    }

    public static bool TryExtractWeekdays(string text, out int[] weekdays)
    {
        weekdays = Array.Empty<int>();
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        if (normalized.Contains("평일", StringComparison.Ordinal))
        {
            weekdays = new[] { 1, 2, 3, 4, 5 };
            return true;
        }

        if (normalized.Contains("주말", StringComparison.Ordinal))
        {
            weekdays = new[] { 6, 0 };
            return true;
        }

        var collected = Regex.Matches(
                normalized,
                @"(?<day>[월화수목금토일])요일(?:마다)?",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
            )
            .Cast<Match>()
            .Select(match => ParseWeekdayToken(match.Groups["day"].Value))
            .Where(static value => value.HasValue)
            .Select(static value => value!.Value)
            .ToArray();

        if (collected.Length == 0)
        {
            return false;
        }

        weekdays = NormalizeWeekdays(collected);
        return weekdays.Length > 0;
    }

    public static RoutineSchedule ParseDailySchedule(string request)
    {
        var text = (request ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new RoutineSchedule(8, 0, "매일 08:00");
        }

        var hhmm = Regex.Match(text, @"(\d{1,2})\s*:\s*(\d{1,2})", RegexOptions.IgnoreCase);
        if (hhmm.Success
            && int.TryParse(hhmm.Groups[1].Value, out var hour1)
            && int.TryParse(hhmm.Groups[2].Value, out var minute1))
        {
            hour1 = Math.Clamp(hour1, 0, 23);
            minute1 = Math.Clamp(minute1, 0, 59);
            return new RoutineSchedule(hour1, minute1, $"매일 {hour1:D2}:{minute1:D2}");
        }

        var match = Regex.Match(text, @"매일\s*(아침|오전|오후|저녁|밤)?\s*(\d{1,2})\s*시(?:\s*(\d{1,2})\s*분)?", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return new RoutineSchedule(8, 0, "매일 08:00");
        }

        var period = match.Groups[1].Value.Trim();
        _ = int.TryParse(match.Groups[2].Value, out var hour);
        _ = int.TryParse(match.Groups[3].Value, out var minute);
        hour = Math.Clamp(hour, 0, 23);
        minute = Math.Clamp(minute, 0, 59);
        if ((period == "오후" || period == "저녁" || period == "밤") && hour < 12)
        {
            hour += 12;
        }

        if ((period == "오전" || period == "아침") && hour == 12)
        {
            hour = 0;
        }

        return new RoutineSchedule(hour, minute, $"매일 {hour:D2}:{minute:D2}");
    }

    public static bool ContainsScheduleExpression(string? request)
    {
        var text = (request ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return false;
        }

        return Regex.IsMatch(text, @"매(?:일|주|월)|평일|주말", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
               || Regex.IsMatch(text, @"(?:월|화|수|목|금|토|일)요일(?:마다)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
               || Regex.IsMatch(text, @"\d{1,2}\s*:\s*\d{1,2}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
               || Regex.IsMatch(text, @"(?:아침|오전|오후|저녁|밤|새벽)?\s*\d{1,2}\s*시(?:\s*(?:\d{1,2}\s*분|반))?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static RoutineScheduleConfig ResolveConfigFromRequest(string request, string? timezoneId)
    {
        if (TryParseConfigFromRequest(request, timezoneId, out var parsed))
        {
            return parsed;
        }

        var fallback = ParseDailySchedule(request);
        return BuildDailyConfig(fallback.Hour, fallback.Minute, timezoneId ?? TimeZoneInfo.Local.Id);
    }

    public static bool TryParseConfigFromRequest(
        string? request,
        string? timezoneId,
        out RoutineScheduleConfig config
    )
    {
        config = BuildDailyConfig(8, 0, timezoneId ?? TimeZoneInfo.Local.Id);
        var text = (request ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return false;
        }

        var resolvedTimezoneId = ResolveTimeZone(timezoneId).Id;
        var hour = 8;
        var minute = 0;
        var hasExplicitTime = TryExtractNaturalTime(text, out hour, out minute);

        if (TryExtractMonthlyDay(text, out var dayOfMonth))
        {
            config = new RoutineScheduleConfig(
                "monthly",
                hour,
                minute,
                BuildScheduleDisplay("monthly", hour, minute, resolvedTimezoneId, dayOfMonth, Array.Empty<int>()),
                resolvedTimezoneId,
                $"{minute} {hour} {dayOfMonth} * *",
                dayOfMonth,
                Array.Empty<int>()
            );
            return true;
        }

        if (TryExtractWeekdays(text, out var weekdays))
        {
            config = new RoutineScheduleConfig(
                "weekly",
                hour,
                minute,
                BuildScheduleDisplay("weekly", hour, minute, resolvedTimezoneId, null, weekdays),
                resolvedTimezoneId,
                $"{minute} {hour} * * {string.Join(",", weekdays.Select(static x => x.ToString(CultureInfo.InvariantCulture)))}",
                null,
                weekdays
            );
            return true;
        }

        if (ContainsScheduleExpression(text) || hasExplicitTime)
        {
            config = BuildDailyConfig(hour, minute, resolvedTimezoneId);
            return true;
        }

        return false;
    }

    public static bool TryParseSupportedCronExpression(
        string? expr,
        out string kind,
        out int hour,
        out int minute,
        out int? dayOfMonth,
        out int[] weekdays,
        out string normalizedExpr,
        out string error
    )
    {
        kind = "daily";
        hour = 0;
        minute = 0;
        dayOfMonth = null;
        weekdays = Array.Empty<int>();
        normalizedExpr = "0 8 * * *";
        error = "지원되지 않는 cron 식입니다.";

        var tokens = (expr ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length != 5)
        {
            error = "cron 식은 5개 필드(m h dom mon dow)여야 합니다.";
            return false;
        }

        if (!int.TryParse(tokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out minute)
            || minute < 0 || minute > 59)
        {
            error = "cron 분은 0-59여야 합니다.";
            return false;
        }

        if (!int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out hour)
            || hour < 0 || hour > 23)
        {
            error = "cron 시는 0-23이어야 합니다.";
            return false;
        }

        if (!string.Equals(tokens[3], "*", StringComparison.Ordinal))
        {
            error = "cron month 필드는 현재 '*'만 지원합니다.";
            return false;
        }

        if (string.Equals(tokens[2], "*", StringComparison.Ordinal)
            && string.Equals(tokens[4], "*", StringComparison.Ordinal))
        {
            kind = "daily";
            normalizedExpr = $"{minute} {hour} * * *";
            return true;
        }

        if (string.Equals(tokens[2], "*", StringComparison.Ordinal))
        {
            var parsedWeekdays = new List<int>();
            foreach (var rawPart in tokens[4].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!int.TryParse(rawPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    error = "주간 cron 요일은 0-6 또는 7(일요일)만 지원합니다.";
                    return false;
                }

                if (parsed == 7)
                {
                    parsed = 0;
                }

                if (parsed < 0 || parsed > 6)
                {
                    error = "주간 cron 요일은 0-6 또는 7(일요일)만 지원합니다.";
                    return false;
                }

                parsedWeekdays.Add(parsed);
            }

            weekdays = NormalizeWeekdays(parsedWeekdays);
            if (weekdays.Length == 0)
            {
                error = "주간 cron 요일은 하나 이상 필요합니다.";
                return false;
            }

            kind = "weekly";
            normalizedExpr = $"{minute} {hour} * * {string.Join(",", weekdays.Select(static x => x.ToString(CultureInfo.InvariantCulture)))}";
            return true;
        }

        if (string.Equals(tokens[4], "*", StringComparison.Ordinal)
            && int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDayOfMonth)
            && parsedDayOfMonth >= 1
            && parsedDayOfMonth <= 31)
        {
            kind = "monthly";
            dayOfMonth = parsedDayOfMonth;
            normalizedExpr = $"{minute} {hour} {parsedDayOfMonth} * *";
            return true;
        }

        error = "지원되는 cron 형식은 daily/weekly/monthly 뿐입니다.";
        return false;
    }

    public static bool TryBuildConfig(
        string? scheduleKind,
        string? scheduleTime,
        IReadOnlyList<int>? weekdays,
        int? dayOfMonth,
        string? timezoneId,
        out RoutineScheduleConfig config,
        out string error
    )
    {
        config = BuildDailyConfig(8, 0, TimeZoneInfo.Local.Id);
        error = string.Empty;
        var kind = NormalizeScheduleKind(scheduleKind);
        if (!TryParseTimeOfDay(scheduleTime, out var hour, out var minute, out error))
        {
            return false;
        }

        string resolvedTimezoneId;
        try
        {
            resolvedTimezoneId = ResolveTimeZone(timezoneId).Id;
        }
        catch (Exception ex)
        {
            error = $"시간대가 올바르지 않습니다: {ex.Message}";
            return false;
        }

        if (string.Equals(kind, "weekly", StringComparison.Ordinal))
        {
            var normalizedWeekdays = NormalizeWeekdays(weekdays);
            if (normalizedWeekdays.Length == 0)
            {
                error = "주간 스케줄은 요일을 하나 이상 선택해야 합니다.";
                return false;
            }

            config = new RoutineScheduleConfig(
                kind,
                hour,
                minute,
                BuildScheduleDisplay(kind, hour, minute, resolvedTimezoneId, null, normalizedWeekdays),
                resolvedTimezoneId,
                $"{minute} {hour} * * {string.Join(",", normalizedWeekdays.Select(static x => x.ToString(CultureInfo.InvariantCulture)))}",
                null,
                normalizedWeekdays
            );
            return true;
        }

        if (string.Equals(kind, "monthly", StringComparison.Ordinal))
        {
            var normalizedDayOfMonth = dayOfMonth ?? 1;
            if (normalizedDayOfMonth < 1 || normalizedDayOfMonth > 31)
            {
                error = "월간 스케줄의 날짜는 1일부터 31일 사이여야 합니다.";
                return false;
            }

            config = new RoutineScheduleConfig(
                kind,
                hour,
                minute,
                BuildScheduleDisplay(kind, hour, minute, resolvedTimezoneId, normalizedDayOfMonth, Array.Empty<int>()),
                resolvedTimezoneId,
                $"{minute} {hour} {normalizedDayOfMonth} * *",
                normalizedDayOfMonth,
                Array.Empty<int>()
            );
            return true;
        }

        config = new RoutineScheduleConfig(
            "daily",
            hour,
            minute,
            BuildScheduleDisplay("daily", hour, minute, resolvedTimezoneId, null, Array.Empty<int>()),
            resolvedTimezoneId,
            $"{minute} {hour} * * *",
            null,
            Array.Empty<int>()
        );
        return true;
    }
}
