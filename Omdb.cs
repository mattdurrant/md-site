using System.Net.Http;
using System.Text.Json;

namespace TheSequelCommittee;

public static class Omdb
{
    private static readonly HttpClient _http = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    /// <summary>
    /// Fetch ratings for the given members from OMDb, but SKIP any that already have ratings in <paramref name="existing"/>.
    /// Returns a complete list (existing + newly-fetched) for all TMDb ids present in <paramref name="members"/>.
    /// </summary>
    public static async Task<List<MovieRatingRow>> FetchRatingsAsync(
        List<MemberRow> members,
        string apiKey,
        int delayMs,
        List<MovieRatingRow>? existing = null)
    {
        // Index existing by TMDb id and IMDb id.
        var existingByTmdb = new Dictionary<int, MovieRatingRow>();
        var existingByImdb = new Dictionary<string, MovieRatingRow>(StringComparer.OrdinalIgnoreCase);

        if (existing != null)
        {
            foreach (var r in existing)
            {
                if (r.MovieTmdbId != 0 && !existingByTmdb.ContainsKey(r.MovieTmdbId))
                    existingByTmdb[r.MovieTmdbId] = r;
                if (!string.IsNullOrWhiteSpace(r.ImdbId) && !existingByImdb.ContainsKey(r.ImdbId))
                    existingByImdb[r.ImdbId] = r;
            }
        }

        // Decide which need fetching (no usable RT/IMDb data)
        bool HasUsable(MovieRatingRow r) =>
            r is not null && (
                r.RtCriticPct.HasValue ||
                r.RtAudiencePct.HasValue ||
                r.ImdbRating100.HasValue ||
                (r.ImdbVotes.HasValue && r.ImdbVotes.Value > 0)
            );

        var toFetch = new List<MemberRow>();
        foreach (var m in members)
        {
            if (existingByTmdb.TryGetValue(m.MovieTmdbId, out var ex) && HasUsable(ex))
                continue;

            if (!string.IsNullOrWhiteSpace(m.ImdbId) &&
                existingByImdb.TryGetValue(m.ImdbId!, out var ex2) && HasUsable(ex2))
            {
                // Associate this TMDb id with the existing IMDb-rated row (covers duplicates)
                existingByTmdb[m.MovieTmdbId] = ex2;
                continue;
            }

            toFetch.Add(m);
        }

        Console.WriteLine($"[OMDb] Cache: {existingByTmdb.Count} existing rows; will fetch {toFetch.Count} missing.");

        // ACCUMULATOR: overwrite on duplicates to avoid key-collision
        var acc = new Dictionary<int, MovieRatingRow>();

        // Seed with existing rows for each member we already have
        foreach (var m in members)
        {
            if (existingByTmdb.TryGetValue(m.MovieTmdbId, out var ex))
            {
                acc[m.MovieTmdbId] = MakeRow(
                    tmdbId: m.MovieTmdbId,
                    imdbId: ex.ImdbId,
                    imdb100: ex.ImdbRating100,
                    imdbVotes: ex.ImdbVotes,
                    errorMsg: null,            // we ignore any old error text
                    rtCrit: ex.RtCriticPct,
                    rtAud: ex.RtAudiencePct,
                    collectionId: TryGetCollectionId(ex)
                );
            }
        }

        // Fetch missing (stop early on request-limit)
        int iFetch = 0;
        foreach (var m in toFetch)
        {
            iFetch++;

            (MovieRatingRow row, string? errorMsg) result;

            if (!string.IsNullOrWhiteSpace(m.ImdbId))
                result = await QueryOmdbByImdbAsync(m.MovieTmdbId, m.ImdbId!, apiKey);
            else
                result = await QueryOmdbByTitleAsync(m.MovieTmdbId, m.Title, Utils.ParseDate(m.ReleaseDate)?.Year, apiKey);

            // Overwrite any prior seed for this TMDb id with the freshly fetched row
            acc[m.MovieTmdbId] = result.row;

            if (!string.IsNullOrWhiteSpace(result.errorMsg) &&
                result.errorMsg.Contains("Request limit", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[OMDb] Request limit reached; stopping fetch and keeping partial results.");
                break;
            }

            await Task.Delay(Math.Max(0, delayMs));
            if (iFetch % 25 == 0) Console.WriteLine($"[OMDb] {iFetch}/{toFetch.Count} fetched…");
        }

        // Ensure we have a row for every member (fill empty if neither existing nor fetched)
        foreach (var m in members)
        {
            if (!acc.ContainsKey(m.MovieTmdbId))
            {
                acc[m.MovieTmdbId] = MakeRow(
                    tmdbId: m.MovieTmdbId,
                    imdbId: m.ImdbId,
                    imdb100: null,
                    imdbVotes: null,
                    errorMsg: "no-data",
                    rtCrit: null,
                    rtAud: null,
                    collectionId: 0
                );
            }
        }

        return acc.Values.ToList();
    }

    private static int TryGetCollectionId(MovieRatingRow r)
    {
        // If your MovieRatingRow exposes CollectionId, use it; otherwise return 0.
        try { return (int)r.GetType().GetProperty("CollectionId")?.GetValue(r, null)!; }
        catch { return 0; }
    }

    private static MovieRatingRow MakeRow(int tmdbId, string? imdbId, double? imdb100, int? imdbVotes, string? errorMsg, double? rtCrit, double? rtAud, int collectionId)
    {
        // Matches your required ctor: (int, int, string, double?, int?, string?, double?, double?)
        return new MovieRatingRow(
            collectionId,
            tmdbId,
            imdbId ?? "",
            imdb100,
            imdbVotes,
            errorMsg,
            rtCrit,
            rtAud
        );
    }

    private static async Task<(MovieRatingRow row, string? errorMsg)> QueryOmdbByImdbAsync(int tmdbId, string imdbId, string apiKey)
    {
        var url = $"https://www.omdbapi.com/?i={Uri.EscapeDataString(imdbId)}&apikey={apiKey}";
        return await FetchAndParseAsync(tmdbId, imdbId, url);
    }

    private static async Task<(MovieRatingRow row, string? errorMsg)> QueryOmdbByTitleAsync(int tmdbId, string title, int? year, string apiKey)
    {
        var url = $"https://www.omdbapi.com/?t={Uri.EscapeDataString(title)}" +
                  (year.HasValue ? $"&y={year.Value}" : "") +
                  $"&type=movie&apikey={apiKey}";
        return await FetchAndParseAsync(tmdbId, null, url);
    }

    private static async Task<(MovieRatingRow row, string? errorMsg)> FetchAndParseAsync(int tmdbId, string? imdbIdHint, string url)
    {
        try
        {
            using var resp = await _http.GetAsync(url);
            var json = await resp.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? response = root.TryGetProperty("Response", out var resProp) ? resProp.GetString() : null;
            if (string.Equals(response, "False", StringComparison.OrdinalIgnoreCase))
            {
                string err = root.TryGetProperty("Error", out var eProp) ? eProp.GetString() ?? "Error" : "Error";
                string? imdbIdFromPayload = root.TryGetProperty("imdbID", out var idProp) ? idProp.GetString() : null;

                return (MakeRow(
                    tmdbId: tmdbId,
                    imdbId: imdbIdFromPayload ?? imdbIdHint,
                    imdb100: null,
                    imdbVotes: null,
                    errorMsg: err,
                    rtCrit: null,
                    rtAud: null,
                    collectionId: 0
                ), err);
            }

            string? imdbId = root.TryGetProperty("imdbID", out var imdbProp) ? imdbProp.GetString() : imdbIdHint;

            double? imdbRating100 = null;
            if (root.TryGetProperty("imdbRating", out var imdbRatingProp))
            {
                if (double.TryParse(imdbRatingProp.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var imdb10))
                    imdbRating100 = (imdb10 > 0) ? imdb10 * 10.0 : (double?)null;
            }

            int? imdbVotes = null;
            if (root.TryGetProperty("imdbVotes", out var votesProp))
            {
                var s = votesProp.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    s = new string(s.Where(ch => char.IsDigit(ch)).ToArray());
                    if (int.TryParse(s, out var v)) imdbVotes = v;
                }
            }

            double? rtCrit = null, rtAud = null;
            if (root.TryGetProperty("Ratings", out var ratings) && ratings.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in ratings.EnumerateArray())
                {
                    var src = r.TryGetProperty("Source", out var sProp) ? sProp.GetString() : null;
                    var val = r.TryGetProperty("Value", out var vProp) ? vProp.GetString() : null;
                    if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(val)) continue;

                    if (src!.Contains("Rotten Tomatoes", StringComparison.OrdinalIgnoreCase) && val!.EndsWith("%"))
                    {
                        if (double.TryParse(val.TrimEnd('%'), out var pct))
                            rtCrit = pct;
                    }
                    // Audience% isn't reliably available via OMDb; left null unless mapped separately.
                }
            }

            return (MakeRow(
                tmdbId: tmdbId,
                imdbId: imdbId,
                imdb100: imdbRating100,
                imdbVotes: imdbVotes,
                errorMsg: null,
                rtCrit: rtCrit,
                rtAud: rtAud,
                collectionId: 0
            ), null);
        }
        catch (Exception ex)
        {
            string err = ex.GetType().Name + ": " + ex.Message;
            return (MakeRow(
                tmdbId: tmdbId,
                imdbId: imdbIdHint,
                imdb100: null,
                imdbVotes: null,
                errorMsg: err,
                rtCrit: null,
                rtAud: null,
                collectionId: 0
            ), err);
        }
    }
}
