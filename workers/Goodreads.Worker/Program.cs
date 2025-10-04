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

            var rss = Env("GOODREADS_RSS_URL"); // full RSS link to your shelf

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            Console.WriteLine("Goodreads: fetching RSS…");
            var books = await GoodreadsRss.FetchAsync(http, rss);

            // Order: most recently read first, fall back to RSS pubDate
            var ordered = books
                .OrderByDescending(b => b.UserReadAt ?? b.PubDate ?? DateTime.MinValue)
                .ToList();

            // Render a neat cover grid
            var body = new StringBuilder();
            body.Append(@"
<style>
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(160px,1fr));gap:14px}
.card{background:#fff;border-radius:12px;overflow:hidden;box-shadow:0 1px 6px rgba(0,0,0,.08)}
.cover{width:100%;height:240px;object-fit:cover;display:block;background:#f2f2f2}
.meta{padding:8px}
.title{font-weight:600;line-height:1.2;margin:0 0 4px 0}
.author,.extra{color:#666;font-size:.9em;margin:0}
</style>
<div class=""grid"">");

            foreach (var b in ordered)
            {
                var when = b.UserReadAt?.ToString("yyyy-MM-dd") ?? b.PubDate?.ToString("yyyy-MM-dd") ?? "";
                var rating = string.IsNullOrWhiteSpace(b.UserRating) || b.UserRating == "0"
                    ? ""
                    : $" — {b.UserRating}★";

                body.Append($@"
  <a class=""card"" href=""{b.Link}"" target=""_blank"" rel=""noopener"">
    <img class=""cover"" src=""{(!string.IsNullOrWhiteSpace(b.ImageUrl) ? b.ImageUrl : b.SmallImageUrl)}"" alt=""{Html.E(b.Title)}"" loading=""lazy"">
    <div class=""meta"">
      <p class=""title"">{Html.E(b.Title)}</p>
      <p class=""author"">{Html.E(b.Author)}</p>
      <p class=""extra"">{Html.E(when)}{Html.E(rating)}</p>
    </div>
  </a>");
            }

            body.Append("</div>");

            var html = Html.Page("Books — from Goodreads (read shelf)", body.ToString());
            await File.WriteAllTextAsync(Path.Combine(outDir, "index.html"), html, Encoding.UTF8);

            Console.WriteLine($"Goodreads: wrote {Path.Combine(outDir, "index.html")} ({ordered.Count} books).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("❌ " + ex.Message);
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
}
