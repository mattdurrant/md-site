using MattSite.Core;
using System.Text;

namespace Home.Worker;

internal class Program
{
    static async Task<int> Main()
    {
        try
        {
            var outputBase = Environment.GetEnvironmentVariable("OUTPUT_DIR") ?? "out";
            var outDir = Path.Combine(outputBase);
            Directory.CreateDirectory(outDir);

            var body = new StringBuilder()
                .Append("<p>Welcome! Quick links:</p>")
                .Append(@"<ul>
                    <li><a href=""/strava/"">Strava — Recent Activities</a></li>
                    <li><a href=""/photos/"">Photos — Dropbox Gallery</a></li>
                    <li><a href=""/albums/"">Favourite Albums</a></li>
                    <li><a href=""/ebay/"">Vinyl Deals (eBay)</a></li>
                </ul>")
                .ToString();

            var html = Html.Page("md-site — index", body);
            await File.WriteAllTextAsync(Path.Combine(outDir, "index.html"), html, Encoding.UTF8);

            Console.WriteLine($"Home: wrote {Path.Combine(outDir, "index.html")}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("❌ " + ex.Message);
            return 1;
        }
    }
}
