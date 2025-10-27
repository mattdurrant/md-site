using System.Diagnostics;
using System.Globalization;

namespace TheSequelCommittee;

public static class Utils
{
    public static readonly CultureInfo CI = CultureInfo.InvariantCulture;

    public static void Heartbeat(int page, int itemIdx, int totalPages, Stopwatch total, Stopwatch lap, ref int lastDetails, int detailsCalls)
    {
        var lapDetails = detailsCalls - lastDetails;
        var lapSec = Math.Max(0.1, lap.Elapsed.TotalSeconds);
        var rps = lapDetails / lapSec;
        lastDetails = detailsCalls;
        lap.Restart();

        double pct = totalPages > 0 ? Math.Min(1.0, (page - 1 + (itemIdx / 20.0)) / totalPages) : 0.0;
        TimeSpan eta = pct > 0.001 ? TimeSpan.FromSeconds(total.Elapsed.TotalSeconds * (1 - pct) / pct) : TimeSpan.Zero;

        Console.WriteLine($"  [♥] Details: {detailsCalls} | Page {page}/{totalPages} item {itemIdx} | {rps:0.0} req/s | Elapsed {total.Elapsed:mm\\:ss} | ETA ~{eta:mm\\:ss}");
    }

    public static DateTime? ParseDate(string? ymd)
    {
        if (string.IsNullOrWhiteSpace(ymd)) return null;
        if (DateTime.TryParse(ymd, out var d)) return d;
        return null;
    }

    public static string CsvEscape(string s)
    {
        if (s is null) return "";
        if (s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
            return $"\"{s.Replace("\"", "\"\"")}\"";
        return s;
    }

    /// <summary>
    /// Returns true if the given ISO-ish date string is in the future (strictly > today UTC).
    /// Null/empty dates are treated as future (so they are excluded when excluding future films).
    /// </summary>
    public static bool IsFutureRelease(string? ymd)
    {
        var d = ParseDate(ymd);
        if (!d.HasValue) return true; // unknown date -> treat as not yet released
        return d.Value.Date > DateTime.UtcNow.Date;
    }
}
