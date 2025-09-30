using MattSite.Core;
using System.Text;

namespace Strava.Worker;

internal class Program
{
    static async Task<int> Main()
    {
        try
        {
            var outputBase = Environment.GetEnvironmentVariable("OUTPUT_DIR") ?? "out";
            var outDir = Path.Combine(outputBase, "strava");
            Directory.CreateDirectory(outDir);

            var clientId = Env("STRAVA_CLIENT_ID");
            var clientSecret = Env("STRAVA_CLIENT_SECRET");
            var refreshToken = Env("STRAVA_REFRESH_TOKEN");

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            Console.WriteLine("Strava: refreshing access token…");
            var access = await StravaApi.GetAccessTokenAsync(http, clientId, clientSecret, refreshToken);

            Console.WriteLine("Strava: fetching recent activities…");
            var acts = await StravaApi.GetRecentActivitiesAsync(http, access, perPage: 50, page: 1);

            // show the latest 20
            var top = acts
                .OrderByDescending(a => a.StartDateLocal)
                .Take(20)
                .ToList();

            var body = new StringBuilder();
            body.Append(@"<ul style=""list-style:none;padding:0;margin:0"">");
            foreach (var a in top)
            {
                var whenUk = StravaApi.ToUk(a.StartDateLocal);
                var dateStr = whenUk.ToString("ddd dd MMM yyyy HH:mm 'UK'");
                var km = a.Distance / 1000.0;
                string dur(int s) => $"{s / 3600:00}:{(s % 3600) / 60:00}:{s % 60:00}";

                body.Append(@"<li style=""padding:10px 0;border-bottom:1px solid #ddd"">");
                body.Append($@"<div><a href=""{StravaApi.ActivityUrl(a.Id)}"" target=""_blank"">{Html.E(a.Name)}</a></div>");
                body.Append($@"<div class=""meta"">{Html.E(a.SportType)} · {km:0.0} km · {dur(a.MovingTime)} · {dateStr}</div>");
                body.Append("</li>");
            }
            body.Append("</ul>");

            var html = Html.Page("Strava — Recent Activities", body.ToString());
            await File.WriteAllTextAsync(Path.Combine(outDir, "index.html"), html, Encoding.UTF8);

            Console.WriteLine($"Strava: wrote {Path.Combine(outDir, "index.html")} ({top.Count} items).");
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
