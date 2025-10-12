using MattSite.Core;
using System.Text;

namespace Goodreads.Worker;

internal class Program
{
    static async Task<int> Main()
    {
        try
        {
            var outputBase = Environment.GetEnvironmentVariable("OUTPUT_DIR") ?? "out";
            var outDir = Path.Combine(outputBase, "books");
            Directory.CreateDirectory(outDir);

            var rss = Env("GOODREADS_RSS_URL"); // full RSS URL to your shelf

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            Console.WriteLine("Goodreads: fetching RSS…");
            var books = await GoodreadsRss.FetchAsync(http, rss);

            // Most recently read first (fall back to RSS pubDate)
            var ordered = books
                .OrderByDescending(b => b.UserReadAt ?? b.PubDate ?? DateTime.MinValue)
                .ToList();

            var body = new StringBuilder();
            body.AppendLine(@"<style>
.list{display:block;margin:0;padding:0;list-style:none}
.list li{padding:.35rem 0;border-bottom:1px solid #eee}
.date{color:#666;margin-right:.5rem}
.title{font-weight:600}
.author{color:#444}
.stars{margin-left:.5rem;white-space:nowrap}
</style>");

            body.AppendLine(@"<ul class=""list"">");

            foreach (var b in ordered)
            {
                var date = (b.UserReadAt ?? b.PubDate)?.ToString("yyyy-MM-dd") ?? "";
                var title = Html.E(b.Title);
                var author = Html.E(b.Author);
                var stars = StarString(ParseRating(b.UserRating));

                // If you want the title clickable, swap <span class="title"> for <a class="title" href="b.Link" ...>
                body.AppendLine($@"  <li>
    <span class=""date"">{date}:</span>
    <span class=""title"">{title}</span> – <span class=""author"">{author}</span>
    <span class=""stars"">{stars}</span>
  </li>");
            }

            body.AppendLine("</ul>");

            var html = Html.Page("Books", body.ToString(), navHtml: Html.BackHomeNav(), showTitle: true);

            await File.WriteAllTextAsync(Path.Combine(outDir, "index.html"), html, Encoding.UTF8);

            Console.WriteLine($"Goodreads: wrote {Path.Combine(outDir, "index.html")} ({ordered.Count} books).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("❌ " + ex);
            return 1;
        }
    }

    // Helpers
    private static string Env(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(v))
            throw new InvalidOperationException($"Missing environment variable: {name}");
        return v;
    }

    private static int ParseRating(string userRating)
        => int.TryParse(userRating, out var r) ? Math.Clamp(r, 0, 5) : 0;

    private static string StarString(int rating)
    {
        // ★ = filled, ☆ = empty — same visual style as albums
        var sb = new StringBuilder(5);
        for (int i = 0; i < 5; i++) sb.Append(i < rating ? '★' : '☆');
        return sb.ToString();
    }
}
