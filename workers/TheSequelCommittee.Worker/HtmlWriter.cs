using System.Globalization;
using System.Linq;
using System.Text;
using TheSequelCommittee.Worker;

namespace MovieDropOff;

public static class HtmlWriter
{
    // --- Score bands (percent) ---
    private const double CLASSIC = 80.0;
    private const double GREAT = 70.0;
    private const double POOR = 50.0;

    // Icons
    private const string ICON_ACCEPT = "✅";
    private const string ICON_DENY = "🚫";

    // UI constants
    private const string PosterBaseDefault = "https://image.tmdb.org/t/p/w342";

    public static async Task WriteHtmlReportsAsync(
        List<FranchiseAgg> franchises,
        List<MovieJoined> moviesJoined,          // released-only
        List<FranchiseRunRow> runs,
        string ratingSource,
        string posterBaseUrl = PosterBaseDefault)
    {
        // Output folder
        var outDir = Path.Combine("out", "thesequelcommittee", "html");
        Directory.CreateDirectory(outDir);

        // Lookups
        var runById = runs.ToDictionary(r => r.CollectionId, r => r);
        var releasedByCid = moviesJoined
            .GroupBy(m => m.CollectionId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.ReleaseDate ?? DateTime.MaxValue)
                      .ThenBy(x => x.Title)
                      .ToList()
            );

