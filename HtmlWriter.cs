using System.Globalization;
using System.Linq;
using System.Text;

namespace TheSequelCommittee;

public static class HtmlWriter
{
    // --- Score bands (percent) ---
    private const double CLASSIC = 80.0;
    private const double GREAT = 70.0;
    private const double DECENT = 65.0;
    private const double POOR = 50.0;

    // Icons
    private const string ICON_BEAT = "🏆"; // Golden cup award
    private const string ICON_GOOD = "✅"; // Seal of approval
    private const string ICON_BAD = "🚫"; // Seal of disapproval

    // --- UI constants ---
    private const string PosterBaseDefault = "https://image.tmdb.org/t/p/w342";

    public static async Task WriteHtmlReportsAsync(
        List<FranchiseAgg> franchises,
        List<MovieJoined> moviesJoined,          // released-only
        List<FranchiseRunRow> runs,
        string ratingSource,
        string posterBaseUrl = PosterBaseDefault)
    {
        var outDir = Path.Combine("out", "html");
        Directory.CreateDirectory(outDir);

        // Prepare lookups
        var runById = runs.ToDictionary(r => r.CollectionId, r => r);
        var releasedByCid = moviesJoined
            .GroupBy(m => m.CollectionId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.ReleaseDate ?? DateTime.MaxValue)
                      .ThenBy(x => x.Title)
                      .ToList()
            );

