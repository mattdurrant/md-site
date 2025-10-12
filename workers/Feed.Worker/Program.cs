using MattSite.Core;
using System.Text;
using static MattSite.Core.Html;

namespace Feed.Worker;

internal class Program
{
    // Optional fields ThumbUrl and StarsHtml let us enhance the row when available
    private record FeedItem(DateTime When, string Url, string LinkText, string Source, string? ThumbUrl = null, string? StarsHtml = null);

    private const string IntroHtml = """
<header>
  <h1><a href="https://www.mattdurrant.com">Matt Durrant</a></h1>
</header>
<a href="/fitness/">I run around</a>, <a href="/books/">I read bad books</a>, <a href="/photos/">I take pictures on my phone</a>.<br>
<a href="/albums/">Here are my favourite albums</a> <a href="/ebay/">(which I'm buying on vinyl).</a><br>
<a href="https://wa.link/nsysbp">Contact me on WhatsApp.</a><a href="mailto:matt@mattdurrant.com"> I largely ignore email</a> <a href="https://www.facebook.com/mattdurrant">and Facebook.</a>
<br>
""";

    static async Task<int> Main()
    {
        try
        {
            var outputBase = Environment.GetEnvironmentVariable("OUTPUT_DIR") ?? "out";
            var outDir = Path.Combine(outputBase);
            Directory.CreateDirectory(outDir);

            var wantStrava = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STRAVA_REFRESH_TOKEN"));
            var wantPhotos = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DROPBOX_REFRESH_TOKEN"));
            var wantBooks = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GOODREADS_RSS_URL"));
            var dropboxFolder = Environment.GetEnvironmentVariable("DROPBOX_PHOTOS_FOLDER") ?? "/Public/Photos";

            var items = new List<FeedItem>();
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            // Running (Strava)
            if (wantStrava)
            {
                try
                {
                    var access = await StravaAccessToken(http);
                    var acts = await StravaApi.GetRecentActivitiesAsync(http, access, perPage: 30, page: 1);
                    foreach (var a in acts.Take(10))
                    {
                        var when = StravaApi.ToUk(a.StartDateLocal);
                        var url = StravaApi.ActivityUrl(a.Id);
                        var name = string.IsNullOrWhiteSpace(a.Name) ? (a.SportType ?? "Activity") : a.Name;
                        items.Add(new FeedItem(when, url, $"Run: {Html.E(name)}", "run"));
                    }
                }
                catch (Exception ex) { Console.WriteLine($"Running skipped: {ex.Message}"); }
            }

            // Photos (Dropbox)
            if (wantPhotos)
            {
                try
                {
                    var access = await DropboxAccessToken(http);
                    var photos = new List<DropboxApi.DbxFile>();
                    await foreach (var f in DropboxApi.ListImagesWithLinksAsync(http, access, dropboxFolder))
                        photos.Add(f);

                    foreach (var p in photos.OrderByDescending(x => x.ClientModified).Take(10))
                    {
                        var name = string.IsNullOrWhiteSpace(p.Name) ? "Photo" : p.Name;
                        items.Add(new FeedItem(
                            When: p.ClientModified,
                            Url: p.LinkUrl,
                            LinkText: $"Photo: {Html.E(name)}",
                            Source: "photo",
                            ThumbUrl: p.RawUrl  // show thumbnail below the text line
                        ));
                    }
                }
                catch (Exception ex) { Console.WriteLine($"Photos skipped: {ex.Message}"); }
            }

            // Books (Goodreads)
            if (wantBooks)
            {
                try
                {
                    var rss = Environment.GetEnvironmentVariable("GOODREADS_RSS_URL");
                    if (!string.IsNullOrWhiteSpace(rss))
                    {
                        var books = await GoodreadsRss.FetchAsync(http, rss!);
                        foreach (var b in books
                            .OrderByDescending(x => x.UserReadAt ?? x.PubDate ?? DateTime.MinValue)
                            .Take(10))
                        {
                            var when = b.UserReadAt ?? b.PubDate ?? DateTime.MinValue;
                            var title = Html.E(b.Title);
                            var author = Html.E(b.Author);
                            var txt = string.IsNullOrWhiteSpace(author) ? $"Book: {title}" : $"Book: {title} – {author}";
                            var link = string.IsNullOrWhiteSpace(b.Link) ? "#" : b.Link!;
                            var stars = StarString(ParseRating(b.UserRating)); // ★★★★☆

                            items.Add(new FeedItem(
                                When: when,
                                Url: link,
                                LinkText: txt,
                                Source: "book",
                                ThumbUrl: null,
                                StarsHtml: $@"<span class=""stars"">{stars}</span>"
                            ));
                        }
                    }
                }
                catch (Exception ex) { Console.WriteLine($"Books skipped: {ex.Message}"); }
            }

            // Merge / sort / limit to 10
            var feed = items
                .Where(i => i.When > DateTime.MinValue)
                .OrderByDescending(i => i.When)
                .Take(10)
                .ToList();

            // Render as table (albumsTable styles) + minimal extra for thumbs/stars
            var body = new StringBuilder();
            body.Append(IntroHtml);
            body.Append(@"
<style>
/* small additions for feed rows */
.feedCell{padding:10px 8px}
.feedDate{color:#666;margin-right:.5rem;white-space:nowrap}
.feedThumb{margin-top:8px}
.feedThumb img{max-width:100%;height:200px;object-fit:cover;border-radius:6px;display:block}
.stars{margin-left:.5rem;white-space:nowrap}
</style>
<table class=""albumsTable""><tbody>");

            if (feed.Count == 0)
            {
                body.Append(@"<tr><td class=""feedCell"">No recent activity yet — check back soon.</td></tr>");
            }
            else
            {
                foreach (var f in feed)
                {
                    var date = UkDate.D(f.When);
                    body.Append($@"<tr><td class=""feedCell""><span class=""feedDate"">{date}:</span><a href=""{f.Url}"" target=""_blank"" rel=""noopener"">{f.LinkText}</a>");
                    if (!string.IsNullOrWhiteSpace(f.StarsHtml))
                        body.Append($" {f.StarsHtml}");
                    if (!string.IsNullOrWhiteSpace(f.ThumbUrl))
                        body.Append($@"<div class=""feedThumb""><a href=""{f.Url}"" target=""_blank"" rel=""noopener""><img src=""{f.ThumbUrl}"" alt=""""></a></div>");
                    body.Append("</td></tr>");
                }
            }

            body.Append("</tbody></table>");

            // No title/nav on homepage
            var pageHtml = Html.Page(title: "", body: body.ToString(), navHtml: "", showTitle: false);
            await File.WriteAllTextAsync(Path.Combine(outDir, "index.html"), pageHtml, Encoding.UTF8);

            Console.WriteLine($"Feed: wrote {Path.Combine(outDir, "index.html")} ({feed.Count} items).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("❌ " + ex);
            return 1;
        }
    }

    // Helpers
    private static int ParseRating(string userRating)
        => int.TryParse(userRating, out var r) ? Math.Clamp(r, 0, 5) : 0;

    private static string StarString(int rating)
    {
        var sb = new StringBuilder(5);
        for (int i = 0; i < 5; i++) sb.Append(i < rating ? '★' : '☆');
        return sb.ToString();
    }

    private static async Task<string> StravaAccessToken(HttpClient http)
    {
        var id = EnvReq("STRAVA_CLIENT_ID");
        var sec = EnvReq("STRAVA_CLIENT_SECRET");
        var rt = EnvReq("STRAVA_REFRESH_TOKEN");
        return await StravaApi.GetAccessTokenAsync(http, id, sec, rt);
    }

    private static async Task<string> DropboxAccessToken(HttpClient http)
    {
        var id = EnvReq("DROPBOX_APP_KEY");
        var sec = EnvReq("DROPBOX_APP_SECRET");
        var rt = EnvReq("DROPBOX_REFRESH_TOKEN");
        return await DropboxApi.GetAccessTokenAsync(http, id, sec, rt);
    }

    private static string EnvReq(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(v)) throw new InvalidOperationException($"Missing environment variable: {name}");
        return v!;
    }
}