        // Upcoming from franchise_members.csv
        Dictionary<int, List<MemberRow>> upcomingByCid = new();
        var membersCsvPath = Path.Combine("out", "thesequelcommittee", "franchise_members.csv");
        if (File.Exists(membersCsvPath))
        {
            try
            {
                var allMembers = Csv.LoadMembersCsv(membersCsvPath);
                var futureOnly = allMembers.Where(m => Utils.IsFutureRelease(m.ReleaseDate)).ToList();
                var releasedIds = new HashSet<int>(moviesJoined.Select(m => m.MovieTmdbId));

                foreach (var grp in futureOnly.GroupBy(m => m.CollectionId))
                {
                    var list = grp
                        .Where(m => !releasedIds.Contains(m.MovieTmdbId))
                        .OrderBy(m => Utils.ParseDate(m.ReleaseDate) ?? DateTime.MaxValue)
                        .ThenBy(m => m.Title)
                        .ToList();
                    if (list.Count > 0) upcomingByCid[grp.Key] = list;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[HTML] Warning: failed to load future items: " + ex.Message);
            }
        }

        // ---------- CSS ----------
        string Css = @"
:root { color-scheme: dark; }
body{margin:0;font-family:system-ui,-apple-system,Segoe UI,Roboto,Inter,Arial;background:#0a0a0a;color:#e7e7e7}
.wrap{max-width:1100px;margin:0 auto;padding:24px}
h1{font-size:28px;margin:0 0 6px}
a{color:#8ab4ff;text-decoration:none}
a:hover{text-decoration:underline}

/* Grids */
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(160px,1fr));gap:16px;margin:6px 0 8px}
.gridUpcoming{display:grid;grid-template-columns:repeat(auto-fill,minmax(160px,1fr));gap:16px;margin:6px 0 28px}

/* Cards */
.card{position:relative;border-radius:14px;overflow:hidden;background:#151515;box-shadow:0 2px 12px rgba(0,0,0,.45)}
.poster{aspect-ratio:2/3;background:#1f1f1f;display:grid;place-items:center;position:relative}
.poster img{width:100%;height:100%;object-fit:cover;display:block}

/* Overlays */
.cap{
  position:absolute;left:0;right:0;bottom:0;padding:10px 10px 12px;
  background:linear-gradient(to top, rgba(0,0,0,.85), rgba(0,0,0,.35) 60%, rgba(0,0,0,0));
  text-shadow:0 1px 2px rgba(0,0,0,.8);
}
.ttl{font-weight:700;font-size:15px;line-height:1.25;margin:0 0 4px}
.meta{font-size:12px;color:#e1e1e1}

/* Visual */
.out{filter:grayscale(1) brightness(0.7) opacity(0.5);}
.crown{position:absolute;top:6px;left:50%;transform:translateX(-50%);background:rgba(0,0,0,.75);padding:2px 6px;border-radius:999px;font-size:12px;z-index:4}
.idx{position:absolute;top:6px;left:8px;background:rgba(0,0,0,.7);padding:2px 6px;border-radius:999px;font-size:12px}

/* Table */
.section{margin:18px 0 10px;font-weight:600}
.tablewrap{overflow:auto;border:1px solid #262626;border-radius:12px;background:#111}
table{width:100%;border-collapse:collapse;font-size:14px}
th,td{padding:10px 12px;border-bottom:1px solid #1f1f1f;text-align:left;white-space:nowrap}
th{position:sticky;top:0;background:#141414}
tr:hover{background:#181818}
td.title{white-space:normal}
.badgeMini{display:inline-block;min-width:28px;text-align:center;padding:2px 6px;border:1px solid #2a2a2a;border-radius:999px;background:#151515;font-size:12px}

/* Index grid of collections */
.gridIdx{display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:16px}
.box{position:relative;display:block;border-radius:14px;padding:16px 18px;background:#151515;border:1px solid #262626}
.box:hover{border-color:#3a3a3a;background:#171717}
.box .ttl{font-weight:700}

/* Index header bits */
.kicker{color:#bdbdbd;margin:0 0 16px;font-size:14px}
.footer-note{color:#a9a9a9;margin-top:22px;font-size:12px;text-align:right}

/* Index two groups */
.groupTitle{margin:22px 0 4px;font-weight:800;color:#e5e5e5}
.groupSub{margin:0 0 12px;color:#bdbdbd}

/* Seal under title */
.sealLine{color:#bdbdbd;margin:4px 0 12px;font-size:14px}
.sealBadge{display:inline-block;padding:6px 10px;border:1px solid #2a2a2a;border-radius:999px;background:#161616}

/* Minor headings */
.subhead{margin:18px 0 8px;color:#cfcfcf;font-weight:700}
";

        // ---------- Collection pages ----------
        foreach (var f in franchises)
        {
            releasedByCid.TryGetValue(f.CollectionId, out var released);
            runById.TryGetValue(f.CollectionId, out var run);
            upcomingByCid.TryGetValue(f.CollectionId, out var upcoming);

            released ??= new();

            // Base scores (no boost)
            var baseScores = released.Select(m => GetBaseScore(m, ratingSource)).ToList();

            // Colouring rule: +2% to FIRST film ONLY if there are NO other films ≥70
            bool anyOtherGreatOrClassic = baseScores.Skip(1).Any(s => s.HasValue && s.Value >= GREAT);
            var colourScores = baseScores.ToArray();
            if (!anyOtherGreatOrClassic && colourScores.Length > 0 && colourScores[0].HasValue)
            {
                double boosted = colourScores[0]!.Value + 2.0;
                if (boosted > 100.0) boosted = 100.0;
                colourScores[0] = boosted;
            }

            int? peakIdx = run?.PeakIndex;

            // Approved / Denied verdict (STRICT ≥70 with its own first-film +2% rule)
            var verdict = EvaluateAcceptedDenied(released, ratingSource);

            // ---------- HTML ----------
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang='en'><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>");
            sb.Append("<title>").Append(Utils.CsvEscape(SimplifyName(f.Name))).Append("</title><style>").Append(Css).Append("</style>");
            sb.Append("<body><div class='wrap'>");

            sb.Append("<a href='./index.html' style='color:#8ab4ff'>&larr; Back</a>");
            sb.Append("<h1>").Append(Utils.CsvEscape(SimplifyName(f.Name))).Append("</h1>");

            // Seal under the title: label only
            sb.Append("<div class='sealLine'><span class='sealBadge'>")
              .Append(VerdictIcon(verdict))
              .Append("&nbsp;")
              .Append(Utils.CsvEscape(VerdictLabel(verdict)))
              .Append("</span></div>");

            // Full collection (no heading), release order, colour if >=70 (after first-film rule), else grey
            if (released.Count > 0)
            {
                sb.Append("<div class='grid'>");
                for (int i = 0; i < released.Count; i++)
                {
                    bool isBest = (peakIdx == i);
                    bool greyOut = !(colourScores[i].HasValue && colourScores[i]!.Value >= GREAT); // >=70 coloured
                    EmitCard(sb, released[i], i, isBest, inStreak: false, greyOut: greyOut, tiny: false, posterBaseUrl);
                }
                sb.Append("</div>");
            }

            // Upcoming
            if (upcoming != null && upcoming.Count > 0)
            {
                sb.Append("<div class='subhead'>Upcoming</div><div class='gridUpcoming'>");
                foreach (var u in upcoming)
                    EmitCardUpcoming(sb, u, posterBaseUrl);
                sb.Append("</div>");
            }

            // Ranked table
            if (released.Count > 0)
            {
                var ranked = released
                    .Select((m, seriesIndex) => new
                    {
                        Movie = m,
                        SeriesIndex = seriesIndex,
                        Score = GetBaseScore(m, ratingSource),
                        ImdbVotes = m.ImdbVotes ?? 0
                    })
                    .OrderByDescending(x => x.Score.HasValue)
                    .ThenByDescending(x => Math.Round(x.Score ?? double.MinValue, 2))
                    .ThenByDescending(x => x.ImdbVotes)
                    .ThenBy(x => x.SeriesIndex)
                    .ToList();

                sb.Append("<div class='section'>Best to Worst Films in the Collection</div>");
                sb.Append("<div class='tablewrap'><table><thead><tr>");
                sb.Append("<th style='width:64px'>Rank</th>");
                sb.Append("<th>Title</th>");
                sb.Append("<th style='width:120px'>Release</th>");
                sb.Append("<th style='width:100px'>Rating</th>");
                sb.Append("</tr></thead><tbody>");

                int rank = 0;
                foreach (var row in ranked)
                {
                    rank++;
                    var m = row.Movie;
                    string dateStr = m.ReleaseDate?.ToString("dd MMM yyyy", CultureInfo.GetCultureInfo("en-GB")) ?? "—";
                    string link = TmdbMovieUrl(m.MovieTmdbId);
                    string scoreStr = row.Score.HasValue ? $"{Math.Round(row.Score.Value):0}%" : "—";

                    sb.Append("<tr>");
                    sb.Append("<td><span class='badgeMini'>").Append(rank).Append("</span></td>");
                    sb.Append("<td class='title'><a href='").Append(link).Append("' target='_blank' rel='noopener noreferrer'>")
                      .Append(Utils.CsvEscape(m.Title)).Append("</a></td>");
                    sb.Append("<td>").Append(Utils.CsvEscape(dateStr)).Append("</td>");
                    sb.Append("<td>").Append(scoreStr).Append("</td>");
                    sb.Append("</tr>");
                }

                sb.Append("</tbody></table></div>");
            }

            sb.Append("</div></body></html>");
            await File.WriteAllTextAsync(Path.Combine(outDir, $"{f.CollectionId}.html"), sb.ToString());
        }

        // ---------- Index page (2 groups) ----------
        string lastUpdatedUk = GetUkNow().ToString("dd'/'MM'/'yy", CultureInfo.GetCultureInfo("en-GB"));

        var accepted = new List<FranchiseAgg>();
        var denied = new List<FranchiseAgg>();

        foreach (var f in franchises)
        {
            releasedByCid.TryGetValue(f.CollectionId, out var released);
            released ??= new();
            var cat = EvaluateAcceptedDenied(released, ratingSource);
            if (cat == Verdict.Accepted) accepted.Add(f); else denied.Add(f);
        }

        accepted = accepted.OrderBy(x => SimplifyName(x.Name)).ToList();
        denied = denied.OrderBy(x => SimplifyName(x.Name)).ToList();

        int total = franchises.Count;
        int ok = accepted.Count;
        double rate = total > 0 ? (ok * 100.0) / total : 0.0;
        string rateStr = rate.ToString("0.0", CultureInfo.InvariantCulture);

        var idx = new StringBuilder();
        idx.Append("<!doctype html><html lang='en'><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>");
        idx.Append("<title>The Sequel Committee</title><style>").Append(Css).Append("</style><body><div class='wrap'>");

        // Title + count + approval rate
        idx.Append("<h1>The Sequel Committee</h1>");
        idx.Append("<p class='kicker'>")
           .Append(total)
           .Append(" collections · Committee approval rate: ")
           .Append(rateStr)
           .Append("%</p>");

        // Accepted
        idx.Append("<div class='groupTitle'>").Append(ICON_ACCEPT).Append(" Accepted by the Committee</div>");
        idx.Append("<div class='groupSub'>Sequels approved!</div>");
        idx.Append("<div class='gridIdx'>");
        foreach (var f in accepted)
        {
            var nm = Utils.CsvEscape(SimplifyName(f.Name));
            idx.Append("<a class='box' href='./").Append(f.CollectionId).Append(".html'><div class='ttl'>").Append(nm).Append("</div></a>");
        }
        idx.Append("</div>");

        // Denied
        idx.Append("<div class='groupTitle' style='margin-top:28px;'>").Append(ICON_DENY).Append(" Denied by the Committee</div>");
        idx.Append("<div class='groupSub'>Congratulations, you've ruined it.</div>");
        idx.Append("<div class='gridIdx'>");
        foreach (var f in denied)
        {
            var nm = Utils.CsvEscape(SimplifyName(f.Name));
            idx.Append("<a class='box' href='./").Append(f.CollectionId).Append(".html" >< div class='ttl'>").Append(nm).Append("</div></a>");
        }
idx.Append("</div>");

        // Last updated footer
        idx.Append("<div class='footer-note'>Last updated ").Append(lastUpdatedUk).Append("</div>");

idx.Append("</div></body></html>");
        await File.WriteAllTextAsync(Path.Combine(outDir, "index.html"), idx.ToString());
    }

    // ---------- Approved / Denied (STRICT ≥70 with conditional first-film +2%) ----------
    private enum Verdict { Accepted, Denied }

private static Verdict EvaluateAcceptedDenied(List<MovieJoined> released, string ratingSource)
{
    if (released == null || released.Count == 0) return Verdict.Denied;

    var baseScores = released.Select(m => GetBaseScore(m, ratingSource)).ToList();

    // First-film +2% ONLY if there are NO other ≥70 films
    bool anyOtherGreat = baseScores.Skip(1).Any(s => s.HasValue && s.Value >= GREAT);
    var adj = baseScores.ToArray();
    if (!anyOtherGreat && adj.Length > 0 && adj[0].HasValue)
    {
        double boosted = adj[0]!.Value + 2.0;
        if (boosted > 100.0) boosted = 100.0;
        adj[0] = boosted;
    }

    bool allGreatOrClassic = adj.All(s => s.HasValue && s.Value >= GREAT);
    return allGreatOrClassic ? Verdict.Accepted : Verdict.Denied;
}

private static string VerdictIcon(Verdict v) => v == Verdict.Accepted ? ICON_ACCEPT : ICON_DENY;
private static string VerdictLabel(Verdict v) => v == Verdict.Accepted ? "Approved by the Committee" : "Denied by the Committee";

// ---------- Helpers ----------
private static string SimplifyName(string name)
{
    if (string.IsNullOrWhiteSpace(name)) return name;
    var t = name.Trim();
    if (t.EndsWith(" Collection", StringComparison.OrdinalIgnoreCase))
        return t[..^" Collection".Length].TrimEnd();
    return t;
}

// Raw score from chosen source (no boost)
private static double? GetBaseScore(MovieJoined m, string src)
{
    if (src == "rt_only") return m.RtCriticPct;
    if (src == "rt_audience_only") return m.RtAudiencePct;
    if (src == "rt") return m.RtCriticPct ?? m.ImdbRating100 ?? (m.TmdbVoteAverage > 0 ? m.TmdbVoteAverage * 10.0 : (double?)null);
    if (src == "rt_audience") return m.RtAudiencePct ?? m.ImdbRating100 ?? (m.TmdbVoteAverage > 0 ? m.TmdbVoteAverage * 10.0 : (double?)null);
    return m.RtCriticPct ?? m.RtAudiencePct ?? m.ImdbRating100 ?? (m.TmdbVoteAverage > 0 ? m.TmdbVoteAverage * 10.0 : (double?)null);
}

private static DateTime GetUkNow()
{
    try
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
    }
    catch
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        }
        catch
        {
            return DateTime.UtcNow;
        }
    }
}

private static void EmitCard(
    StringBuilder sb,
    MovieJoined m,
    int seriesIndex,
    bool isBest,
    bool inStreak,
    bool greyOut,
    bool tiny,
    string posterBaseUrl)
{
    string link = TmdbMovieUrl(m.MovieTmdbId);
    string posterUrl = string.IsNullOrWhiteSpace(m.PosterPath)
        ? ""
        : (m.PosterPath!.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? m.PosterPath!
            : $"{posterBaseUrl}{m.PosterPath}");
    string ukDate = m.ReleaseDate?.ToString("dd MMM yyyy", CultureInfo.GetCultureInfo("en-GB")) ?? "";

    var cls = new List<string> { "card" };
    if (greyOut) cls.Add("out");

    sb.Append("<article class='").Append(string.Join(" ", cls)).Append("'>");
    if (isBest) sb.Append("<div class='crown'>👑</div>");
    sb.Append("<div class='idx'>#").Append(seriesIndex + 1).Append("</div>");
    sb.Append("<a href='").Append(link).Append("' target='_blank' rel='noopener noreferrer' class='poster'>");
    if (!string.IsNullOrWhiteSpace(posterUrl))
        sb.Append("<img loading='lazy' alt='").Append(Utils.CsvEscape(m.Title)).Append("' src='").Append(posterUrl).Append("'>");
    else
        sb.Append("<div style='color:#bbb;font-size:12px'>No image</div>");
    sb.Append("<div class='cap'><div class='ttl'>").Append(Utils.CsvEscape(m.Title)).Append("</div>")
      .Append("<div class='meta'>").Append(Utils.CsvEscape(ukDate)).Append("</div></div></a></article>");
}

private static void EmitCardUpcoming(StringBuilder sb, MemberRow u, string posterBaseUrl)
{
    string link = TmdbMovieUrl(u.MovieTmdbId);
    string posterUrl = string.IsNullOrWhiteSpace(u.PosterPath)
        ? ""
        : (u.PosterPath!.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? u.PosterPath!
            : $"{posterBaseUrl}{u.PosterPath}");
    string ukDate = Utils.ParseDate(u.ReleaseDate)?.ToString("dd MMM yyyy", CultureInfo.GetCultureInfo("en-GB")) ?? "TBC";

    sb.Append("<article class='card'><div class='badgeUp'>Upcoming</div>");
    sb.Append("<a href='").Append(link).Append("' target='_blank' rel='noopener noreferrer' class='poster'>");
    if (!string.IsNullOrWhiteSpace(posterUrl))
        sb.Append("<img loading='lazy' alt='").Append(Utils.CsvEscape(u.Title)).Append("' src='").Append(posterUrl).Append("'>");
    else
        sb.Append("<div style='color:#bbb;font-size:12px'>No image</div>");
    sb.Append("<div class='cap'><div class='ttl'>").Append(Utils.CsvEscape(u.Title)).Append("</div>")
      .Append("<div class='meta'>").Append(Utils.CsvEscape(ukDate)).Append("</div></div></a></article>");
}

private static string TmdbMovieUrl(int id) => $"https://www.themoviedb.org/movie/{id}";
}
