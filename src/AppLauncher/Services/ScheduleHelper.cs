using System.Globalization;
using AppLauncher.Models;
using Cronos;

namespace AppLauncher.Services;

/// <summary>
/// Okunabilir zamanlama tanımlarını cron ifadesine çevirir.
/// Örnekler:
///   Her gün 22:00               -> "0 22 * * *"
///   Her ayın 28'i 09:00          -> "0 9 28 * *"
///   Her ayın son perşembesi 22:00 -> "0 22 * * 4L"
///   Her ayın 2. salısı 08:30      -> "30 8 * * 2#2"
/// </summary>
public static class ScheduleHelper
{
    private static readonly string[] DayNames =
        { "Pazar", "Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi" };

    public static string? ToCron(ScheduleDefinition? s)
    {
        if (s is null || s.Type == ScheduleType.None) return null;

        if (s.Type == ScheduleType.Cron)
        {
            if (string.IsNullOrWhiteSpace(s.Cron)) throw new FormatException("Cron ifadesi boş olamaz.");
            return s.Cron.Trim();
        }

        var (h, m) = ParseTime(s.Time);

        return s.Type switch
        {
            ScheduleType.Daily => $"{m} {h} * * *",

            ScheduleType.Weekly => s.DaysOfWeek.Count == 0
                ? throw new FormatException("Haftalık zamanlama için en az bir gün seçilmeli.")
                : $"{m} {h} * * {string.Join(",", s.DaysOfWeek.Distinct().OrderBy(d => d).Select(d => (int)d))}",

            ScheduleType.Monthly => s.DayOfMonth is < 1 or > 31
                ? throw new FormatException("Ayın günü 1 ile 31 arasında olmalı.")
                : $"{m} {h} {s.DayOfMonth} * *",

            ScheduleType.MonthlyLastDay => $"{m} {h} L * *",

            ScheduleType.MonthlyWeekday => $"{m} {h} * * {(int)s.DayOfWeek}{WeekSuffix(s.Week)}",

            _ => throw new FormatException("Bilinmeyen zamanlama tipi.")
        };
    }

    public static CronExpression? GetExpression(ScheduleDefinition? s)
    {
        var cron = ToCron(s);
        if (cron is null) return null;
        var fieldCount = cron.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var format = fieldCount == 6 ? CronFormat.IncludeSeconds : CronFormat.Standard;
        try
        {
            return CronExpression.Parse(cron, format);
        }
        catch (CronFormatException ex)
        {
            throw new FormatException($"Geçersiz cron ifadesi '{cron}': {ex.Message}");
        }
    }

    /// <summary>Tanım geçerliyse null, değilse hata mesajı döner.</summary>
    public static string? Validate(ScheduleDefinition? s)
    {
        try { GetExpression(s); return null; }
        catch (FormatException ex) { return ex.Message; }
    }

    public static List<DateTimeOffset> NextOccurrences(CronExpression expr, TimeZoneInfo tz, DateTimeOffset from, int count)
    {
        var list = new List<DateTimeOffset>();
        var cursor = from;
        for (var i = 0; i < count; i++)
        {
            var next = expr.GetNextOccurrence(cursor, tz);
            if (next is null) break;
            list.Add(next.Value);
            cursor = next.Value;
        }
        return list;
    }

    public static string Describe(ScheduleDefinition? s)
    {
        if (s is null || s.Type == ScheduleType.None) return "Zamanlama yok (elle çalıştırılır)";
        try
        {
            var time = s.Type == ScheduleType.Cron ? "" : " " + NormalizeTime(s.Time);
            return s.Type switch
            {
                ScheduleType.Daily => $"Her gün{time}",
                ScheduleType.Weekly => $"Her hafta {string.Join(", ", s.DaysOfWeek.Distinct().OrderBy(d => d).Select(d => DayNames[(int)d]))}{time}",
                ScheduleType.Monthly => $"Her ayın {s.DayOfMonth}. günü{time}",
                ScheduleType.MonthlyLastDay => $"Her ayın son günü{time}",
                ScheduleType.MonthlyWeekday => $"Her ayın {WeekText(s.Week)} {DayNames[(int)s.DayOfWeek]} günü{time}",
                ScheduleType.Cron => $"Cron: {s.Cron}",
                _ => "?"
            };
        }
        catch
        {
            return "Geçersiz zamanlama";
        }
    }

    private static (int h, int m) ParseTime(string? time)
    {
        if (!TimeOnly.TryParseExact(time?.Trim(), new[] { "HH:mm", "H:mm" }, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var t))
            throw new FormatException($"Saat 'SS:dd' biçiminde olmalı (örn. 22:00). Girilen: '{time}'");
        return (t.Hour, t.Minute);
    }

    private static string NormalizeTime(string time)
    {
        var (h, m) = ParseTime(time);
        return $"{h:00}:{m:00}";
    }

    private static string WeekSuffix(string? week)
    {
        var w = (week ?? "").Trim();
        if (w.Equals("Last", StringComparison.OrdinalIgnoreCase) || w.Equals("L", StringComparison.OrdinalIgnoreCase)
            || w.Equals("son", StringComparison.OrdinalIgnoreCase))
            return "L";
        if (int.TryParse(w, out var n) && n is >= 1 and <= 5) return $"#{n}";
        throw new FormatException("Hafta değeri 1-5 arası bir sayı ya da 'Last' olmalı.");
    }

    private static string WeekText(string? week) =>
        WeekSuffix(week) == "L" ? "son" : $"{week!.Trim()}.";
}
