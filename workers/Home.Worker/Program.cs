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
                .Append(@"
                    <header>
                        <h1><a href=""http://www.mattdurrant.com"">Matt Durrant</a></h1>
                    </header>
                    <a href=""/fitness/"">I run around</a>, <a href=""/goodreads/"">I read bad books</a>, <a href=""/photos/"">I take pictures on my phone</a> <a href=""/phoneVideos"">and some videos.</a> <br>
                    <a href=""https://music.mattdurrant.com/"">I sometimes try to make electronic music</a> <br>
                    <a href=""/albums/"">Here are my favourite albums</a> <a href=""https://albums.mattdurrant.com/ebay/""> (which I'm buying on vinyl).</a> <br>
                    <a href=""https://wa.link/nsysbp"">Contact me on WhatsApp.</a><a href=""mailto:matt@mattdurrant.com""> I largely ignore email</a> <a href=""https://www.facebook.com/mattdurrant"">and Facebook.</a>

<ul>
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
