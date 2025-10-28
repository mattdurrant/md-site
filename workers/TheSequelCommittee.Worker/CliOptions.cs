using System;
using System.Globalization;

namespace TheSequelCommittee.Worker
{
    public sealed class CliOptions
    {
        // ---- High-level modes ----
        public bool HtmlOnly { get; private set; }     // `html-only`
        public bool BuildAll { get; private set; }     // `build-all`

        // ---- Behaviour toggles ----
        public bool Reuse { get; private set; } = false;       // --reuse
        public bool NoFill { get; private set; } = false;      // --no-fill
        public bool NoPrune { get; private set; } = false;     // --no-prune (preserve CSV rows not rediscovered today)
        public bool IncludeFuture { get; private set; } = false; // --include-future (default exclude)

        // ---- Discovery / scope ----
        public int Pages { get; private set; } = 10;           // --pages
        public int MinMovies { get; private set; } = 2;        // --min-movies
        public int VoteCountMin { get; private set; } = 150;   // --vote-count-min (TMDb discover filter)

        // ---- Ratings / analysis ----
        public string? RatingSource { get; private set; } = "tmdb"; // --rating-source (tmdb|rt|rt_only|rt_audience|rt_audience_only)
        public int MinImdbVotes { get; private set; } = 5000;       // --min-imdb-votes (when mixing imdb)
        public double BlendAlpha { get; private set; } = 0.7;        // --blend-alpha (if you blend sources)

        // Fall-off params (kept for compatibility with Analyzer.BuildRuns even if not used heavily)
        public double FallAdj { get; private set; } = 0.0;     // --fall-adj
        public double FallCum { get; private set; } = 0.0;     // --fall-cum
        public double FallK { get; private set; } = 1.0;     // --fall-k
        public double FallAvg { get; private set; } = 0.0;     // --fall-avg

        // Streak & scoring knobs
        public int GoodThreshold { get; private set; } = 70;   // --good-threshold (percent)
        public int MinStreakLen { get; private set; } = 1;     // --min-streak
        public int FirstFilmGrace { get; private set; } = 2;   // --first-film-grace (not used in latest logic but preserved)
        public bool PreferOrigin { get; private set; } = true; // --prefer-origin / --no-prefer-origin

        // TMDb / crawl niceties
        public string? TmdbKey { get; private set; }           // from env or --tmdb-key
        public int SleepMs { get; private set; } = 250;        // --sleep-ms (politeness)
        public int FillLimit { get; private set; } = 1000;     // --fill-limit (max missing parts to add)

        public static CliOptions Parse(string[] args)
        {
            var opt = new CliOptions();

            // Pick up TMDB_API_KEY from env by default
            opt.TmdbKey = Environment.GetEnvironmentVariable("TMDB_API_KEY");

            // Positional commands (html-only, build-all)
            foreach (var a in args)
            {
                if (a.Equals("html-only", StringComparison.OrdinalIgnoreCase))
                    opt.HtmlOnly = true;
                if (a.Equals("build-all", StringComparison.OrdinalIgnoreCase))
                    opt.BuildAll = true;
            }

            // Key/value and flags
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];

                // normalise --key=value form
                string key = a;
                string? val = null;
                int eq = a.IndexOf('=');
                if (eq > 0 && a.StartsWith("--"))
                {
                    key = a[..eq];
                    val = a[(eq + 1)..];
                }

