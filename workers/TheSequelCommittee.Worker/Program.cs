using System.Diagnostics;
using System.Globalization;
using System.Text;
using TheSequelCommittee.Worker;

internal class Program
{
    private static readonly CultureInfo CI = CultureInfo.InvariantCulture;
    private const string OUT_ROOT = "./out/sequelcommittee";

    static async Task<int> Main(string[] args)
    {
        try
        {
            var opt = CliOptions.Parse(args);

            Console.WriteLine($"[Mode] html-only={(opt.HtmlOnly ? "yes" : "no")} reuse={(opt.Reuse ? 'y' : 'n')} no-fill={(opt.NoFill ? "yes" : "no")} no-prune={(opt.NoPrune ? "yes" : "no")}");
            Console.WriteLine($"[Ratings] source={(string.IsNullOrWhiteSpace(opt.RatingSource) ? "tmdb" : opt.RatingSource)} (TMDb-only), minImdbVotes={opt.MinImdbVotes}, blendAlpha={opt.BlendAlpha}");
            Console.WriteLine($"[Release filter] include-future={(opt.IncludeFuture ? "yes" : "no (exclude)")}");
            Console.WriteLine($"[Streaks] good-threshold={opt.GoodThreshold}, first-film-grace={opt.FirstFilmGrace}, min-streak-len={opt.MinStreakLen}, prefer-origin={(opt.PreferOrigin ? "yes" : "no")}");

            Directory.CreateDirectory(OUT_ROOT);

            string franchisesCsv = Path.Combine(OUT_ROOT, "franchises.csv");
            string membersCsv = Path.Combine(OUT_ROOT, "franchise_members.csv");
            string runsCsv = Path.Combine(OUT_ROOT, "franchise_runs.csv");

            var franchises = new Dictionary<int, FranchiseAgg>();
            var members = new List<MemberRow>();

            // Snapshot BEFORE (for changes)
            var beforeMembers = LoadMembersSafe(membersCsv);

            // ---------- HTML-ONLY ----------
            if (opt.HtmlOnly)
            {
                if (!File.Exists(franchisesCsv) || !File.Exists(membersCsv))
                    throw new InvalidOperationException($"Missing {franchisesCsv} or {membersCsv}. Run a TMDb crawl once to generate them.");

                foreach (var f in Csv.LoadFranchisesCsv(franchisesCsv)) franchises[f.CollectionId] = f;
                members.AddRange(Csv.LoadMembersCsv(membersCsv));

                if (!opt.IncludeFuture)
                {
                    int before = members.Count;
                    members = members.Where(m => !Utils.IsFutureRelease(m.ReleaseDate)).ToList();
                    int removed = before - members.Count;
                    if (removed > 0) Console.WriteLine($"[Filter] Excluded {removed} unreleased films.");
                }

                Rescore(franchises, members);
                await WriteHtmlAsync(franchises, members, runsCsv, opt);
                PostProcessIndexWithWhatsNew(OUT_ROOT, take: 15);
                return 0;
            }

            // ---------- NORMAL PATH ----------
            bool haveCsvs = File.Exists(franchisesCsv) && File.Exists(membersCsv);
            bool franchisesLoaded = false;

            if (opt.Reuse && haveCsvs)
            {
                Console.WriteLine($"[Reuse] Loading from {OUT_ROOT}/*.csv …");
                foreach (var f in Csv.LoadFranchisesCsv(franchisesCsv)) franchises[f.CollectionId] = f;
                members = LoadMembersSafe(membersCsv);
                Console.WriteLine($"[Reuse] Loaded {franchises.Count} franchises, {members.Count} members.");
                franchisesLoaded = true;
            }

            if (!franchisesLoaded)
            {
                if (opt.Reuse) Console.WriteLine("[Reuse] CSVs not found; falling back to TMDb crawl.");
                if (string.IsNullOrWhiteSpace(opt.TmdbKey))
                    throw new InvalidOperationException("TMDb crawl requested. Set TMDB_API_KEY or run with --reuse --html-only.");

                // Preload for no-prune merge
                var existingFranchises = new Dictionary<int, FranchiseAgg>();
                var existingMembers = new List<MemberRow>();
                if (opt.NoPrune && haveCsvs)
                {
                    Console.WriteLine("[NoPrune] Pre-loading existing CSVs.");
                    foreach (var f in Csv.LoadFranchisesCsv(franchisesCsv)) existingFranchises[f.CollectionId] = f;
                    existingMembers = LoadMembersSafe(membersCsv);
                }

                using var tmdb = Tmdb.NewClient();
                var sw = Stopwatch.StartNew();
                var seenMovieIds = new HashSet<int>();
                int detailsCalls = 0;
                var lastTick = Stopwatch.StartNew();
                int lastDetails = 0;

                Console.WriteLine("[TMDb] Discovering movies & collecting collections…");
                for (int page = 1; page <= opt.Pages; page++)
                {
                    var discover = await Tmdb.DiscoverMoviesAsync(tmdb, opt.TmdbKey!, page, opt.VoteCountMin);
                    if (discover?.Results is null || discover.Results.Count == 0)
                    {
                        Console.WriteLine($"[TMDb] Page {page}: empty; stopping.");
                        break;
                    }

                    Console.WriteLine($"[TMDb] Page {page}/{discover.TotalPages} | {discover.Results.Count} items");
                    int idx = 0;
                    foreach (var mb in discover.Results)
                    {
                        idx++;
                        if (!seenMovieIds.Add(mb.Id)) continue;

                        var details = await Tmdb.GetMovieDetailsAsync(tmdb, opt.TmdbKey!, mb.Id);
                        detailsCalls++;

                        if (details?.BelongsToCollection is null)
                        {
                            if (detailsCalls % 25 == 0) Utils.Heartbeat(page, idx, discover.TotalPages, sw, lastTick, ref lastDetails, detailsCalls);
                            await Task.Delay(opt.SleepMs);
                            continue;
                        }

                        int cid = details.BelongsToCollection.Id;
                        if (!franchises.TryGetValue(cid, out var agg))
                            franchises[cid] = agg = new FranchiseAgg { CollectionId = cid, Name = details.BelongsToCollection.Name };

                        agg.MovieCount++;
                        agg.SumPopularity += mb.Popularity;
                        agg.TotalVoteCount += mb.VoteCount;
                        agg.WeightedVoteSum += mb.VoteAverage * mb.VoteCount;
                        agg.MaxPopularity = Math.Max(agg.MaxPopularity, mb.Popularity);

                        members.Add(new MemberRow
                        {
                            CollectionId = cid,
                            CollectionName = agg.Name,
                            MovieTmdbId = details.Id,
                            Title = details.Title ?? "",
                            ReleaseDate = details.ReleaseDate,
                            Popularity = mb.Popularity,
                            VoteAverage = mb.VoteAverage,
                            VoteCount = mb.VoteCount,
                            ImdbId = details.ExternalIds?.ImdbId ?? "",
                            PosterPath = details.PosterPath ?? mb.PosterPath
                        });

                        if (detailsCalls % 10 == 0) Utils.Heartbeat(page, idx, discover.TotalPages, sw, lastTick, ref lastDetails, detailsCalls);
                        await Task.Delay(opt.SleepMs);
                    }

                    Console.WriteLine($"[TMDb] Page {page} complete | Collections: {franchises.Count} | Members: {members.Count}");
                    await Task.Delay(opt.SleepMs);
                    if (discover.TotalPages > 0 && page >= discover.TotalPages) break;
                }

                Console.WriteLine($"[TMDb] Finished in {sw.Elapsed:mm\\:ss}. Details: {detailsCalls}, Collections: {franchises.Count}, Members: {members.Count}");

                // Merge (no-prune)
                if (opt.NoPrune && haveCsvs)
                {
                    Console.WriteLine("[NoPrune] Merging prior data into today’s crawl.");
                    members = UnionMembers(existingMembers, members);
                    foreach (var kv in existingFranchises)
                        if (!franchises.ContainsKey(kv.Key))
                            franchises[kv.Key] = kv.Value;
                    Console.WriteLine($"[NoPrune] After merge: Collections={franchises.Count}, Members={members.Count}");
                }

                Rescore(franchises, members);
                await File.WriteAllTextAsync(franchisesCsv, Csv.BuildFranchisesCsv(franchises.Values));
                await File.WriteAllTextAsync(membersCsv, Csv.BuildMembersCsv(members));
                Console.WriteLine("[Write] franchises.csv, franchise_members.csv");
            }

            // Optional fill
            if (!opt.NoFill && !string.IsNullOrWhiteSpace(opt.TmdbKey))
            {
                Console.WriteLine("[Fill] Checking TMDb collection 'parts' for missing movies…");
                int added = await Filler.FillMissingCollectionPartsAsync(opt.TmdbKey!, franchises, members, opt.SleepMs, opt.FillLimit);
                Console.WriteLine($"[Fill] Added {added} missing movies.");
                Rescore(franchises, members);
                await File.WriteAllTextAsync(franchisesCsv, Csv.BuildFranchisesCsv(franchises.Values));
                await File.WriteAllTextAsync(membersCsv, Csv.BuildMembersCsv(members));
                Console.WriteLine("[Write] Updated base CSVs after fill.");
            }
            else
            {
                Console.WriteLine("[Fill] Skipped (either --no-fill set or no TMDB_API_KEY).");
            }

            // ----- CHANGELOG: append new additions -----
            AppendAddedMembersToChanges(beforeMembers, members, franchises, OUT_ROOT);

            // Filter for HTML view (hide future if requested)
            var htmlMembers = members;
            if (!opt.IncludeFuture)
            {
                int beforeCnt = htmlMembers.Count;
                htmlMembers = htmlMembers.Where(m => !Utils.IsFutureRelease(m.ReleaseDate)).ToList();
                int removed = beforeCnt - htmlMembers.Count;
                if (removed > 0) Console.WriteLine($"[Filter] Excluded {removed} unreleased films from HTML.");
                Rescore(franchises, htmlMembers);
            }

            await WriteHtmlAsync(franchises, htmlMembers, runsCsv, opt);

            // Inject global "What's new" into index.html
            PostProcessIndexWithWhatsNew(OUT_ROOT, take: 15);

            Console.WriteLine("[Done] All outputs written to ./out/sequelcommittee/");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    // ---------- Helpers ----------

    private static List<MemberRow> LoadMembersSafe(string path)
    {
        var list = new List<MemberRow>();
        if (!File.Exists(path)) return list;

        var seen = new HashSet<(int cid, int mid)>();
        foreach (var m in Csv.LoadMembersCsv(path))
        {
            var k = (m.CollectionId, m.MovieTmdbId);
            if (seen.Add(k)) list.Add(m);
        }
        return list;
    }

    private static List<MemberRow> UnionMembers(List<MemberRow> a, List<MemberRow> b)
    {
        var list = new List<MemberRow>(a.Count + b.Count);
        var seen = new HashSet<(int cid, int mid)>();
        void add(MemberRow m) { var k = (m.CollectionId, m.MovieTmdbId); if (seen.Add(k)) list.Add(m); }
        foreach (var m in a) add(m);
        foreach (var m in b) add(m);
        return list;
    }

    private static void Rescore(Dictionary<int, FranchiseAgg> franchises, List<MemberRow> members)
    {
        foreach (var f in franchises.Values)
        {
            var ms = members.Where(m => m.CollectionId == f.CollectionId);
            f.MovieCount = ms.Count();
            f.SumPopularity = ms.Sum(m => m.Popularity);
            f.TotalVoteCount = ms.Sum(m => m.VoteCount);
            f.WeightedVoteSum = ms.Sum(m => m.VoteAverage * m.VoteCount);
            f.MaxPopularity = ms.Any() ? ms.Max(m => m.Popularity) : 0;
            f.AvgVoteWeighted = f.TotalVoteCount > 0 ? f.WeightedVoteSum / f.TotalVoteCount : 0.0;
            f.Score = 0.5 * Math.Log10(1 + f.SumPopularity)
                              + 0.3 * f.AvgVoteWeighted
                              + 0.2 * Math.Log10(1 + f.TotalVoteCount);
        }
    }

    private static async Task WriteHtmlAsync(Dictionary<int, FranchiseAgg> franchises, List<MemberRow> members, string runsCsv, CliOptions opt)
    {
        var allowedIds = franchises.Values.Where(f => f.MovieCount >= opt.MinMovies)
                                          .Select(f => f.CollectionId)
                                          .ToHashSet();

        var franchisesOut = franchises.Values.Where(f => allowedIds.Contains(f.CollectionId))
                                             .OrderByDescending(f => f.Score)
                                             .ThenByDescending(f => f.SumPopularity)
                                             .ToList();

        var membersOut = members.Where(m => allowedIds.Contains(m.CollectionId)).ToList();

        var joined = membersOut.Select(m => new MovieJoined
        {
            CollectionId = m.CollectionId,
            CollectionName = m.CollectionName,
            MovieTmdbId = m.MovieTmdbId,
            ImdbId = m.ImdbId,
            Title = m.Title,
            ReleaseDate = Utils.ParseDate(m.ReleaseDate),
            Popularity = m.Popularity,
            TmdbVoteAverage = m.VoteAverage,
            TmdbVoteCount = m.VoteCount,
            ImdbRating100 = null,
            ImdbVotes = null,
            OmdbError = null,
            PosterPath = m.PosterPath,
            RtCriticPct = null,
            RtAudiencePct = null
        }).ToList();

        var runs = Analyzer.BuildRuns(
            joined, "tmdb",
            opt.MinImdbVotes, opt.BlendAlpha,
            (int)Math.Round(opt.FallAdj), (int)Math.Round(opt.FallCum),
            (int)Math.Round(opt.FallK), (int)Math.Round(opt.FallAvg),
            opt.GoodThreshold, opt.MinStreakLen, opt.FirstFilmGrace, opt.PreferOrigin);

        await File.WriteAllTextAsync(runsCsv, Csv.BuildRunCsv(runs));
        Console.WriteLine("[Write] franchise_runs.csv");

        await HtmlWriter.WriteHtmlReportsAsync(franchisesOut, joined, runs, "tmdb", "https://image.tmdb.org/t/p/w342");
        Console.WriteLine("[Write] ./out/sequelcommittee index + collection pages");
    }

    private static void AppendAddedMembersToChanges(
        List<MemberRow> beforeMembers,
        List<MemberRow> afterMembers,
        Dictionary<int, FranchiseAgg> franchises,
        string outRoot)
    {
        try
        {
            var beforeKeys = beforeMembers.Select(m => (m.CollectionId, m.MovieTmdbId)).ToHashSet();
            var afterKeys = afterMembers.Select(m => (m.CollectionId, m.MovieTmdbId)).ToHashSet();
            var addedKeys = afterKeys.Except(beforeKeys).ToList();

            if (addedKeys.Count == 0)
            {
                Console.WriteLine("[Changes] No new collection members detected.");
                return;
            }

            var index = afterMembers.GroupBy(m => (m.CollectionId, m.MovieTmdbId))
                                    .ToDictionary(g => g.Key, g => g.First());

            var toAppend = new List<ChangeEvent>(addedKeys.Count);
            foreach (var k in addedKeys)
            {
                if (!index.TryGetValue(k, out var m)) continue;
                var fname = franchises.TryGetValue(m.CollectionId, out var f) ? f.Name : m.CollectionName;
                toAppend.Add(new ChangeEvent(
                    DateTime.UtcNow,
                    m.CollectionId,
                    fname ?? "",
                    m.MovieTmdbId,
                    m.Title ?? "",
                    "AddedToCollection"
                ));
            }

            if (toAppend.Count > 0)
            {
                Changes.AppendChanges(outRoot, toAppend);
                Console.WriteLine($"[Changes] Appended {toAppend.Count} 'AddedToCollection' events.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[Changes] Warning: failed to append changes: " + ex.Message);
        }
    }

    private static void PostProcessIndexWithWhatsNew(string outRoot, int take)
    {
        var indexPath = Path.Combine(outRoot, "index.html");
        if (!File.Exists(indexPath))
        {
            Console.WriteLine("[WhatsNew] index.html not found; skipping injection.");
            return;
        }

        var changes = Changes.LoadChanges(outRoot)
                             .OrderByDescending(c => c.TimestampUtc)
                             .Take(take)
                             .ToList();

        if (changes.Count == 0)
        {
            Console.WriteLine("[WhatsNew] No changes found; nothing to inject.");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine(@"<section class=""whats-new-global"">");
        sb.AppendLine(@"  <h2>What’s new</h2>");
        sb.AppendLine(@"  <ul class=""whats-new-list"">");
        foreach (var ev in changes)
        {
            var when = ev.TimestampUtc.ToString("dd/MM/yy HH:mm 'UTC'", CI);
            // Not linking to collection to avoid guessing filenames; easy to add once we standardise slugs.
            sb.AppendLine($"    <li><strong>{Changes.Html(ev.Title)}</strong> added to <em>{Changes.Html(ev.CollectionName)}</em> — {when}</li>");
        }
        sb.AppendLine(@"  </ul>");
        sb.AppendLine(@"</section>");
        sb.AppendLine();

        var html = File.ReadAllText(indexPath);

        // Try to inject right after <main ...>
        int insertPos = -1;
        var mainIdx = html.IndexOf("<main", StringComparison.OrdinalIgnoreCase);
        if (mainIdx >= 0)
        {
            var gt = html.IndexOf('>', mainIdx);
            if (gt > mainIdx) insertPos = gt + 1;
        }
        // Fallback: right after <body ...>
        if (insertPos < 0)
        {
            var bodyIdx = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
            if (bodyIdx >= 0)
            {
                var gt = html.IndexOf('>', bodyIdx);
                if (gt > bodyIdx) insertPos = gt + 1;
            }
        }
        // Fallback: prepend
        if (insertPos < 0) insertPos = 0;

        var updated = html.Insert(insertPos, sb.ToString());

        // minimal CSS so it looks tidy
        var css = @"
<style>
  .whats-new-global{margin:1.25rem 0;padding:1rem;border-radius:.75rem;background:#0f172a10}
  .whats-new-global h2{margin:0 0 .5rem 0;font-size:1.1rem}
  .whats-new-list{margin:0;padding-left:1.1rem}
  .whats-new-list li{margin:.25rem 0}
</style>
";
        // add CSS before </head> if not already present
        if (updated.IndexOf(".whats-new-global", StringComparison.Ordinal) < 0)
        {
            var headEnd = updated.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
            if (headEnd >= 0) updated = updated.Insert(headEnd, css);
            else updated = css + updated;
        }

        File.WriteAllText(indexPath, updated, Encoding.UTF8);
        Console.WriteLine("[WhatsNew] Injected global 'What’s new' into index.html");
    }
}
