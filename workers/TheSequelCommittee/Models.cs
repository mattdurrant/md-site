using System.Text.Json.Serialization;

namespace TheSequelCommittee;

public record DiscoverResponse
{
    [JsonPropertyName("page")] public int Page { get; init; }
    [JsonPropertyName("total_pages")] public int TotalPages { get; init; }
    [JsonPropertyName("results")] public List<MovieBrief> Results { get; init; } = new();
}

public record MovieBrief
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("title")] public string? Title { get; init; }
    [JsonPropertyName("release_date")] public string? ReleaseDate { get; init; }
    [JsonPropertyName("popularity")] public double Popularity { get; init; }
    [JsonPropertyName("vote_average")] public double VoteAverage { get; init; }
    [JsonPropertyName("vote_count")] public int VoteCount { get; init; }
    [JsonPropertyName("poster_path")] public string? PosterPath { get; init; }
}

public record MovieDetails
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("title")] public string? Title { get; init; }
    [JsonPropertyName("release_date")] public string? ReleaseDate { get; init; }
    [JsonPropertyName("belongs_to_collection")] public CollectionRef? BelongsToCollection { get; init; }
    [JsonPropertyName("external_ids")] public ExternalIds? ExternalIds { get; init; }
    [JsonPropertyName("poster_path")] public string? PosterPath { get; init; }
}

public record CollectionRef
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
}

public record ExternalIds
{
    [JsonPropertyName("imdb_id")] public string? ImdbId { get; init; }
}

public record CollectionDetails
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("parts")] public List<MovieBrief> Parts { get; init; } = new();
}

public sealed class FranchiseAgg
{
    public int CollectionId { get; set; }
    public string Name { get; set; } = "";
    public int MovieCount { get; set; }
    public double SumPopularity { get; set; }
    public int TotalVoteCount { get; set; }
    public double WeightedVoteSum { get; set; }
    public double AvgVoteWeighted { get; set; }
    public double MaxPopularity { get; set; }
    public double Score { get; set; }
}

public sealed class MemberRow
{
    public int CollectionId { get; set; }
    public string CollectionName { get; set; } = "";
    public int MovieTmdbId { get; set; }
    public string Title { get; set; } = "";
    public string? ReleaseDate { get; set; }
    public double Popularity { get; set; }
    public double VoteAverage { get; set; }
    public int VoteCount { get; set; }
    public string ImdbId { get; set; } = "";
    public string? PosterPath { get; set; }
}

// Ratings row (IMDb + optional RT)
public sealed record MovieRatingRow(
    int CollectionId,
    int MovieTmdbId,
    string ImdbId,
    double? ImdbRating100,
    int? ImdbVotes,
    string? Error,
    double? RtCriticPct = null,
    double? RtAudiencePct = null
);

public sealed class MovieJoined
{
    public int CollectionId { get; set; }
    public string CollectionName { get; set; } = "";
    public int MovieTmdbId { get; set; }
    public string ImdbId { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime? ReleaseDate { get; set; }
    public double Popularity { get; set; }
    public double TmdbVoteAverage { get; set; }
    public int TmdbVoteCount { get; set; }
    public double? ImdbRating100 { get; set; }
    public int? ImdbVotes { get; set; }
    public string? OmdbError { get; set; }
    public string? PosterPath { get; set; }

    // Rotten Tomatoes (0–100)
    public double? RtCriticPct { get; set; }
    public double? RtAudiencePct { get; set; }
}

public sealed class FranchiseRunRow
{
    public int CollectionId { get; set; }
    public string CollectionName { get; set; } = "";
    public int FilmCount { get; set; }

    // Legacy (from early code)
    public int GoodRunLength { get; set; }
    public int PeakIndex { get; set; }
    public string? PeakTitle { get; set; }
    public int? FallIndex { get; set; }
    public string? FallTitle { get; set; }
    public double? CliffDrop { get; set; }
    public double? AvgFirstN { get; set; }
    public double? AvgAll { get; set; }
    public bool HasAnyMissingRatings { get; set; }

    // NEW: streak info + per-film flags (for UI)
    public int? StreakStartIndex { get; set; }
    public int? StreakEndIndex { get; set; }
    public int StreakLength { get; set; }
    public double? StreakAvg { get; set; }
    public string? GoodIndicesCsv { get; set; } // e.g., "0;2;3"
    public int GoodThreshold { get; set; } = 70;
}

public sealed record RunAnalysis(int PeakIndex, int? FallIndex, int GoodRunLength);