        // Load unreleased (upcoming) from franchise_members.csv
        Dictionary<int, List<MemberRow>> upcomingByCid = new();
        var membersCsvPath = Path.Combine("out", "franchise_members.csv");
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
.oneliner{color:#bdbdbd;margin:8px 0 16px;font-size:14px}

/* Section heading */
.subhead{margin:18px 0 8px;color:#cfcfcf;font-weight:700}

/* Grids */
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(160px,1fr));gap:16px;margin:6px 0 8px}
.gridTiny{display:grid;grid-template-columns:repeat(auto-fill,minmax(120px,1fr));gap:12px;margin:6px 0 18px}
.gridUpcoming{display:grid;grid-template-columns:repeat(auto-fill,minmax(160px,1fr));gap:16px;margin:6px 0 28px}

/* Cards */
.card{position:relative;border-radius:14px;overflow:hidden;background:#151515;box-shadow:0 2px 12px rgba(0,0,0,.45)}
.card.tiny{border-radius:10px;box-shadow:0 1px 6px rgba(0,0,0,.30)}
.poster{aspect-ratio:2/3;background:#1f1f1f;display:grid;place-items:center;position:relative}
.poster img{width:100%;height:100%;object-fit:cover;display:block}

/* Overlays */
.cap{
  position:absolute;left:0;right:0;bottom:0;padding:10px 10px 12px;
  background:linear-gradient(to top, rgba(0,0,0,.85), rgba(0,0,0,.35) 60%, rgba(0,0,0,0));
  text-shadow:0 1px 2px rgba(0,0,0,.8);
}
.ttl{font-weight:700;font-size:15px;line-height:1.25;margin:0 0 4px}
.card.tiny .ttl{font-size:13px}
.meta{font-size:12px;color:#e1e1e1}
.card.tiny .meta{font-size:10.5px}

/* Visual */
.instreak{outline:2px solid rgba(45,212,191,.7);}
.out{filter:grayscale(1) brightness(0.7) opacity(0.5);}
.crown{position:absolute;top:6px;left:50%;transform:translateX(-50%);background:rgba(0,0,0,.75);padding:2px 6px;border-radius:999px;font-size:12px;z-index:4}

/* Table */
.section{margin:8px 0 10px;font-weight:600}
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

/* Index groups */
.groupTitle{margin:22px 0 10px;font-weight:700;color:#d9d9d9}
.seal{display:inline-block;margin-left:8px;font-size:18px;vertical-align:middle}

/* Collection seal under title */
.sealLine{color:#bdbdbd;margin:4px 0 12px;font-size:14px}
.sealBadge{display:inline-block;padding:6px 10px;border:1px solid #2a2a2a;border-radius:999px;background:#161616}
";

        // ---------- Build each collection page ----------
        foreach (var f in franchises)
        {
            releasedByCid.TryGetValue(f.CollectionId, out var released);
            runById.TryGetValue(f.CollectionId, out var run);
            upcomingByCid.TryGetValue(f.CollectionId, out var upcoming);

            released ??= new();

            // --- Precompute adjusted scores for COLOURING (>=65 rule) ---
            // Base scores for all films (no boost)
            var baseScores = released.Select(m => GetBaseScore(m, ratingSource)).ToList();
            bool anyOtherDecent = baseScores.Skip(1).Any(s => s.HasValue && s.Value >= DECENT);

            // Copy & apply +2% to FIRST film ONLY if there are NO other >=65
            var colourScores = baseScores.ToArray();
            if (!anyOtherDecent && colourScores.Count() > 0 && colourScores[0].HasValue)
            {
                double boosted = colourScores[0]!.Value + 2.0;
                if (boosted > 100.0) boosted = 100.0;
                colourScores[0] = boosted;
            }

            int? peakIdx = run?.PeakIndex;
            int? sStart = run?.StreakStartIndex;
            int? sEnd = run?.StreakEndIndex;

            // Theory category (STRICT ≥70 groups with its own boost rule)
            var theory = EvaluateTheoryStrict(released, ratingSource);

            // Recommended sequence (existing streak logic or peak fallback)
            var recommended = new List<MovieJoined>();
            if (released.Count > 0 && sStart.HasValue && sEnd.HasValue &&
                sStart.Value >= 0 && sEnd.Value >= sStart.Value && sEnd.Value < released.Count)
            {
                for (int i = sStart.Value; i <= sEnd.Value; i++)
                    recommended.Add(released[i]);
            }
            else if (released.Count > 0 && peakIdx is int p && p >= 0 && p < released.Count)
            {
                recommended.Add(released[p]);
            }

            // Ranked table (released-only)
            var ranked = released
                .Select((m, seriesIndex) => new
                {
                    Movie = m,
                    SeriesIndex = seriesIndex,
                    Score = GetBaseScore(m, ratingSource), // table shows raw chosen source
                    ImdbVotes = m.ImdbVotes ?? 0
                })
                .OrderByDescending(x => x.Score.HasValue)
                .ThenByDescending(x => Math.Round(x.Score ?? double.MinValue, 2))
                .ThenByDescending(x => x.ImdbVotes)
                .ThenBy(x => x.SeriesIndex)
                .ToList();

            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang='en'><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>");
            sb.Append("<title>").Append(Utils.CsvEscape(SimplifyName(f.Name))).Append("</title><style>").Append(Css).Append("</style>");
            sb.Append("<body><div class='wrap'>");

            sb.Append("<a href='./index.html' style='color:#8ab4ff'>&larr; Back</a>");
            sb.Append("<h1>").Append(Utils.CsvEscape(SimplifyName(f.Name))).Append("</h1>");

            // Seal under the title
            sb.Append("<div class='sealLine'><span class='sealBadge'>")
              .Append(TheorySealIcon(theory))
              .Append("&nbsp;")
              .Append(Utils.CsvEscape(TheorySealLabel(theory)))
              .Append("</span></div>");

            // Ones To Watch — apply ≥65 colouring too
            sb.Append("<div class='subhead'>The Ones To Watch</div><div class='grid'>");
            if (recommended.Count > 0)
            {
                foreach (var m in recommended)
                {
                    int seriesIndex = released.FindIndex(x => x.MovieTmdbId == m.MovieTmdbId);
                    bool isBest = (peakIdx == seriesIndex);
                    bool greyOut = !(colourScores[seriesIndex].HasValue && colourScores[seriesIndex]!.Value >= DECENT);
                    bool inStreak = sStart.HasValue && sEnd.HasValue && seriesIndex >= sStart.Value && seriesIndex <= sEnd.Value;

                    EmitCard(sb, m, seriesIndex, isBest, inStreak: inStreak, greyOut: greyOut, tiny: false, posterBaseUrl);
                }
            }
            else
            {
                sb.Append("<div style='color:#bdbdbd'>No recommended run found.</div>");
            }
            sb.Append("</div>");

            // Franchise one-liner verdict beneath recommendations (unchanged)
            var oneLiner = OneLineVerdict(released, run, upcoming, ratingSource);
            sb.Append("<p class='oneliner'>").Append(Utils.CsvEscape(oneLiner)).Append("</p>");

            // Complete set (smaller; grey out using ≥65 adjusted logic) — omit if identical to recommended
            bool completeEqualsRecommended = recommended.Count > 0 && recommended.Count == released.Count &&
                                             recommended.Select(x => x.MovieTmdbId).SequenceEqual(released.Select(x => x.MovieTmdbId));

            if (released.Count > 0 && !completeEqualsRecommended)
            {
                sb.Append("<div class='subhead'>Complete set</div><div class='gridTiny'>");
                for (int i = 0; i < released.Count; i++)
                {
                    bool isBest = (peakIdx == i);
                    bool inStreak = sStart.HasValue && sEnd.HasValue && i >= sStart.Value && i <= sEnd.Value;
                    bool greyOut = !(colourScores[i].HasValue && colourScores[i]!.Value >= DECENT);

                    EmitCard(sb, released[i], i, isBest, inStreak: inStreak, greyOut: greyOut, tiny: true, posterBaseUrl);
                }
                sb.Append("</div>");
            }

            // Upcoming (not included in calculations/table)
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

        // ---------- Index page with strict groups (unchanged) ----------
        string lastUpdatedUk = GetUkNow().ToString("dd'/'MM'/'yy", CultureInfo.GetCultureInfo("en-GB"));

        var beat = new List<FranchiseAgg>();
        var good = new List<FranchiseAgg>();
        var bad = new List<FranchiseAgg>();

        foreach (var f in franchises)
        {
            releasedByCid.TryGetValue(f.CollectionId, out var released);
            released ??= new();
            var cat = EvaluateTheoryStrict(released, ratingSource);
            switch (cat)
            {
                case TheoryCategory.Beat: beat.Add(f); break;
                case TheoryCategory.MatchGood: good.Add(f); break;
                default: bad.Add(f); break;
            }
        }

        beat = beat.OrderBy(x => SimplifyName(x.Name)).ToList();
        good = good.OrderBy(x => SimplifyName(x.Name)).ToList();
        bad = bad.OrderBy(x => SimplifyName(x.Name)).ToList();

        var idx = new StringBuilder();
        idx.Append("<!doctype html><html lang='en'><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>");
        idx.Append("<title>The Sequel Committee</title><style>").Append(Css).Append("</style><body><div class='wrap'>");

        // Title + overall count
        idx.Append("<h1>The Sequel Committee</h1>");
        idx.Append("<p class='kicker'>").Append(franchises.Count).Append(" collections</p>");

        // Group 1: Beat the theory (🏆)
        idx.Append("<div class='groupTitle'>Beat the theory <span class='seal'>").Append(ICON_BEAT).Append("</span></div>");
        idx.Append("<div class='gridIdx'>");
        foreach (var f in beat)
        {
            var nm = Utils.CsvEscape(SimplifyName(f.Name));
            idx.Append("<a class='box' href='./").Append(f.CollectionId).Append(".html'><div class='ttl'>").Append(nm).Append("</div></a>");
        }
        idx.Append("</div>");

        // Group 2: Match the theory (good) (✅)
        idx.Append("<div class='groupTitle'>Match the theory (good) <span class='seal'>").Append(ICON_GOOD).Append("</span></div>");
        idx.Append("<div class='gridIdx'>");
        foreach (var f in good)
        {
            var nm = Utils.CsvEscape(SimplifyName(f.Name));
            idx.Append("<a class='box' href='./").Append(f.CollectionId).Append(".html'><div class='ttl'>").Append(nm).Append("</div></a>");
        }
        idx.Append("</div>");

        // Group 3: Match the theory (bad) (🚫)
        idx.Append("<div class='groupTitle'>Match the theory (bad) <span class='seal'>").Append(ICON_BAD).Append("</span></div>");
        idx.Append("<div class='gridIdx'>");
        foreach (var f in bad)
        {
            var nm = Utils.CsvEscape(SimplifyName(f.Name));
            idx.Append("<a class='box' href='./").Append(f.CollectionId).Append(".html'><div class='ttl'>").Append(nm).Append("</div></a>");
        }
        idx.Append("</div>");

        // Last updated footer (UK format dd/MM/yy)
        idx.Append("<div class='footer-note'>Last updated ").Append(lastUpdatedUk).Append("</div>");

        idx.Append("</div></body></html>");
        await File.WriteAllTextAsync(Path.Combine(outDir, "index.html"), idx.ToString());
    }

    // ---------- Theory evaluation (STRICT ≥70 groups) ----------

    private enum TheoryCategory { Beat, MatchGood, MatchBad }

    private static TheoryCategory EvaluateTheoryStrict(List<MovieJoined> released, string ratingSource)
    {
        if (released == null || released.Count == 0) return TheoryCategory.MatchBad;

        // Base scores for all films (no boost)
        var baseScores = released.Select(m => GetBaseScore(m, ratingSource)).ToList();

        // Determine if there are any Great/Classic films beyond the first (>=70)
        bool anyOtherGood = baseScores.Skip(1).Any(s => s.HasValue && s.Value >= GREAT);

        // Adjust first film +2% ONLY if there are NO other ≥70 films
        var adjScores = baseScores.ToArray();
        if (!anyOtherGood && adjScores.Length > 0 && adjScores[0].HasValue)
        {
            double boosted = adjScores[0]!.Value + 2.0;
            if (boosted > 100.0) boosted = 100.0;
            adjScores[0] = boosted;
        }

        // STRICT grouping: to be in Beat or MatchGood, *every* released film must be ≥70 (Great/Classic)
        bool allGreatOrClassic = adjScores.All(s => s.HasValue && s.Value >= GREAT);
        if (allGreatOrClassic && released.Count >= 4) return TheoryCategory.Beat;
        if (allGreatOrClassic && released.Count <= 3) return TheoryCategory.MatchGood;

        return TheoryCategory.MatchBad;
    }

    private static string TheorySealIcon(TheoryCategory cat)
        => cat switch
        {
            TheoryCategory.Beat => ICON_BEAT,
            TheoryCategory.MatchGood => ICON_GOOD,
            _ => ICON_BAD
        };

    private static string TheorySealLabel(TheoryCategory cat)
        => cat switch
        {
            TheoryCategory.Beat => "Beats the theory",
            TheoryCategory.MatchGood => "Matches (good)",
            _ => "Matches (bad)"
        };

    // ---------- Helpers ----------

    private static string SimplifyName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;
        var t = name.Trim();
        if (t.EndsWith(" Collection", StringComparison.OrdinalIgnoreCase))
            return t[..^" Collection".Length].TrimEnd();
        return t;
    }

    // Raw score from the chosen source (no boost)
    private static double? GetBaseScore(MovieJoined m, string src)
    {
        if (src == "rt_only") return m.RtCriticPct;
        if (src == "rt_audience_only") return m.RtAudiencePct;
        if (src == "rt") return m.RtCriticPct ?? m.ImdbRating100 ?? (m.TmdbVoteAverage > 0 ? m.TmdbVoteAverage * 10.0 : (double?)null);
        if (src == "rt_audience") return m.RtAudiencePct ?? m.ImdbRating100 ?? (m.TmdbVoteAverage > 0 ? m.TmdbVoteAverage * 10.0 : (double?)null);
        return m.RtCriticPct ?? m.RtAudiencePct ?? m.ImdbRating100 ?? (m.TmdbVoteAverage > 0 ? m.TmdbVoteAverage * 10.0 : (double?)null);
    }

    private static string OneLineVerdict(List<MovieJoined> released, FranchiseRunRow? run, List<MemberRow>? upcoming, string ratingSource)
    {
        int total = released.Count;
        if (total == 0) return "No released films yet.";

        var good = new HashSet<int>();
        if (!string.IsNullOrWhiteSpace(run?.GoodIndicesCsv))
            foreach (var tok in run!.GoodIndicesCsv!.Split(';', StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(tok, out var i) && i >= 0) good.Add(i);

        int goodCount = good.Count;
        int streakLen = run?.StreakLength ?? 0;
        int? sStart = run?.StreakStartIndex;
        int? sEnd = run?.StreakEndIndex;
        int? peak = run?.PeakIndex;

        var dates = released.Select(m => m.ReleaseDate).Where(d => d.HasValue).Select(d => d!.Value).OrderBy(d => d).ToList();
        DateTime? lastReleased = dates.Count > 0 ? dates[^1] : (DateTime?)null;
        DateTime? firstUpcoming = upcoming?.Select(u => Utils.ParseDate(u.ReleaseDate)).Where(d => d.HasValue).Select(d => d!.Value).OrderBy(d => d).FirstOrDefault();

        if (goodCount == 0)
            return "These movies were never good.";

        bool longGapCashIn = false;
        if (lastReleased.HasValue)
        {
            if (firstUpcoming.HasValue && (firstUpcoming.Value - lastReleased.Value).TotalDays >= 3652)
                longGapCashIn = true;
            else
            {
                for (int i = 1; i < released.Count; i++)
                {
                    var prev = released[i - 1].ReleaseDate;
                    var cur = released[i].ReleaseDate;
                    if (prev.HasValue && cur.HasValue && (cur.Value - prev.Value).TotalDays >= 3652 && !good.Contains(i))
                    { longGapCashIn = true; break; }
                }
            }
        }
        if (longGapCashIn)
            return "They just couldn't leave a beloved franchise alone.";

        if (sStart.HasValue && sEnd.HasValue && sStart.Value <= sEnd.Value && sStart.Value >= 0 && sEnd.Value < released.Count)
        {
            var streakScores = Enumerable.Range(sStart.Value, sEnd.Value - sStart.Value + 1)
                .Select(i => GetBaseScore(released[i], ratingSource))
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();

            if (streakScores.Count > 0)
            {
                double maxStreak = streakScores.Max();
                double avgStreak = streakScores.Average();
                if (maxStreak < CLASSIC || avgStreak < 75.0)
                    return "No classics but a decent set of movies.";
            }
        }

        if (goodCount == total && total <= 3)
            return "A strong set of movies. Perfect.";

        if (goodCount == total && total > 3)
            return "A strong set that stayed good beyond a trilogy.";

        if (goodCount == 1 && total >= 3)
            return good.Contains(0)
                ? "None of them are great, maybe just watch the first."
                : "One decent entry in an otherwise weak series.";

        if (run is not null && run.StreakStartIndex == 0 && streakLen >= 2 && run.StreakEndIndex.HasValue &&
            (total - (run.StreakEndIndex.Value + 1)) >= Math.Max(2, total / 3))
            return "A great franchise they have driven into the ground.";

        if (peak.HasValue && peak.Value <= Math.Max(1, total / 3) && run?.StreakEndIndex is int e && e < total - 1)
            return "A good set of movies that got worse over time.";

        if (peak.HasValue && peak.Value >= total - Math.Max(1, total / 3))
        {
            int badBefore = Enumerable.Range(0, peak.Value).Count(i => !good.Contains(i));
            if (badBefore >= Math.Max(1, total / 4))
                return "A strong set of movies that only got better over time.";
        }

        if (total > 3 && (streakLen <= 1 || goodCount <= total / 2))
            return "A franchise that went on too long with the occasional decent movie.";

        return "A solid run with a few bumps.";
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
                var tz = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time"); // Windows
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
        if (tiny) cls.Add("tiny");
        if (inStreak) cls.Add("instreak");
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
