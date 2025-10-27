using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheSequelCommittee;

public static class Tmdb
{
    public static HttpClient NewClient()
    {
        var c = new HttpClient { BaseAddress = new Uri("https://api.themoviedb.org/3/") };
        c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return c;
    }

    public static Task<DiscoverResponse?> DiscoverMoviesAsync(HttpClient http, string apiKey, int page, int voteCountMin)
        => GetJson<DiscoverResponse>(http, $"discover/movie?sort_by=popularity.desc&vote_count.gte={voteCountMin}&page={page}&api_key={Uri.EscapeDataString(apiKey)}");

    public static Task<MovieDetails?> GetMovieDetailsAsync(HttpClient http, string apiKey, int movieId)
        => GetJson<MovieDetails>(http, $"movie/{movieId}?append_to_response=external_ids&api_key={Uri.EscapeDataString(apiKey)}");

    public static Task<CollectionDetails?> GetCollectionAsync(HttpClient http, string apiKey, int collectionId)
        => GetJson<CollectionDetails>(http, $"collection/{collectionId}?api_key={Uri.EscapeDataString(apiKey)}");

    private static async Task<T?> GetJson<T>(HttpClient http, string url, int maxRetries = 4)
    {
        var delay = 500;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var resp = await http.GetAsync(url);
                if ((int)resp.StatusCode == 429)
                {
                    var retryAfter = resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromMilliseconds(delay);
                    Console.WriteLine($"  [429] Rate-limited. Retrying after {retryAfter.TotalMilliseconds:0} ms…");
                    await Task.Delay(retryAfter);
                    delay = Math.Min(delay * 2, 8000);
                    continue;
                }

                if (resp.StatusCode == HttpStatusCode.OK)
                {
                    var stream = await resp.Content.ReadAsStreamAsync();
                    return await JsonSerializer.DeserializeAsync<T>(stream, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        NumberHandling = JsonNumberHandling.AllowReadingFromString
                    });
                }

                Console.WriteLine($"  [HTTP {((int)resp.StatusCode)}] Backing off {delay} ms…");
                await Task.Delay(delay);
                delay = Math.Min(delay * 2, 8000);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [NetErr] {ex.Message}. Backing off {delay} ms…");
                await Task.Delay(delay);
                delay = Math.Min(delay * 2, 8000);
            }
        }
        return default;
    }
}
