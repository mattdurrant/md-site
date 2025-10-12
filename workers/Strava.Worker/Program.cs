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
            var outDir = Path.Combine(outputBase, "fitness");
            Directory.CreateDirectory(outDir);

            var clientId = Env("STRAVA_CLIENT_ID");
            var clientSecret = Env("STRAVA_CLIENT_SECRET");
            var refreshToken = Env("STRAVA_REFRESH_TOKEN");

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            Console.WriteLine("Strava: obtaining access token…");
            var accessToken = await StravaApi.GetAccessTokenAsync(http, clientId, clientSecret, refreshToken);

            Console.WriteLine("Strava: fetching recent activities…");
            // pull a decent chunk; you can tweak if needed
            var activities = await StravaApi.GetRecentActivitiesAsync(http, accessToken, perPage: 50, page: 1);

            // --- Render ---
            var body = new StringBuilder();
            body.Append(@"
<style>
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(280px,1fr));gap:14px}
.card{background:#fff;border-radius:12px;box-shadow:0 1px 6px rgba(0,0,0,.08);padding:12px}
.hdr{font-weight:600;margin-bottom:6px}
.meta{color:#666;font-size:.92em}
.badge{display:inline-block;padding:2px 6px;border-radius:999px;background:#f3f4f6;margin-left:6px;font-size:.82em}
</style>
<div class=""grid"">");

            foreach (var a in activities)
            {
                var whenLocal = StravaApi.ToUk(a.StartDateLocal);
                var km = a.Distance / 1000.0;
                string dur(int s) => $"{s / 3600:0}:{(s % 3600) / 60:00}:{s % 60:00}";

                // Simple pace if it's a run: minutes per km
                string pace = "";
                if (a.SportType?.Equals("Run", StringComparison.OrdinalIgnoreCase) == true && a.MovingTime > 0 && km > 0)
                {
                    var secPerKm = a.MovingTime / km;
                    var m = (int)(secPerKm / 60);
                    var s = (int)(secPerKm % 60);
                    pace = $" — {m}:{s:00}/km";
                }

                var url = StravaApi.ActivityUrl(a.Id);
                var name = string.IsNullOrWhiteSpace(a.Name) ? a.SportType ?? "Activity" : a.Name;

                body.Append($@"
  <div class=""card"">
    <div class=""hdr"">🏃 <a href=""{url}"" target=""_blank"" rel=""noopener"">{Html.E(name)}</a>
      <span class=""badge"">{Html.E(a.SportType ?? "Activity")}</span>
    </div>
    <div class=""meta"">{km:0.0} km · {dur(a.MovingTime)}{pace} · {whenLocal:ddd dd MMM yyyy HH:mm}</div>
  </div>");
            }

            body.Append("</div>");

            var html = Html.Page("Running", body.ToString());
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