                switch (key)
                {
                    case "--help":
                    case "-h":
                    case "/?":
                        PrintHelpAndExit();
                        break;

                    case "--reuse": opt.Reuse = true; break;
                    case "--no-fill": opt.NoFill = true; break;
                    case "--no-prune": opt.NoPrune = true; break;
                    case "--include-future": opt.IncludeFuture = true; break;

                    case "--pages": opt.Pages = ReadInt(val ?? Next(args, ref i), 1, 5000); break;
                    case "--min-movies": opt.MinMovies = ReadInt(val ?? Next(args, ref i), 1, 1000); break;
                    case "--vote-count-min": opt.VoteCountMin = ReadInt(val ?? Next(args, ref i), 0, 1_000_000); break;

                    case "--rating-source": opt.RatingSource = (val ?? Next(args, ref i))?.Trim(); break;
                    case "--min-imdb-votes": opt.MinImdbVotes = ReadInt(val ?? Next(args, ref i), 0, 10_000_000); break;
                    case "--blend-alpha": opt.BlendAlpha = ReadDouble(val ?? Next(args, ref i), 0, 1); break;

                    case "--fall-adj": opt.FallAdj = ReadDouble(val ?? Next(args, ref i), -1000, 1000); break;
                    case "--fall-cum": opt.FallCum = ReadDouble(val ?? Next(args, ref i), -1000, 1000); break;
                    case "--fall-k": opt.FallK = ReadDouble(val ?? Next(args, ref i), -1000, 1000); break;
                    case "--fall-avg": opt.FallAvg = ReadDouble(val ?? Next(args, ref i), -1000, 1000); break;

                    case "--good-threshold": opt.GoodThreshold = ReadInt(val ?? Next(args, ref i), 0, 100); break;
                    case "--min-streak": opt.MinStreakLen = ReadInt(val ?? Next(args, ref i), 1, 1000); break;
                    case "--first-film-grace": opt.FirstFilmGrace = ReadInt(val ?? Next(args, ref i), 0, 100); break;

                    case "--prefer-origin": opt.PreferOrigin = true; break;
                    case "--no-prefer-origin": opt.PreferOrigin = false; break;

                    case "--tmdb-key": opt.TmdbKey = (val ?? Next(args, ref i)); break;
                    case "--sleep-ms": opt.SleepMs = ReadInt(val ?? Next(args, ref i), 0, 10000); break;
                    case "--fill-limit": opt.FillLimit = ReadInt(val ?? Next(args, ref i), 0, 1_000_000); break;

                    // Positional are already handled; ignore them here
                    case "html-only":
                    case "build-all":
                        break;

                    default:
                        if (key.StartsWith("--"))
                        {
                            Console.WriteLine($"[Warn] Unknown switch: {key}");
                        }
                        break;
                }
            }

            return opt;
        }

        // ---------- helpers ----------

        private static string Next(string[] a, ref int i)
        {
            if (i + 1 >= a.Length) throw new ArgumentException($"Missing value for {a[i]}");
            i++;
            return a[i];
        }

        private static int ReadInt(string s, int min, int max)
        {
            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                throw new ArgumentException($"Expected integer but got '{s}'");
            if (v < min || v > max) throw new ArgumentOutOfRangeException(nameof(s), $"Value {v} out of range [{min},{max}]");
            return v;
        }

        private static double ReadDouble(string s, double min, double max)
        {
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                throw new ArgumentException($"Expected number but got '{s}'");
            if (v < min || v > max) throw new ArgumentOutOfRangeException(nameof(s), $"Value {v} out of range [{min},{max}]");
            return v;
        }

        private static void PrintHelpAndExit()
        {
            Console.WriteLine(@"
The Sequel Committee — CLI

Usage:
  dotnet run -- [options] build-all
  dotnet run -- [options] html-only

Options:
  --reuse                 Reuse existing CSVs if present (skip fresh crawl).
  --no-fill               Skip TMDb 'parts' completion step.
  --no-prune              Preserve previously discovered CSV rows not found today (union-merge).
  --include-future        Include unreleased titles in outputs.

  --pages N               TMDb discover pages to crawl (default 10).
  --min-movies N          Minimum movies per collection (default 2).
  --vote-count-min N      Minimum TMDb vote_count filter on discover (default 150).

  --rating-source S       Score source: tmdb|rt|rt_only|rt_audience|rt_audience_only (default tmdb).
  --min-imdb-votes N      Minimum IMDb votes when mixing in IMDb (default 5000).
  --blend-alpha R         Blend alpha 0..1 (default 0.7).

  --good-threshold PCT    Threshold for 'good' (default 70).
  --min-streak N          Minimum streak length (default 1).
  --first-film-grace N    (legacy knob; retained for compatibility)
  --prefer-origin         Prefer streaks starting with film #1 (default on)
  --no-prefer-origin      Disable that preference.

  --tmdb-key KEY          TMDb API key (or set TMDB_API_KEY env var).
  --sleep-ms N            Delay between API calls (default 250).
  --fill-limit N          Max parts to add during fill (default 1000).

Commands:
  build-all               Full pipeline (discover/fill/aggregate/html).
  html-only               Rebuild HTML from existing CSVs (no API calls).

Examples:
  dotnet run -- --pages 200 --allow-fill build-all
  dotnet run -- --reuse --no-prune build-all
  dotnet run -- html-only
");
            Environment.Exit(0);
        }
    }
}
