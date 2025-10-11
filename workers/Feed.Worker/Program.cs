using MattSite.Core;
using System.Text;
using static MattSite.Core.Html;

namespace Feed.Worker;

internal class Program
{
    private record FeedItem(DateTime When, string Url, string LinkText, string Source);

    private const string IntroHtml = """
<header>
    <h1><a href="http://www.mattdurrant.com">Matt Durrant</a></h1>
</header>
<a href="/runaround">I run around</a>, <a href="/bookReviews">I read bad books</a>, <a href="/phonePics">I take pictures on my phone</a> <a href="/phoneVideos">and some videos.</a> <br>
<a href="https://music.mattdurrant.com/">I sometimes try to make electronic music</a> <br>
<a href="https://albums.mattdurrant.com/">Here are my favourite albums</a> <a href="https://albums.mattdurrant.com/ebay/"> (which I'm buying on vinyl).</a> <br>
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
                        items.Add(new FeedItem(p.ClientModified, p.LinkUrl, $"Photo: {Html.E(name)}", "photo"));
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
                            items.Add(new FeedItem(when, link, txt, "book"));
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

            // Render as a table using the albums table style
            var body = new StringBuilder();
            body.Append(IntroHtml);
            body.Append(@"
<table class=""albumsTable""><tbody>");
            if (feed.Count == 0)
            {
                body.Append(@"<tr><td>No recent activity yet — check back soon.</td></tr>");
            }
            else
            {
                foreach (var f in feed)
                {
                    var date = UkDate.D(f.When);
                    body.Append($@"<tr><td>{date}: <a href=""{f.Url}"" target=""_blank"" rel=""noopener"">{f.LinkText}</a></td></tr>");
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
