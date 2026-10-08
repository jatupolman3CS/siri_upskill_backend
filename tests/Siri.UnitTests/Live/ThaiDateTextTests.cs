using Siri.Modules.Live.Application;

namespace Siri.UnitTests.Live;

/// <summary>Unit tests for <see cref="ThaiDateText"/> (docs/contracts/P11-04-live-invites-ics-reminders.md §4.2): fixed UTC+7, Buddhist
/// year, no ICU / TimeZoneInfo.</summary>
public class ThaiDateTextTests
{
    private static DateTime Utc(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void Format_TheContractExample()
    {
        // 03:00–05:00 UTC is 10:00–12:00 in Thailand; 1 October 2026 is a Thursday, 2569 in the Buddhist era.
        var text = ThaiDateText.Format(Utc(2026, 10, 1, 3), Utc(2026, 10, 1, 5));

        Assert.Equal("พฤหัสบดีที่ 1 ตุลาคม 2569 เวลา 10:00–12:00 น. (เวลาประเทศไทย)", text);
    }

    [Fact]
    public void Format_AMidnightUtcBoundary_RollsTheThaiDateForward()
    {
        // 17:00 UTC on 1 Oct is exactly 00:00 on 2 Oct in Thailand.
        var text = ThaiDateText.Format(Utc(2026, 10, 1, 17), Utc(2026, 10, 1, 19));

        Assert.Equal("ศุกร์ที่ 2 ตุลาคม 2569 เวลา 00:00–02:00 น. (เวลาประเทศไทย)", text);
    }

    [Fact]
    public void Format_ASessionThatEndsExactlyAtThaiMidnight_ShowsBothDates()
    {
        var text = ThaiDateText.Format(Utc(2026, 10, 1, 16, 59), Utc(2026, 10, 1, 16, 59).AddMinutes(1));

        Assert.Equal("พฤหัสบดีที่ 1 ตุลาคม 2569 เวลา 23:59 น. – ศุกร์ที่ 2 ตุลาคม 2569 เวลา 00:00 น. (เวลาประเทศไทย)", text);
    }

    [Fact]
    public void Format_ASessionAcrossThaiMidnight_ShowsBothDates()
    {
        var text = ThaiDateText.Format(Utc(2026, 10, 1, 16, 30), Utc(2026, 10, 1, 18, 30));

        Assert.Equal("พฤหัสบดีที่ 1 ตุลาคม 2569 เวลา 23:30 น. – ศุกร์ที่ 2 ตุลาคม 2569 เวลา 01:30 น. (เวลาประเทศไทย)", text);
    }

    [Fact]
    public void Format_LeapDay_IsFebruary29_InBuddhistYear2571()
    {
        // 2028-02-28 17:30 UTC is 00:30 on 29 February in Thailand (a Tuesday).
        var text = ThaiDateText.Format(Utc(2028, 2, 28, 17, 30), Utc(2028, 2, 28, 19, 30));

        Assert.Equal("อังคารที่ 29 กุมภาพันธ์ 2571 เวลา 00:30–02:30 น. (เวลาประเทศไทย)", text);
    }

    [Fact]
    public void Format_AfterFebruary28InANonLeapYear_IsMarch1()
    {
        var text = ThaiDateText.Format(Utc(2027, 2, 28, 17, 0), Utc(2027, 2, 28, 18, 0));

        Assert.Equal("จันทร์ที่ 1 มีนาคม 2570 เวลา 00:00–01:00 น. (เวลาประเทศไทย)", text);
    }

    [Fact]
    public void Format_NewYearsEveUtc_IsAlreadyNewYearInThailand()
    {
        var text = ThaiDateText.Format(Utc(2026, 12, 31, 20), Utc(2026, 12, 31, 22));

        Assert.Equal("ศุกร์ที่ 1 มกราคม 2570 เวลา 03:00–05:00 น. (เวลาประเทศไทย)", text);
    }

    [Theory]
    [InlineData(2026, 10, 4, "อาทิตย์")]
    [InlineData(2026, 10, 5, "จันทร์")]
    [InlineData(2026, 10, 6, "อังคาร")]
    [InlineData(2026, 10, 7, "พุธ")]
    [InlineData(2026, 10, 8, "พฤหัสบดี")]
    [InlineData(2026, 10, 9, "ศุกร์")]
    [InlineData(2026, 10, 10, "เสาร์")]
    public void Format_UsesTheThaiWeekdayNames(int year, int month, int day, string expectedWeekday)
    {
        var text = ThaiDateText.Format(Utc(year, month, day, 3), Utc(year, month, day, 4));

        Assert.StartsWith(expectedWeekday + "ที่ ", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, "มกราคม")]
    [InlineData(2, "กุมภาพันธ์")]
    [InlineData(3, "มีนาคม")]
    [InlineData(4, "เมษายน")]
    [InlineData(5, "พฤษภาคม")]
    [InlineData(6, "มิถุนายน")]
    [InlineData(7, "กรกฎาคม")]
    [InlineData(8, "สิงหาคม")]
    [InlineData(9, "กันยายน")]
    [InlineData(10, "ตุลาคม")]
    [InlineData(11, "พฤศจิกายน")]
    [InlineData(12, "ธันวาคม")]
    public void Format_UsesTheThaiMonthNames(int month, string expectedMonth)
    {
        var text = ThaiDateText.Format(Utc(2026, month, 15, 3), Utc(2026, month, 15, 4));

        Assert.Contains($" 15 {expectedMonth} 2569 ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_DoesNotDependOnTheCurrentCulture()
    {
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ar-SA"); // uses Hijri digits/calendar by default
            var text = ThaiDateText.Format(Utc(2026, 10, 1, 3), Utc(2026, 10, 1, 5));

            Assert.Equal("พฤหัสบดีที่ 1 ตุลาคม 2569 เวลา 10:00–12:00 น. (เวลาประเทศไทย)", text);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Fact]
    public void FormatStart_AndTimeOfDay_UseThaiTime()
    {
        Assert.Equal("พฤหัสบดีที่ 1 ตุลาคม 2569 เวลา 10:00 น. (เวลาประเทศไทย)", ThaiDateText.FormatStart(Utc(2026, 10, 1, 3)));
        Assert.Equal("09:45 น.", ThaiDateText.FormatTimeOfDay(Utc(2026, 10, 1, 2, 45)));
    }

    [Fact]
    public void ToThailandTime_AddsSevenHours_ForUtcAndUnspecifiedKinds()
    {
        Assert.Equal(new DateTime(2026, 10, 1, 10, 0, 0), ThaiDateText.ToThailandTime(Utc(2026, 10, 1, 3)));
        Assert.Equal(new DateTime(2026, 10, 1, 10, 0, 0), ThaiDateText.ToThailandTime(new DateTime(2026, 10, 1, 3, 0, 0, DateTimeKind.Unspecified)));
    }
}
