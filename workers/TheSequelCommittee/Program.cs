using System.Diagnostics;
using System.Globalization;
using TheSequelCommittee;

internal class Program
{
    private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

    static async Task<int> Main(string[] args)
    {
        try
        {
            var opt = CliOptions.Parse(args);

            Console.WriteLine($"[Mode] html-only={(opt.HtmlOnly ? "yes" : "no")} reuse={(opt.Reuse ? 'y' : 'n')} no-fill={(opt.NoFill ? "yes" : "no")}");
            Console.WriteLine($"[Ratings] source={(string.IsNullOrWhiteSpace(opt.RatingSource) ? "tmdb" : opt.RatingSource)} (TMDb-only build), minImdbVotes={opt.MinImdbVotes}, blendAlpha={opt.BlendAlpha}, no-ratings={(opt.NoRatings ? "yes" : "no")}");
            Console.WriteLine($"[Release filter] include-future={(opt.IncludeFuture ? "yes" : "no (exclude)")}");
            Console.WriteLine($"[Streaks] good-threshold={opt.GoodThreshold}, first-film-grace={opt.FirstFilmGrace}, min-streak-len={opt.MinStreakLen}, prefer-origin={(opt.PreferOrigin ? "yes" : "no")}");

            Directory.CreateDirectory("./out");

            var franchises = new Dictionary<int, FranchiseAgg>();
            var members = new List<MemberRow>();

            HashSet<int> allowedIds = new();
            List<FranchiseAgg> franchisesOut = new();
            List<MemberRow> membersOut = new();
            List<MovieJoined> joinedOut = new();
            List<FranchiseRunRow> runsOut = new();

            // --- HTML ONLY PATH (no API calls) ---
            if (opt.HtmlOnly)
            {
                Console.WriteLine("[HTML] Rebuilding from cached TMDb CSVs only.");
                if (!File.Exists("./out/franchises.csv") || !File.Exists("./out/franchise_members.csv"))
                    throw new InvalidOperationException("Missing ./out/franchises.csv or ./out/franchise_members.csv. Run a TMDb crawl once to generate them.");

                foreach (var f in Csv.LoadFranchisesCsv("./out/franchises.csv")) franchises[f.CollectionId] = f;
                members.AddRange(Csv.LoadMembersCsv("./out/franchise_members.csv"));

                if (!opt.IncludeFuture)
                {
                    int before = members.Count;
                    members = members.Where(m => !Utils.IsFutureRelease(m.ReleaseDate)).ToList();
                    int removed = before - members.Count;
                    if (removed > 0) Console.WriteLine($"[Filter] Excluded {removed} unreleased films.");
                }

                Rescore(franchises, members);

                allowedIds = franchises.Values.Where(f => f.MovieCount >= opt.MinMovies).Select(f => f.CollectionId).ToHashSet();
                franchisesOut = franchises.Values.Where(f => allowedIds.Contains(f.CollectionId))
                                                 .OrderByDescending(f => f.Score)
                                                 .ThenByDescending(f => f.SumPopularity)
                                                 .ToList();
                membersOut = members.Where(m => allowedIds.Contains(m.CollectionId)).ToList();

                // TMDb-only join
                joinedOut = membersOut.Select(m => new MovieJoined
                {
                    CollectionId = m.CollectionId,
                    CollectionName = m.CollectionName,
                    MovieTmdbId = m.MovieTmdbId,
                    ImdbId = m.ImdbId, // may be empty; not used for scoring in TMDb-only mode
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

                runsOut = Analyzer.BuildRuns(
                    joinedOut,
                    "tmdb",                 // force TMDb score source for analysis
                    opt.MinImdbVotes,
                    opt.BlendAlpha,
                    opt.FallAdj, opt.FallCum, opt.FallK, opt.FallAvg,
                    opt.GoodThreshold, opt.MinStreakLen, opt.FirstFilmGrace, opt.PreferOrigin);

                await HtmlWriter.WriteHtmlReportsAsync(franchisesOut, joinedOut, runsOut, "tmdb", "https://image.tmdb.org/t/p/w342");
                Console.WriteLine("[Write] ./out/html index + collection pages");
                Console.WriteLine("[Done] HTML-only rebuild complete.");
                return 0;
            }

            // --- NORMAL PATH (TMDb crawl) ---

            var franchisesLoaded = false;
            if (opt.Reuse && File.Exists("./out/franchises.csv") && File.Exists("./out/franchise_members.csv"))
            {
                Console.WriteLine("[Reuse] Loading from ./out/*.csv …");
                foreach (var f in Csv.LoadFranchisesCsv("./out/franchises.csv")) franchises[f.CollectionId] = f;
                members.AddRange(Csv.LoadMembersCsv("./out/franchise_members.csv"));
                Console.WriteLine($"[Reuse] Loaded {franchises.Count} franchises, {members.Count} members.");
                franchisesLoaded = true;
            }

            if (!franchisesLoaded)
            {
                if (opt.Reuse) Console.WriteLine("[Reuse] CSVs not found; falling back to TMDb crawl.");
                if (string.IsNullOrWhiteSpace(opt.TmdbKey))
                    throw new InvalidOperationException("TMDb crawl requested. Set TMDB_API_KEY or run with --reuse --html-only.");

                using var tmdb = Tmdb.NewClient();
                var sw = Stopwatch.StartNew();
                var seen = new HashSet<int>();
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
                        if (!seen.Add(mb.Id)) continue;

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

                foreach (var f in franchises.Values)
                {
                    var ms = members.Where(m => m.CollectionId == f.CollectionId);
                    f.MovieCount = ms.Count();
                    f.SumPopularity = ms.Sum(m => m.Popularity);
                    f.TotalVoteCount = ms.Sum(m => m.VoteCount);
                    f.WeightedVoteSum = ms.Sum(m => m.VoteAverage * m.VoteCount);
                    f.MaxPopularity = ms.Any() ? ms.Max(m => m.Popularity) : 0;
                    f.AvgVoteWeighted = f.TotalVoteCount > 0 ? f.WeightedVoteSum / f.TotalVoteCount : 0.0;
                    f.Score = 0.5 * Math.Log10(1 + f.SumPopularity) + 0.3 * f.AvgVoteWeighted + 0.2 * Math.Log10(1 + f.TotalVoteCount);
                }
                await File.WriteAllTextAsync("./out/franchises.csv", Csv.BuildFranchisesCsv(franchises.Values));
                await File.WriteAllTextAsync("./out/franchise_members.csv", Csv.BuildMembersCsv(members));
                Console.WriteLine("[Write] franchises.csv, franchise_members.csv");
            }

            if (!opt.NoFill && !string.IsNullOrWhiteSpace(opt.TmdbKey))
            {
                Console.WriteLine("[Fill] Checking TMDb collection 'parts' for missing movies…");
                int added = await Filler.FillMissingCollectionPartsAsync(opt.TmdbKey!, franchises, members, opt.SleepMs, opt.FillLimit);
                Console.WriteLine($"[Fill] Added {added} missing movies.");
                Rescore(franchises, members);
                await File.WriteAllTextAsync("./out/franchises.csv", Csv.BuildFranchisesCsv(franchises.Values));
                await File.WriteAllTextAsync("./out/franchise_members.csv", Csv.BuildMembersCsv(members));
                Console.WriteLine("[Write] Updated base CSVs after fill.");
            }
            else
            {
                Console.WriteLine("[Fill] Skipped (either --no-fill set or no TMDB_API_KEY).");
            }

            if (!opt.IncludeFuture)
            {
                int before = members.Count;
                members = members.Where(m => !Utils.IsFutureRelease(m.ReleaseDate)).ToList();
                int removed = before - members.Count;
                if (removed > 0) Console.WriteLine($"[Filter] Excluded {removed} unreleased films (future/unknown dates).");
                Rescore(franchises, members);
            }

            allowedIds = franchises.Values.Where(f => f.MovieCount >= opt.MinMovies).Select(f => f.CollectionId).ToHashSet();
            var franchisesOut2 = franchises.Values.Where(f => allowedIds.Contains(f.CollectionId))
                                                  .OrderByDescending(f => f.Score)
                                                  .ThenByDescending(f => f.SumPopularity)
                                                  .ToList();
            var membersOut2 = members.Where(m => allowedIds.Contains(m.CollectionId)).ToList();

            // TMDb-only "joined" dataset (no external ratings)
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
                "tmdb",                 // ensure analysis uses TMDb votes (vote_average * 10)
                opt.MinImdbVotes,
                opt.BlendAlpha,
                opt.FallAdj, opt.FallCum, opt.FallK, opt.FallAvg,
                opt.GoodThreshold, opt.MinStreakLen, opt.FirstFilmGrace, opt.PreferOrigin);

            // Optional: write franchise_runs.csv for debugging
            await File.WriteAllTextAsync("./out/franchise_runs.csv", Csv.BuildRunCsv(runs));
            Console.WriteLine("[Write] franchise_runs.csv");

            await HtmlWriter.WriteHtmlReportsAsync(franchisesOut2, joined, runs, "tmdb", "https://image.tmdb.org/t/p/w342");
            Console.WriteLine("[Write] ./out/html index + collection pages");
            Console.WriteLine("[Done] All outputs written to ./out/");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
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
            f.Score = 0.5 * Math.Log10(1 + f.SumPopularity) + 0.3 * f.AvgVoteWeighted + 0.2 * Math.Log10(1 + f.TotalVoteCount);
        }
    }
}
