using MattSite.Core;
using System.Text;
using static MattSite.Core.Html;

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

            // FetchAsync should now be resilient (UA + retries + empty list on 403/429)
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
.notice{padding:.75rem 1rem;background:#fff7e6;border:1px solid #ffe2a8;border-radius:8px;margin:1rem 0;color:#553}
</style>");

            if (ordered.Count == 0)
            {
                // Non-fatal: still generate the page so the overall site build succeeds.
                body.AppendLine(@"<div class=""notice"">
<strong>Goodreads is temporarily unavailable</strong><br/>
The RSS feed returned a blocked/failed response from the build runner. This page will update automatically next time the feed is accessible.
</div>");
            }

            body.AppendLine(@"<ul class=""list"">");

            foreach (var b in ordered)
            {
                var when = b.UserReadAt ?? b.PubDate ?? DateTime.MinValue;
                var date = UkDate.D(when);
                var title = Html.E(b.Title);
                var author = Html.E(b.Author);
                var link = string.IsNullOrWhiteSpace(b.Link) ? "#" : b.Link!;
                var stars = StarString(ParseRating(b.UserRating));

                body.AppendLine($@"
<li>
  <span class=""date"">{date}:</span>
  <a href=""{link}"" target=""_blank"" rel=""noopener"">{title}</a>
  {(string.IsNullOrWhiteSpace(author) ? "" : $" – {author}")}
  <span class=""stars"">{stars}</span>
</li>");
            }

            body.AppendLine("</ul>");

            var html = Html.Page("Books", body.ToString(), navHtml: Html.BackHomeNav(), showTitle: true);

            var outPath = Path.Combine(outDir, "index.html");
            await File.WriteAllTextAsync(outPath, html, Encoding.UTF8);

            Console.WriteLine($"Goodreads: wrote {outPath} ({ordered.Count} books).");

            // Important: succeed even if Goodreads was blocked (empty list)
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("❌ " + ex);
            return 1;
        }
    }

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
        var sb = new StringBuilder(5);
        for (int i = 0; i < 5; i++) sb.Append(i < rating ? '★' : '☆');
        return sb.ToString();
    }
}
