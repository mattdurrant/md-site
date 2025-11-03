using System.Globalization;
using System.Text;

namespace TheSequelCommittee.Worker
{
    public sealed record ChangeEvent(
        DateTime TimestampUtc,
        int CollectionId,
        string CollectionName,
        int MovieTmdbId,
        string Title,
        string Reason // e.g. "AddedToCollection"
    );

    public static class Changes
    {
        private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        public static string ChangesCsvPath(string outRoot) => Path.Combine(outRoot, "changes.csv");

        public static List<ChangeEvent> LoadChanges(string outRoot)
        {
            var path = ChangesCsvPath(outRoot);
            var list = new List<ChangeEvent>();
            if (!File.Exists(path)) return list;

            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = SplitCsv(line);
                if (parts.Length < 6) continue;
                if (!DateTime.TryParse(parts[0], null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var ts)) continue;
                if (!int.TryParse(parts[1], out var cid)) continue;
                if (!int.TryParse(parts[3], out var mid)) continue;

                list.Add(new ChangeEvent(
                    ts, cid, parts[2],
                    mid, parts[4],
                    parts[5]
                ));
            }
            return list;
        }

        public static void AppendChanges(string outRoot, IEnumerable<ChangeEvent> eventsToAppend)
        {
            var path = ChangesCsvPath(outRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            using var sw = new StreamWriter(path, append: true, Encoding.UTF8);
            foreach (var ev in eventsToAppend)
            {
                var line = string.Join(",",
                    CsvEscape(ev.TimestampUtc.ToString("o", CI)),
                    CsvEscape(ev.CollectionId.ToString(CI)),
                    CsvEscape(ev.CollectionName ?? string.Empty),
                    CsvEscape(ev.MovieTmdbId.ToString(CI)),
                    CsvEscape(ev.Title ?? string.Empty),
                    CsvEscape(ev.Reason ?? "AddedToCollection")
                );
                sw.WriteLine(line);
            }
        }

        // Small helpers
        private static string CsvEscape(string s)
        {
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private static string[] SplitCsv(string line)
        {
            var result = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (inQuotes)
                {
                    if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"')
                    { sb.Append('"'); i++; }
                    else if (ch == '"') { inQuotes = false; }
                    else sb.Append(ch);
                }
                else
                {
                    if (ch == ',') { result.Add(sb.ToString()); sb.Clear(); }
                    else if (ch == '"') inQuotes = true;
                    else sb.Append(ch);
                }
            }
            result.Add(sb.ToString());
            return result.ToArray();
        }

        public static string Html(string s) =>
            System.Net.WebUtility.HtmlEncode(s ?? string.Empty);
    }
}
