using System.Diagnostics;
using System.Globalization;
using TheSequelCommittee.Worker;

internal class Program
{
    private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

    // Unified output root for CSVs + HTML
    private const string OUT_ROOT = "./out/sequelcommittee";

    static async Task<int> Main(string[] args)
    {
        try
        {
            var opt = CliOptions.Parse(args);

            Console.WriteLine($"[Mode] html-only={(opt.HtmlOnly ? "yes" : "no")} reuse={(opt.Reuse ? 'y' : 'n')} no-fill={(opt.NoFill ? "yes" : "no")} no-prune={(opt.NoPrune ? "yes" : "no")}");
            Console.WriteLine($"[Ratings] source={(string.IsNullOrWhiteSpace(opt.RatingSource) ? "tmdb" : opt.RatingSource)} (TMDb-only build), minImdbVotes={opt.MinImdbVotes}, blendAlpha={opt.BlendAlpha}");
            Console.WriteLine($"[Release filter] include-future={(opt.IncludeFuture ? "yes" : "no (exclude)")}");
            Console.WriteLine($"[Streaks] good-threshold={opt.GoodThreshold}, first-film-grace={opt.FirstFilmGrace}, min-streak-len={opt.MinStreakLen}, prefer-origin={(opt.PreferOrigin ? "yes" : "no")}");

            Directory.CreateDirectory(OUT_ROOT);

            string franchisesCsv = Path.Combine(OUT_ROOT, "franchises.csv");
            string membersCsv = Path.Combine(OUT_ROOT, "franchise_members.csv");
            string runsCsv = Path.Combine(OUT_ROOT, "franchise_runs.csv");

            var franchises = new Dictionary<int, FranchiseAgg>();
            var members = new List<MemberRow>();

            // --- HTML ONLY PATH (no API calls) ---
            if (opt.HtmlOnly)
            {
                Console.WriteLine("[HTML] Rebuilding from cached CSVs only.");
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

                var allowedIds = franchises.Values.Where(f => f.MovieCount >= opt.MinMovies)
                                                  .Select(f => f.CollectionId)
                                                  .ToHashSet();

                var franchisesOut = franchises.Values.Where(f => allowedIds.Contains(f.CollectionId))
                                                     .OrderByDescending(f => f.Score)
                                                     .ThenByDescending(f => f.SumPopularity)
                                                     .ToList();

                var membersOut = members.Where(m => allowedIds.Contains(m.CollectionId)).ToList();

                var joinedOut = membersOut.Select(m => new MovieJoined
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

                var runsOut = Analyzer.BuildRuns(
                    joinedOut,
                    "tmdb",
                    opt.MinImdbVotes,
                    opt.BlendAlpha,
                    (int)Math.Round(opt.FallAdj),
                    (int)Math.Round(opt.FallCum),
                    (int)Math.Round(opt.FallK),
                    (int)Math.Round(opt.FallAvg),
                    opt.GoodThreshold,
                    opt.MinStreakLen,
                    opt.FirstFilmGrace,
                    opt.PreferOrigin);

                await HtmlWriter.WriteHtmlReportsAsync(franchisesOut, joinedOut, runsOut, "tmdb", "https://image.tmdb.org/t/p/w342");
                Console.WriteLine("[Write] ./out/sequelcommittee index + collection pages");
                Console.WriteLine("[Done] HTML-only rebuild complete.");
                return 0;
            }

            // --- NORMAL PATH (reuse existing CSVs if present) ---
            var haveCsvs = File.Exists(franchisesCsv) && File.Exists(membersCsv);
            var franchisesLoaded = false;

            if (opt.Reuse && haveCsvs)
            {
                Console.WriteLine($"[Reuse] Loading from {OUT_ROOT}/*.csv …");
                foreach (var f in Csv.LoadFranchisesCsv(franchisesCsv)) franchises[f.CollectionId] = f;

                // De-dup members from CSV by (CollectionId, MovieTmdbId)
                var seen = new HashSet<(int cid, int mid)>();
                foreach (var m in Csv.LoadMembersCsv(membersCsv))
                {
                    var k = (m.CollectionId, m.MovieTmdbId);
                    if (seen.Add(k)) members.Add(m);
                }
                Console.WriteLine($"[Reuse] Loaded {franchises.Count} franchises, {members.Count} members.");
                franchisesLoaded = true;
            }

            // If we don't have CSVs or reuse isn't requested, do a TMDb crawl
            if (!franchisesLoaded)
            {
                if (opt.Reuse) Console.WriteLine("[Reuse] CSVs not found; falling back to TMDb crawl.");
                if (string.IsNullOrWhiteSpace(opt.TmdbKey))
                    throw new InvalidOperationException("TMDb crawl requested. Set TMDB_API_KEY or run with --reuse --html-only.");

                // If --no-prune, pre-load ANY existing CSVs to preserve them even if not using --reuse
                var existingFranchises = new Dictionary<int, FranchiseAgg>();
                var existingMembers = new List<MemberRow>();
                if (opt.NoPrune && haveCsvs)
                {
                    Console.WriteLine("[NoPrune] Pre-loading existing CSVs to preserve prior discoveries.");
                    foreach (var f in Csv.LoadFranchisesCsv(franchisesCsv)) existingFranchises[f.CollectionId] = f;

                    var seen = new HashSet<(int cid, int mid)>();
                    foreach (var m in Csv.LoadMembersCsv(membersCsv))
                    {
                        var key = (m.CollectionId, m.MovieTmdbId);
                        if (seen.Add(key)) existingMembers.Add(m);
                    }
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
                        {
                            agg = new FranchiseAgg { CollectionId = cid, Name = details.BelongsToCollection.Name };
                            franchises[cid] = agg;
                        }

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

                // ----- MERGE: keep prior data when --no-prune is set -----
                if (opt.NoPrune && File.Exists(franchisesCsv) && File.Exists(membersCsv))
                {
                    Console.WriteLine("[NoPrune] Merging prior data into today’s crawl.");
                    // If we didn't pre-load earlier (because reuse=false), pre-load now
                    if (!TryHasData(existingFranchises, existingMembers))
                    {
                        foreach (var f in Csv.LoadFranchisesCsv(franchisesCsv)) existingFranchises[f.CollectionId] = f;
                        var seen = new HashSet<(int cid, int mid)>();
                        foreach (var m in Csv.LoadMembersCsv(membersCsv))
                        {
                            var k = (m.CollectionId, m.MovieTmdbId);
                            if (seen.Add(k)) existingMembers.Add(m);
                        }
                    }

                    // Merge members (union by (CollectionId, MovieTmdbId))
                    var mergedMembers = new List<MemberRow>(members.Count + existingMembers.Count);
                    var seenPairs = new HashSet<(int cid, int mid)>();
                    void addMember(MemberRow m)
                    {
                        var key = (m.CollectionId, m.MovieTmdbId);
                        if (seenPairs.Add(key)) mergedMembers.Add(m);
                    }
                    // Preserve older first (so today's items don't duplicate)
                    foreach (var m in existingMembers) addMember(m);
                    foreach (var m in members) addMember(m);
                    members = mergedMembers;

                    // Merge franchises – ensure all existing collections survive
                    foreach (var kv in existingFranchises)
                        if (!franchises.ContainsKey(kv.Key))
                            franchises[kv.Key] = kv.Value;

                    Console.WriteLine($"[NoPrune] After merge: Collections={franchises.Count}, Members={members.Count}");
                }
                // ----- end merge -----

                // Recompute aggregates on the (possibly merged) set
                Rescore(franchises, members);

                // Persist
                await File.WriteAllTextAsync(franchisesCsv, Csv.BuildFranchisesCsv(franchises.Values));
                await File.WriteAllTextAsync(membersCsv, Csv.BuildMembersCsv(members));
                Console.WriteLine("[Write] franchises.csv, franchise_members.csv");
            }

            // Optional "fill" to patch missing parts inside known collections
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

            // Filter out future if requested
            if (!opt.IncludeFuture)
            {
                int before = members.Count;
                members = members.Where(m => !Utils.IsFutureRelease(m.ReleaseDate)).ToList();
                int removed = before - members.Count;
                if (removed > 0) Console.WriteLine($"[Filter] Excluded {removed} unreleased films (future/unknown dates).");
                Rescore(franchises, members);
            }

            // Prepare data for HTML
            var allowedIds2 = franchises.Values.Where(f => f.MovieCount >= opt.MinMovies)
                                               .Select(f => f.CollectionId)
                                               .ToHashSet();

            var franchisesOut2 = franchises.Values.Where(f => allowedIds2.Contains(f.CollectionId))
                                                  .OrderByDescending(f => f.Score)
                                                  .ThenByDescending(f => f.SumPopularity)
                                                  .ToList();

            var membersOut2 = members.Where(m => allowedIds2.Contains(m.CollectionId)).ToList();

            var joined = membersOut2.Select(m => new MovieJoined
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
                joined,
                "tmdb",
                opt.MinImdbVotes,
                opt.BlendAlpha,
                (int)Math.Round(opt.FallAdj),
                (int)Math.Round(opt.FallCum),
                (int)Math.Round(opt.FallK),
                (int)Math.Round(opt.FallAvg),
                opt.GoodThreshold,
                opt.MinStreakLen,
                opt.FirstFilmGrace,
                opt.PreferOrigin);

            await File.WriteAllTextAsync(runsCsv, Csv.BuildRunCsv(runs));
            Console.WriteLine("[Write] franchise_runs.csv");

            await HtmlWriter.WriteHtmlReportsAsync(franchisesOut2, joined, runs, "tmdb", "https://image.tmdb.org/t/p/w342");
            Console.WriteLine("[Write] ./out/sequelcommittee index + collection pages");
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

    private static bool TryHasData(Dictionary<int, FranchiseAgg> fr, List<MemberRow> mm) =>
        (fr != null && fr.Count > 0) || (mm != null && mm.Count > 0);

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
}
