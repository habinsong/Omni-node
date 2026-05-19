using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class RoutineSchedulePolicyTests
{
    [Fact]
    public void ResolveTimeZoneFallsBackToLocalForBlankInput()
    {
        Assert.Equal(TimeZoneInfo.Local.Id, RoutineSchedulePolicy.ResolveTimeZone(null).Id);
        Assert.Equal(TimeZoneInfo.Local.Id, RoutineSchedulePolicy.ResolveTimeZone(string.Empty).Id);
        Assert.Equal(TimeZoneInfo.Local.Id, RoutineSchedulePolicy.ResolveTimeZone("garbage timezone").Id);
    }

    [Fact]
    public void ResolveTimeZoneRecognizesKnownId()
    {
        var resolved = RoutineSchedulePolicy.ResolveTimeZone("UTC");

        Assert.Equal("UTC", resolved.Id);
    }

    [Theory]
    [InlineData("weekly", "weekly")]
    [InlineData("MONTHLY", "monthly")]
    [InlineData("daily", "daily")]
    [InlineData("garbage", "daily")]
    [InlineData(null, "daily")]
    [InlineData("", "daily")]
    public void NormalizeScheduleKindFallsBackToDaily(string? input, string expected)
    {
        Assert.Equal(expected, RoutineSchedulePolicy.NormalizeScheduleKind(input));
    }

    [Fact]
    public void NormalizeWeekdaysDeduplicatesAndSortsWithSundayLast()
    {
        var normalized = RoutineSchedulePolicy.NormalizeWeekdays(new[] { 7, 1, 3, 1, 0, 8, 5 });

        Assert.Equal(new[] { 1, 3, 5, 0 }, normalized);
    }

    [Fact]
    public void NormalizeWeekdaysReturnsEmptyForNullOrEmpty()
    {
        Assert.Empty(RoutineSchedulePolicy.NormalizeWeekdays(null));
        Assert.Empty(RoutineSchedulePolicy.NormalizeWeekdays(Array.Empty<int>()));
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData(0, 0)]
    [InlineData(10, 5)]
    [InlineData(3, 3)]
    [InlineData(-1, 0)]
    public void NormalizeRetryCountClampsTo0to5(int? input, int expected)
    {
        Assert.Equal(expected, RoutineSchedulePolicy.NormalizeRetryCount(input));
    }

    [Theory]
    [InlineData(null, 15)]
    [InlineData(0, 0)]
    [InlineData(500, 300)]
    [InlineData(60, 60)]
    public void NormalizeRetryDelaySecondsClampsTo0to300(int? input, int expected)
    {
        Assert.Equal(expected, RoutineSchedulePolicy.NormalizeRetryDelaySeconds(input));
    }

    [Theory]
    [InlineData("on_change", "on_change")]
    [InlineData("ERROR_ONLY", "error_only")]
    [InlineData("never", "never")]
    [InlineData("always", "always")]
    [InlineData("unknown", "always")]
    public void NormalizeNotifyPolicyFallsBackToAlways(string input, string expected)
    {
        Assert.Equal(expected, RoutineSchedulePolicy.NormalizeNotifyPolicy(input));
    }

    [Theory]
    [InlineData("error", true)]
    [InlineData("TIMEOUT", true)]
    [InlineData("ok", false)]
    [InlineData("quality_failed", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsRetryableStatusOnlyAllowsErrorAndTimeout(string? input, bool expected)
    {
        Assert.Equal(expected, RoutineSchedulePolicy.IsRetryableStatus(input));
    }

    [Fact]
    public void ComputeOutputFingerprintReturnsHexAndEmptyForBlank()
    {
        Assert.Equal(string.Empty, RoutineSchedulePolicy.ComputeOutputFingerprint("   "));
        Assert.Equal(string.Empty, RoutineSchedulePolicy.ComputeOutputFingerprint(null!));

        var fingerprint = RoutineSchedulePolicy.ComputeOutputFingerprint("hello");
        Assert.Equal(64, fingerprint.Length);
        Assert.Matches("^[0-9A-F]+$", fingerprint);
    }

    [Theory]
    [InlineData(0, "일")]
    [InlineData(1, "월")]
    [InlineData(2, "화")]
    [InlineData(3, "수")]
    [InlineData(4, "목")]
    [InlineData(5, "금")]
    [InlineData(6, "토")]
    [InlineData(9, "월")]
    public void FormatWeekdayMapsKnownValues(int input, string expected)
    {
        Assert.Equal(expected, RoutineSchedulePolicy.FormatWeekday(input));
    }

    [Fact]
    public void FormatWeekdaysJoinsListAndDefaultsToMon()
    {
        Assert.Equal("월", RoutineSchedulePolicy.FormatWeekdays(Array.Empty<int>()));
        Assert.Equal("월", RoutineSchedulePolicy.FormatWeekdays(null!));
        Assert.Equal("월, 화, 수", RoutineSchedulePolicy.FormatWeekdays(new[] { 1, 2, 3 }));
    }

    [Fact]
    public void BuildScheduleDisplayUsesKindSpecificFormat()
    {
        var daily = RoutineSchedulePolicy.BuildScheduleDisplay("daily", 9, 30, TimeZoneInfo.Local.Id, null, Array.Empty<int>());
        var weekly = RoutineSchedulePolicy.BuildScheduleDisplay("weekly", 9, 30, TimeZoneInfo.Local.Id, null, new[] { 1, 3, 5 });
        var monthly = RoutineSchedulePolicy.BuildScheduleDisplay("monthly", 9, 30, TimeZoneInfo.Local.Id, 15, Array.Empty<int>());

        Assert.Equal("매일 09:30", daily);
        Assert.Equal("매주 월, 수, 금 09:30", weekly);
        Assert.Equal("매월 15일 09:30", monthly);
    }

    [Fact]
    public void BuildScheduleDisplayAppendsTimezoneSuffixWhenNotLocal()
    {
        var utc = RoutineSchedulePolicy.BuildScheduleDisplay("daily", 9, 30, "UTC", null, Array.Empty<int>());

        // Test only meaningful if Local isn't UTC.
        if (!string.Equals(TimeZoneInfo.Local.Id, "UTC", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Contains("(UTC)", utc);
        }
    }

    [Fact]
    public void BuildDailyConfigComposesCronExprAndDisplay()
    {
        var config = RoutineSchedulePolicy.BuildDailyConfig(8, 30, TimeZoneInfo.Local.Id);

        Assert.Equal("daily", config.Kind);
        Assert.Equal(8, config.Hour);
        Assert.Equal(30, config.Minute);
        Assert.Equal("30 8 * * *", config.CronExpr);
        Assert.Contains("매일 08:30", config.Display);
    }

    [Fact]
    public void TryParseTimeOfDayAcceptsHhMmAndBlank()
    {
        Assert.True(RoutineSchedulePolicy.TryParseTimeOfDay("", out var h1, out var m1, out _));
        Assert.Equal(8, h1);
        Assert.Equal(0, m1);

        Assert.True(RoutineSchedulePolicy.TryParseTimeOfDay("09:30", out var h2, out var m2, out _));
        Assert.Equal(9, h2);
        Assert.Equal(30, m2);

        Assert.False(RoutineSchedulePolicy.TryParseTimeOfDay("25:00", out _, out _, out var error1));
        Assert.Contains("HH:MM", error1);

        Assert.False(RoutineSchedulePolicy.TryParseTimeOfDay("garbage", out _, out _, out _));
    }

    [Theory]
    [InlineData("월", 1)]
    [InlineData("일", 0)]
    [InlineData("외", null)]
    [InlineData("", null)]
    public void ParseWeekdayTokenRecognizesKnownLetters(string token, int? expected)
    {
        Assert.Equal(expected, RoutineSchedulePolicy.ParseWeekdayToken(token));
    }

    [Theory]
    [InlineData("매일 9:30에 실행", 9, 30)]
    [InlineData("매일 오후 3시에 실행", 15, 0)]
    [InlineData("매일 오전 9시 반에 실행", 9, 30)]
    [InlineData("매일 오후 12시 0분", 12, 0)]
    [InlineData("매일 오전 12시", 0, 0)]
    [InlineData("매일 새벽 3시", 3, 0)]
    public void TryExtractNaturalTimeHandlesKoreanAndHhMmPatterns(string text, int expectedHour, int expectedMinute)
    {
        var ok = RoutineSchedulePolicy.TryExtractNaturalTime(text, out var hour, out var minute);

        Assert.True(ok);
        Assert.Equal(expectedHour, hour);
        Assert.Equal(expectedMinute, minute);
    }

    [Fact]
    public void TryExtractMonthlyDayParsesKoreanMonthDay()
    {
        Assert.True(RoutineSchedulePolicy.TryExtractMonthlyDay("매월 15일에 실행", out var day));
        Assert.Equal(15, day);

        Assert.False(RoutineSchedulePolicy.TryExtractMonthlyDay("매일 실행", out _));
    }

    [Fact]
    public void TryExtractWeekdaysRecognizesPyeongIlAndJuMal()
    {
        Assert.True(RoutineSchedulePolicy.TryExtractWeekdays("평일 9시", out var weekdays1));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, weekdays1);

        Assert.True(RoutineSchedulePolicy.TryExtractWeekdays("주말 9시", out var weekdays2));
        Assert.Equal(new[] { 6, 0 }, weekdays2);

        Assert.True(RoutineSchedulePolicy.TryExtractWeekdays("월요일, 수요일마다", out var weekdays3));
        Assert.Equal(new[] { 1, 3 }, weekdays3);

        Assert.False(RoutineSchedulePolicy.TryExtractWeekdays("매일", out _));
    }

    [Fact]
    public void ParseDailyScheduleReturnsDefaultsForBlankAndExtractsHhMm()
    {
        Assert.Equal("매일 08:00", RoutineSchedulePolicy.ParseDailySchedule(string.Empty).Display);
        Assert.Equal("매일 09:30", RoutineSchedulePolicy.ParseDailySchedule("매일 9:30").Display);

        var afternoon = RoutineSchedulePolicy.ParseDailySchedule("매일 오후 3시");
        Assert.Equal(15, afternoon.Hour);
        Assert.Equal(0, afternoon.Minute);
    }

    [Fact]
    public void TryBuildConfigComposesWeeklyMonthlyDaily()
    {
        Assert.True(RoutineSchedulePolicy.TryBuildConfig(
            "weekly",
            "10:00",
            new[] { 1, 3, 5 },
            null,
            TimeZoneInfo.Local.Id,
            out var weeklyConfig,
            out _
        ));
        Assert.Equal("weekly", weeklyConfig.Kind);
        Assert.Equal("0 10 * * 1,3,5", weeklyConfig.CronExpr);

        Assert.True(RoutineSchedulePolicy.TryBuildConfig(
            "monthly",
            "08:30",
            null,
            15,
            TimeZoneInfo.Local.Id,
            out var monthlyConfig,
            out _
        ));
        Assert.Equal("monthly", monthlyConfig.Kind);
        Assert.Equal("30 8 15 * *", monthlyConfig.CronExpr);

        Assert.True(RoutineSchedulePolicy.TryBuildConfig(
            null,
            null,
            null,
            null,
            null,
            out var dailyConfig,
            out _
        ));
        Assert.Equal("daily", dailyConfig.Kind);
        Assert.Equal("0 8 * * *", dailyConfig.CronExpr);
    }

    [Fact]
    public void TryBuildConfigRejectsWeeklyWithoutWeekdaysAndBadMonthlyDay()
    {
        Assert.False(RoutineSchedulePolicy.TryBuildConfig(
            "weekly",
            "10:00",
            Array.Empty<int>(),
            null,
            TimeZoneInfo.Local.Id,
            out _,
            out var weeklyError
        ));
        Assert.Contains("주간", weeklyError);

        Assert.False(RoutineSchedulePolicy.TryBuildConfig(
            "monthly",
            "10:00",
            null,
            32,
            TimeZoneInfo.Local.Id,
            out _,
            out var monthlyError
        ));
        Assert.Contains("31일", monthlyError);
    }

    [Fact]
    public void TryBuildConfigPropagatesBadTimeOfDayError()
    {
        Assert.False(RoutineSchedulePolicy.TryBuildConfig(
            "daily",
            "99:99",
            null,
            null,
            TimeZoneInfo.Local.Id,
            out _,
            out var error
        ));
        Assert.Contains("HH:MM", error);
    }
}
