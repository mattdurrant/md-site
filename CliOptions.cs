namespace TheSequelCommittee;

public sealed class CliOptions
{
    public bool Reuse { get; init; }
    public bool NoFill { get; init; }
    public int FillLimit { get; init; } = int.MaxValue;

    public int Pages { get; init; } = 100;
    public int VoteCountMin { get; init; } = 100;
    public int MinMovies { get; init; } = 2;
    public int SleepMs { get; init; } = 250;

    public int FallAdj { get; init; } = 10;
    public int FallCum { get; init; } = 18;
    public int FallK { get; init; } = 2;
    public int FallAvg { get; init; } = 65;

    public string RatingSource { get; init; } = "auto";
    public int MinImdbVotes { get; init; } = 5000;
    public double BlendAlpha { get; init; } = 0.7;

    public string? TmdbKey { get; init; } = Environment.GetEnvironmentVariable("TMDB_API_KEY");
    public string? OmdbKey { get; init; } = Environment.GetEnvironmentVariable("OMDB_API_KEY");
    public int OmdbDelayMs { get; init; } = 150;

    public bool IncludeFuture { get; init; } = false;
    public bool HtmlOnly { get; init; } = false;
    public bool NoRatings { get; init; } = false;

    // NEW: streak behavior knobs
    public int GoodThreshold { get; init; } = 70;   // unchanged default
    public int FirstFilmGrace { get; init; } = 8;   // first film can be this many points below threshold and still be "good"
    public int MinStreakLen { get; init; } = 1;     // allow single-film streaks by default
    public bool PreferOrigin { get; init; } = true;  // favor streaks that start at index 0

    public bool NeedsTmdb => !Reuse || !NoFill;

    public static CliOptions Parse(string[] args)
    {
        int ArgInt(string name, int def) { var i = Array.IndexOf(args, name); return (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v)) ? v : def; }
        string ArgStr(string name, string def) { var i = Array.IndexOf(args, name); return (i >= 0 && i + 1 < args.Length) ? args[i + 1] : def; }
        double ArgDouble(string name, double def) { var i = Array.IndexOf(args, name); return (i >= 0 && i + 1 < args.Length && double.TryParse(args[i + 1], out var v)) ? v : def; }
        bool ArgFlag(string name) => Array.IndexOf(args, name) >= 0;

        // PreferOrigin: default true; you can disable with --no-prefer-origin
        bool preferOrigin = ArgFlag("--prefer-origin") || !ArgFlag("--no-prefer-origin");

        return new CliOptions
        {
            Reuse = ArgFlag("--reuse"),
            NoFill = ArgFlag("--no-fill"),
            FillLimit = ArgInt("--fill-limit", int.MaxValue),

            Pages = ArgInt("--pages", 100),
            VoteCountMin = ArgInt("--vote-count-min", 100),
            MinMovies = ArgInt("--min-movies", 2),
            SleepMs = ArgInt("--sleep-ms", 250),

            FallAdj = ArgInt("--fall-adj", 10),
            FallCum = ArgInt("--fall-cum", 18),
            FallK = ArgInt("--fall-k", 2),
            FallAvg = ArgInt("--fall-thresh", 65),

            RatingSource = ArgStr("--rating-source", "auto"),
            MinImdbVotes = ArgInt("--min-imdb-votes", 5000),
            BlendAlpha = ArgDouble("--blend-alpha", 0.7),

            OmdbDelayMs = ArgInt("--omdb-delay-ms", 150),

            IncludeFuture = ArgFlag("--include-future"),
            HtmlOnly = ArgFlag("--html-only"),
            NoRatings = ArgFlag("--no-ratings"),

            // NEW args
            GoodThreshold = ArgInt("--good-threshold", 70),
            FirstFilmGrace = ArgInt("--first-film-grace", 8),
            MinStreakLen = ArgInt("--min-streak-len", 1),
            PreferOrigin = preferOrigin
        };
    }
}
