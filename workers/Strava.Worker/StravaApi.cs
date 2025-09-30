using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MattSite.Core;

public static class StravaApi
{
    public sealed record Activity(
        long Id,
        string Name,
        [property: JsonPropertyName("sport_type")] string SportType,
        [property: JsonPropertyName("type")] string LegacyType,
        [property: JsonPropertyName("start_date_local")] DateTime StartDateLocal,
        double Distance,                 // meters
        [property: JsonPropertyName("moving_time")] int MovingTime,   // seconds
        [property: JsonPropertyName("elapsed_time")] int ElapsedTime, // seconds
        [property: JsonPropertyName("total_elevation_gain")] double Elevation  // meters
    );

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<string> GetAccessTokenAsync(HttpClient http, string clientId, string clientSecret, string refreshToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://www.strava.com/oauth/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            })
        };

        var res = await http.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Strava token refresh failed: {(int)res.StatusCode} {res.ReasonPhrase}\n{body}");

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("access_token", out var tok))
            throw new InvalidOperationException($"Strava token response missing access_token.\n{body}");
        return tok.GetString()!;
    }

    public static async Task<List<Activity>> GetRecentActivitiesAsync(HttpClient http, string accessToken, int perPage = 50, int page = 1)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://www.strava.com/api/v3/athlete/activities?per_page={perPage}&page={page}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var res = await http.SendAsync(req);
        var json = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Strava activities failed: {(int)res.StatusCode} {res.ReasonPhrase}\n{json}");

        var list = System.Text.Json.JsonSerializer.Deserialize<List<Activity>>(json, Json) ?? new();
        return list;
    }

    public static string ActivityUrl(long id) => $"https://www.strava.com/activities/{id}";
    public static DateTime ToUk(DateTime localFromApi)  // Strava gives start_date_local in *local* of your account; treat as local then show UK
    {
        // The API provides "start_date_local" without tz info; we treat it as local and convert to Europe/London for display consistency
        var unspecified = DateTime.SpecifyKind(localFromApi, DateTimeKind.Unspecified);
        try { return TimeZoneInfo.ConvertTime(unspecified, TimeZoneInfo.FindSystemTimeZoneById("Europe/London")); }
        catch { return unspecified; }
    }
}
