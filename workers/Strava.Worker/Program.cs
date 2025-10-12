using MattSite.Core;
using System.Text;
using static MattSite.Core.Html;

namespace Fitness.Worker;

internal class Program
{
    static async Task<int> Main()
    {
        try
        {
            var outputBase = Environment.GetEnvironmentVariable("OUTPUT_DIR") ?? "out";
            var outDir = Path.Combine(outputBase, "fitness");
            Directory.CreateDirectory(outDir);

            var clientId = Env("STRAVA_CLIENT_ID");
            var clientSecret = Env("STRAVA_CLIENT_SECRET");
            var refreshToken = Env("STRAVA_REFRESH_TOKEN");

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            Console.WriteLine("Fitness: obtaining Strava token…");
            var accessToken = await StravaApi.GetAccessTokenAsync(http, clientId, clientSecret, refreshToken);

            Console.WriteLine("Fitness: fetching recent Strava activities…");
            var activities = await StravaApi.GetRecentActivitiesAsync(http, accessToken, perPage: 50, page: 1);

            // ---- Render: simple two-line list per activity ----
            var body = new StringBuilder();
            body.Append(@"
<style>
.fitlist{list-style:none;margin:0;padding:0}
.fititem{padding:10px 0;border-bottom:1px solid #eee}
.fitdate{color:#666;margin-right:.5rem;white-space:nowrap}
.fitname a{text-decoration:none}
.fitmeta{color:#555;font-size:.92em;margin-top:4px}
</style>
<ul class=""fitlist"">");

            foreach (var a in activities)
            {
                var whenLocal = StravaApi.ToUk(a.StartDateLocal);
                var date = UkDate.D(whenLocal);
                var km = a.Distance / 1000.0;

                static string Dur(int s) => $"{s / 3600:0}:{(s % 3600) / 60:00}:{s % 60:00}";

                string pace = "";
                if ((a.SportType ?? "").Equals("Run", StringComparison.OrdinalIgnoreCase) && a.MovingTime > 0 && km > 0)
                {
                    var spk = a.MovingTime / km; // seconds per km
                    var m = (int)(spk / 60);
                    var s = (int)(spk % 60);
                    pace = $" · {m}:{s:00}/km";
                }

                var url = StravaApi.ActivityUrl(a.Id);
                var name = string.IsNullOrWhiteSpace(a.Name) ? a.SportType ?? "Activity" : a.Name;

                body.Append($@"
  <li class=""fititem"">
    <div class=""fitname""><span class=""fitdate"">{date}:</span><a href=""{url}"" target=""_blank"" rel=""noopener"">{Html.E(name)}</a></div>
    <div class=""fitmeta"">{km:0.0} km · {Dur(a.MovingTime)}{pace}</div>
  </li>");
            }

            body.Append("</ul>");

            var html = Html.Page("Fitness", body.ToString(), Html.BackHomeNav(), showTitle: true);
            await File.WriteAllTextAsync(Path.Combine(outDir, "index.html"), html, Encoding.UTF8);

            Console.WriteLine($"Fitness: wrote {Path.Combine(outDir, "index.html")} ({activities.Count} activities).");
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
        return v!;
    }
}
