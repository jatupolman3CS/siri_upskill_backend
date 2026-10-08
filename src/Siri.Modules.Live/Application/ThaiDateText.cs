using System.Globalization;

namespace Siri.Modules.Live.Application;

/// <summary>
/// Thai long-form date/time text for e-mails (docs/contracts/P11-04-live-invites-ics-reminders.md §4.2), e.g.
/// <c>พฤหัสบดีที่ 1 ตุลาคม 2569 เวลา 10:00–12:00 น. (เวลาประเทศไทย)</c>. Pure and static: it does not touch
/// <c>CultureInfo</c>, ICU or <c>TimeZoneInfo</c> (neither is reliable inside a slim container), so the output is identical
/// everywhere. Thailand is a fixed UTC+7 with no daylight saving, so the conversion is a constant offset; the year is the
/// Buddhist-era year (Gregorian + 543).
/// </summary>
public static class ThaiDateText
{
    /// <summary>Thailand's fixed offset from UTC.</summary>
    public static readonly TimeSpan ThailandOffset = TimeSpan.FromHours(7);

    private const int BuddhistEraOffset = 543;

    private static readonly string[] DayNames =
    [
        "อาทิตย์", "จันทร์", "อังคาร", "พุธ", "พฤหัสบดี", "ศุกร์", "เสาร์",
    ];

    private static readonly string[] MonthNames =
    [
        "มกราคม", "กุมภาพันธ์", "มีนาคม", "เมษายน", "พฤษภาคม", "มิถุนายน",
        "กรกฎาคม", "สิงหาคม", "กันยายน", "ตุลาคม", "พฤศจิกายน", "ธันวาคม",
    ];

    /// <summary>
    /// The full phrase for a session. Within one Thai calendar day:
    /// <c>{weekday}ที่ {d} {month} {BE year} เวลา HH:mm–HH:mm น. (เวลาประเทศไทย)</c>; across midnight (Thai time) both ends
    /// carry their own date: <c>… เวลา HH:mm น. – {weekday}ที่ … เวลา HH:mm น. (เวลาประเทศไทย)</c>.
    /// </summary>
    public static string Format(DateTime startsUtc, DateTime endsUtc)
    {
        var start = ToThailandTime(startsUtc);
        var end = ToThailandTime(endsUtc);

        if (start.Date == end.Date)
        {
            return $"{FormatDate(start)} เวลา {FormatClock(start)}–{FormatClock(end)} น. (เวลาประเทศไทย)";
        }

        return $"{FormatDate(start)} เวลา {FormatClock(start)} น. – {FormatDate(end)} เวลา {FormatClock(end)} น. (เวลาประเทศไทย)";
    }

    /// <summary>Only the start moment — <c>{weekday}ที่ {d} {month} {BE year} เวลา HH:mm น. (เวลาประเทศไทย)</c> — for places
    /// that have no meaningful end (a reminder headline).</summary>
    public static string FormatStart(DateTime startsUtc)
    {
        var start = ToThailandTime(startsUtc);
        return $"{FormatDate(start)} เวลา {FormatClock(start)} น. (เวลาประเทศไทย)";
    }

    /// <summary>Only the Thai wall-clock time of day, <c>HH:mm น.</c> (for "the room opens at ...").</summary>
    public static string FormatTimeOfDay(DateTime utc) => $"{FormatClock(ToThailandTime(utc))} น.";

    /// <summary>The Thai wall-clock time of a UTC instant. A value of unspecified kind is taken to already be UTC (what the
    /// database returns); a local value is converted first.</summary>
    public static DateTime ToThailandTime(DateTime utcOrLocal)
    {
        var utc = utcOrLocal.Kind == DateTimeKind.Local ? utcOrLocal.ToUniversalTime() : utcOrLocal;
        return DateTime.SpecifyKind(utc, DateTimeKind.Unspecified).Add(ThailandOffset);
    }

    /// <summary><c>{weekday}ที่ {d} {month} {BE year}</c> for a value already converted to Thai time.</summary>
    private static string FormatDate(DateTime thaiTime) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{DayNames[(int)thaiTime.DayOfWeek]}ที่ {thaiTime.Day} {MonthNames[thaiTime.Month - 1]} {thaiTime.Year + BuddhistEraOffset}");

    private static string FormatClock(DateTime thaiTime) =>
        thaiTime.ToString("HH':'mm", CultureInfo.InvariantCulture);
}
