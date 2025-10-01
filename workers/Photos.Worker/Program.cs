using MattSite.Core;
using System.Text;

namespace Photos.Worker;

internal class Program
{
    static async Task<int> Main()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(100) };

            var appKey = Env("DROPBOX_APP_KEY");
            var appSecret = Env("DROPBOX_APP_SECRET");
            var refreshTok = Env("DROPBOX_REFRESH_TOKEN");
            var folderPath = Environment.GetEnvironmentVariable("DROPBOX_PHOTOS_FOLDER")
                             ?? Environment.GetEnvironmentVariable("DROPBOX_FOLDER")
                             ?? "/Public/Photos";
            var outputBase = Environment.GetEnvironmentVariable("OUTPUT_DIR") ?? "out";
            var outDir = Path.Combine(outputBase, "photos");
            Directory.CreateDirectory(outDir);

            Console.WriteLine($"Photos: listing {folderPath} from Dropbox…");
            var access = await DropboxApi.GetAccessTokenAsync(http, appKey, appSecret, refreshTok);

            var files = new List<DropboxApi.DbxFile>();
            await foreach (var f in DropboxApi.ListImagesWithLinksAsync(http, access, folderPath, s => Console.WriteLine("   " + s)))
                files.Add(f);

            // newest first
            var rows = files.OrderByDescending(f => f.ClientModified).ToList();

            var body = new StringBuilder();
            body.Append(@"
<style>
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(180px,1fr));gap:12px}
.card{border-radius:12px;overflow:hidden;box-shadow:0 1px 6px rgba(0,0,0,.08);background:#fff}
.thumb{width:100%;height:180px;object-fit:cover;display:block}
.cap{font-size:.9em;color:#555;padding:6px 8px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
</style>
<div class=""grid"">");

            foreach (var r in rows)
            {
                var alt = Html.E(r.Name);
                body.Append($@"<a class=""card"" href=""{r.LinkUrl}"" target=""_blank"" rel=""noopener"">");
                body.Append($@"<img class=""thumb"" src=""{r.RawUrl}"" alt=""{alt}"" loading=""lazy"">");
                body.Append($@"<div class=""cap"">{alt}</div>");
                body.Append("</a>");
            }

            body.Append("</div>");

            var html = Html.Page("Photos", body.ToString());
            await File.WriteAllTextAsync(Path.Combine(outDir, "index.html"), html, Encoding.UTF8);

            Console.WriteLine($"Photos: wrote {Path.Combine(outDir, "index.html")} ({rows.Count} images).");
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
        if (string.IsNullOrWhiteSpace(v)) throw new InvalidOperationException($"Missing environment variable: {name}");
        return v;
    }
}
