using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace MattSite.Core;

public static class DropboxApi
{
    public sealed record DbxFile(
        string Id,
        string Name,
        string PathLower,
        long Size,
        DateTime ClientModified,
        string LinkUrl,   // preview link
        string RawUrl     // direct/raw for <img src=>
    );

    public static async Task<string> GetAccessTokenAsync(HttpClient http, string appKey, string appSecret, string refreshToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.dropbox.com/oauth2/token");
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        });
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{appKey}:{appSecret}"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        var res = await http.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dropbox token failed: {(int)res.StatusCode} {res.ReasonPhrase}\n{body}");

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }

    public static async IAsyncEnumerable<DbxFile> ListImagesWithLinksAsync(
        HttpClient http, string accessToken, string folderPath, Action<string>? log = null)
    {
        static bool IsImage(string name)
        {
            var n = name.ToLowerInvariant();
            return n.EndsWith(".jpg") || n.EndsWith(".jpeg") || n.EndsWith(".png") ||
                   n.EndsWith(".gif") || n.EndsWith(".webp");
        }

        async Task<(string? cursor, List<JsonElement> items)> ListPageAsync(string? cursor)
        {
            HttpRequestMessage req;
            if (cursor is null)
            {
                req = new HttpRequestMessage(HttpMethod.Post, "https://api.dropboxapi.com/2/files/list_folder")
                {
                    Content = JsonContent.Create(new
                    {
                        path = folderPath,
                        recursive = true, // include subfolders
                        include_non_downloadable_files = false
                    })
                };
            }
            else
            {
                req = new HttpRequestMessage(HttpMethod.Post, "https://api.dropboxapi.com/2/files/list_folder/continue")
                {
                    Content = JsonContent.Create(new { cursor })
                };
            }
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var res = await http.SendAsync(req);
            var json = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException($"Dropbox list_folder failed: {(int)res.StatusCode} {res.ReasonPhrase}\n{json}");

            using var doc = JsonDocument.Parse(json);
            var hasMore = doc.RootElement.GetProperty("has_more").GetBoolean();
            var nextCursor = doc.RootElement.TryGetProperty("cursor", out var c) ? c.GetString() : null;

            // clone elements so they survive after doc is disposed
            var items = new List<JsonElement>();
            foreach (var e in doc.RootElement.GetProperty("entries").EnumerateArray())
                items.Add(e.Clone());

            return (hasMore ? nextCursor : null, items);
        }

        async Task<string?> GetExistingSharedLinkAsync(string pathLower)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.dropboxapi.com/2/sharing/list_shared_links")
            {
                Content = JsonContent.Create(new { path = pathLower, direct_only = true })
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var res = await http.SendAsync(req);
            var json = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(json);
            foreach (var l in doc.RootElement.GetProperty("links").EnumerateArray())
                return l.GetProperty("url").GetString();
            return null;
        }

        async Task<string?> CreateSharedLinkAsync(string pathLower)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.dropboxapi.com/2/sharing/create_shared_link_with_settings")
            {
                Content = JsonContent.Create(new { path = pathLower, settings = new { requested_visibility = "public" } })
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var res = await http.SendAsync(req);
            var json = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("url").GetString();
        }

        static string ToRaw(string sharedUrl)
        {
            // Convert ...?dl=0|1 to ...?raw=1 so it can be used in <img src=...>
            var u = sharedUrl.Replace("?dl=0", "").Replace("?dl=1", "");
            if (u.Contains('?')) return u + "&raw=1";
            return u + "?raw=1";
        }

        string? cursor = null;
        do
        {
            var (next, items) = await ListPageAsync(cursor);
            cursor = next;

            foreach (var it in items)
            {
                var tag = it.GetProperty(".tag").GetString();
                if (tag != "file") continue;

                var name = it.GetProperty("name").GetString() ?? "";
                if (!IsImage(name)) continue;

                var id = it.GetProperty("id").GetString() ?? "";
                var pathLower = it.GetProperty("path_lower").GetString() ?? "";
                var size = it.GetProperty("size").GetInt64();
                var clientMod = it.GetProperty("client_modified").GetDateTime();

                var link = await GetExistingSharedLinkAsync(pathLower) ?? await CreateSharedLinkAsync(pathLower);
                if (string.IsNullOrWhiteSpace(link)) { log?.Invoke($"No link for {name}"); continue; }

                yield return new DbxFile(
                    Id: id,
                    Name: name,
                    PathLower: pathLower,
                    Size: size,
                    ClientModified: clientMod,
                    LinkUrl: link,
                    RawUrl: ToRaw(link)
                );
            }
        } while (cursor is not null);
    }
}
