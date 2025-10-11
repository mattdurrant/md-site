using MattSite.Core;
using System.Text;

namespace Feed.Worker;

internal class Program
{
    private record FeedItem(DateTime When, string Html, string Source);

    private const string IntroHtml = """
        <header>
            <h1><a href="https://www.mattdurrant.com">Matt Durrant</a></h1>
        </header>
        <a href="https://www.mattdurrant.com/strava/">I run around</a>, <a href="https://www.mattdurrant.com/books/">I read bad books</a>, <a href="https://www.mattdurrant.com/photos/">I take pictures on my phone</a> <a href="/phoneVideos">and some videos.</a> <br>
        <a href="https://music.mattdurrant.com/">I sometimes try to make electronic music</a> <br>
        <a href="https://www.mattdurrant.com/albums/">Here are my favourite albums</a> <a href="https://www.mattdurrant.com/ebay/"> (which I'm buying on vinyl).</a> <br>
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

            // Which sources are enabled (based on env)
            var wantStrava = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STRAVA_REFRESH_TOKEN"));
            var wantPhotos = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DROPBOX_REFRESH_TOKEN"));
            var wantBooks = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GOODREADS_RSS_URL"));

            var dropboxFolder = Environment.GetEnvironmentVariable("DROPBOX_PHOTOS_FOLDER") ?? "/Public/Photos";

            var items = new List<FeedItem>();
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            // --- Strava (latest activities) ---
            if (wantStrava)
            {
                try
                {
                    var access = await StravaAccessToken(http);
                    var acts = await StravaApi.GetRecentActivitiesAsync(http, access, perPage: 30, page: 1);
                    foreach (var a in acts.Take(10))
                    {
                        var when = StravaApi.ToUk(a.StartDateLocal);
                        var km = a.Distance / 1000.0;
                        string dur(int s) => $"{s / 3600:0}:{(s % 3600) / 60:00}:{s % 60:00}";
                        var cardHtml =
$@"<div class=""card strava"">
  <div class=""hdr"">🏃 {Html.E(a.SportType)} — <a href=""{StravaApi.ActivityUrl(a.Id)}"" target=""_blank"">{Html.E(a.Name)}</a></div>
  <div class=""meta"">{km:0.0} km · {dur(a.MovingTime)} · {when:ddd dd MMM yyyy HH:mm}</div>
</div>";
                        items.Add(new FeedItem(when, cardHtml, "strava"));
                    }
                }
                catch (Exception ex) { Console.WriteLine($"Strava skipped: {ex.Message}"); }
            }

            // --- Photos (latest images by ClientModified) ---
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
                        var cardHtml =
$@"<div class=""card photos"">
  <div class=""hdr"">📷 New photo</div>
  <a href=""{p.LinkUrl}"" target=""_blank""><img class=""thumb"" src=""{p.RawUrl}"" alt=""{Html.E(p.Name)}"" loading=""lazy""></a>
  <div class=""meta"">{p.ClientModified:ddd dd MMM yyyy HH:mm}</div>
</div>";
                        items.Add(new FeedItem(p.ClientModified, cardHtml, "photos"));
                    }
                }
                catch (Exception ex) { Console.WriteLine($"Photos skipped: {ex.Message}"); }
            }

            // --- Goodreads (latest read) ---
            if (wantBooks)
            {
                try
                {
                    var rss = Env("GOODREADS_RSS_URL");
                    var books = await GoodreadsRss.FetchAsync(http, rss);
                    foreach (var b in books
                        .OrderByDescending(x => x.UserReadAt ?? x.PubDate ?? DateTime.MinValue)
                        .Take(10))
                    {
                        var when = b.UserReadAt ?? b.PubDate ?? DateTime.MinValue;
                        var rating = string.IsNullOrWhiteSpace(b.UserRating) || b.UserRating == "0" ? "" : $" — {b.UserRating}★";
                        var cover = !string.IsNullOrWhiteSpace(b.ImageUrl) ? b.ImageUrl : b.SmallImageUrl;
                        var cardHtml =
$@"<div class=""card books"">
  <div class=""hdr"">📚 Finished: <a href=""{b.Link}"" target=""_blank"">{Html.E(b.Title)}</a></div>
  <div class=""row"">
    <img class=""cover"" src=""{cover}"" alt=""{Html.E(b.Title)}"" loading=""lazy"">
    <div class=""info"">
      <div class=""author"">{Html.E(b.Author)}</div>
      <div class=""meta"">{when:ddd dd MMM yyyy}{Html.E(rating)}</div>
    </div>
  </div>
</div>";
                        items.Add(new FeedItem(when, cardHtml, "books"));
                    }
                }
                catch (Exception ex) { Console.WriteLine($"Goodreads skipped: {ex.Message}"); }
            }

            // --- Merge, sort, limit to 10 ---
            var feed = items
                .Where(i => i.When > DateTime.MinValue)
                .OrderByDescending(i => i.When)
                .Take(10)
                .ToList();

            var body = new StringBuilder();

            body.Append(IntroHtml);

            body.Append(@"
<style>
.feed{display:flex;flex-direction:column;gap:14px}
.card{background:#fff;border-radius:12px;box-shadow:0 1px 6px rgba(0,0,0,.08);padding:12px}
.card .hdr{font-weight:600;margin-bottom:6px}
.card .meta{color:#666;font-size:.9em}
.card .thumb{width:100%;height:280px;object-fit:cover;border-radius:8px}
.card .row{display:flex;gap:12px;align-items:flex-start}
.card .cover{width:120px;height:180px;object-fit:cover;border-radius:6px;background:#f0f0f0}
.card .info{flex:1}
.top-note{margin:0 0 12px;color:#555}
</style>
<div class=""feed"">");

            foreach (var f in feed) body.Append(f.Html);
            
            body.Append("</div>");

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
    private static string Env(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(v)) throw new InvalidOperationException($"Missing environment variable: {name}");
        return v!;
    }

    private static async Task<string> StravaAccessToken(HttpClient http)
    {
        var id = Env("STRAVA_CLIENT_ID");
        var sec = Env("STRAVA_CLIENT_SECRET");
        var rt = Env("STRAVA_REFRESH_TOKEN");
        return await StravaApi.GetAccessTokenAsync(http, id, sec, rt);
    }

    private static async Task<string> DropboxAccessToken(HttpClient http)
    {
        var id = Env("DROPBOX_APP_KEY");
        var sec = Env("DROPBOX_APP_SECRET");
        var rt = Env("DROPBOX_REFRESH_TOKEN");
        return await DropboxApi.GetAccessTokenAsync(http, id, sec, rt);
    }
}
