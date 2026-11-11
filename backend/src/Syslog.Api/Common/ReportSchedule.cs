using System.Globalization;

namespace Syslog.Api.Common;

public static class ReportSchedule
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");

    public static bool IsFrequency(string? value) => value is "daily" or "weekly" or "monthly";

    public static bool TryParseTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    public static DateTimeOffset NextAfter(string frequency, TimeOnly time, DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, Zone).DateTime;
        var date = DateOnly.FromDateTime(local);
        var start = frequency switch
        {
            "daily" => date,
            "weekly" => date.AddDays(-((int)date.DayOfWeek + 6) % 7),
            "monthly" => new DateOnly(date.Year, date.Month, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(frequency))
        };
        if (start.ToDateTime(time) <= local) start = Shift(start, frequency, 1);
        return ToUtc(start, time);
    }

    public static (DateTimeOffset From, DateTimeOffset To) PreviousPeriod(string frequency, DateTimeOffset next)
    {
        // 恢复后只补最近一个到期周期，避免长期停机后连续发送历史邮件。
        var upcoming = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(next, Zone).DateTime);
        var end = Shift(upcoming, frequency, -1);
        return (ToUtc(Shift(end, frequency, -1), TimeOnly.MinValue), ToUtc(end, TimeOnly.MinValue));
    }

    private static DateOnly Shift(DateOnly date, string frequency, int periods) => frequency switch
    {
        "daily" => date.AddDays(periods),
        "weekly" => date.AddDays(periods * 7),
        "monthly" => date.AddMonths(periods),
        _ => throw new ArgumentOutOfRangeException(nameof(frequency))
    };

    private static DateTimeOffset ToUtc(DateOnly date, TimeOnly time) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(time), Zone);
}
