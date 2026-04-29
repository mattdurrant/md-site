using System.Net;
using System.Net.Http.Headers;
using System.Xml.Linq;

namespace MattSite.Core;

public static class GoodreadsRss
{
    public sealed record Book(
        string Title,
        string Author,
        string Link,
        string BookId,
        string ImageUrl,
        string SmallImageUrl,
        string Shelves,
        string UserRating,      // "0".."5"
        DateTime? UserReadAt,   // when you marked it read
        DateTime? PubDate       // RSS pubDate
    );

    static string S(XElement? e) => e?.Value?.Trim() ?? "";
    static DateTime? D(string s) => DateTime.TryParse(s, out var dt) ? dt : null;

    public static async Task<List<Book>> FetchAsync(HttpClient http, string rssUrl, CancellationToken ct = default)
    {
        // Retry a few times for transient failures (rate limiting / temporary blocks).
        // If Goodreads keeps blocking CI, we degrade gracefully (return empty list)
        // so your whole website build doesn't fail.
        const int maxAttempts = 3;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, rssUrl);

                // Goodreads (and other RSS endpoints) often block “bot-like” requests.
                // Adding a browser-ish UA is frequently enough to avoid 403 on CI.
                req.Headers.UserAgent.Clear();
                req.Headers.UserAgent.Add(new ProductInfoHeaderValue("Mozilla", "5.0"));
                req.Headers.UserAgent.Add(new ProductInfoHeaderValue("(Windows NT 10.0)"));
                req.Headers.UserAgent.Add(new ProductInfoHeaderValue("AppleWebKit", "537.36"));
                req.Headers.UserAgent.Add(new ProductInfoHeaderValue("(KHTML, like Gecko)"));
                req.Headers.UserAgent.Add(new ProductInfoHeaderValue("Chrome", "120.0"));
                req.Headers.UserAgent.Add(new ProductInfoHeaderValue("Safari", "537.36"));

                req.Headers.Accept.Clear();
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/rss+xml"));
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

                using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

                // Graceful degradation cases (don’t kill the whole build)
                if (resp.StatusCode is HttpStatusCode.Forbidden or (HttpStatusCode)429)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct);
                    Console.Error.WriteLine($"⚠️ Goodreads RSS returned {(int)resp.StatusCode} ({resp.ReasonPhrase}). Attempt {attempt}/{maxAttempts}.");
                    Console.Error.WriteLine($"⚠️ URL: {rssUrl}");
                    if (!string.IsNullOrWhiteSpace(body))
                        Console.Error.WriteLine($"⚠️ Response snippet: {body[..Math.Min(body.Length, 300)]}");

                    if (attempt < maxAttempts)
                    {
                        await BackoffAsync(attempt, ct);
                        continue;
                    }

                    // After retries: return empty list so site build still succeeds.
                    Console.Error.WriteLine("⚠️ Giving up on Goodreads for this run (returning empty list so build can continue).");
                    return new List<Book>();
                }

                resp.EnsureSuccessStatusCode();

                var xml = await resp.Content.ReadAsStringAsync(ct);
                return Parse(xml);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                Console.Error.WriteLine($"⚠️ Goodreads RSS fetch failed (attempt {attempt}/{maxAttempts}): {ex.GetType().Name}: {ex.Message}");
                await BackoffAsync(attempt, ct);
            }
            catch (Exception ex)
            {
                // Final attempt failed: degrade gracefully, don’t fail entire pipeline.
                Console.Error.WriteLine($"⚠️ Goodreads RSS fetch failed (final attempt): {ex.GetType().Name}: {ex.Message}");
                Console.Error.WriteLine("⚠️ Returning empty list so build can continue.");
                return new List<Book>();
            }
        }

        // Shouldn’t be reachable
        return new List<Book>();
    }

    private static List<Book> Parse(string xml)
    {
        var doc = XDocument.Parse(xml);

        var items = new List<Book>();
        foreach (var it in doc.Descendants("item"))
        {
            string title = S(it.Element("title"));
            string link = S(it.Element("link"));
            string author = S(it.Element("author_name"));
            string bookId = S(it.Element("book_id"));
            string img = S(it.Element("book_image_url"));
            string imgSmall = S(it.Element("book_small_image_url"));
            string shelves = S(it.Element("user_shelves"));      // comma separated
            string userRating = S(it.Element("user_rating"));
            var readAt = D(S(it.Element("user_read_at")));
            var pubDate = D(S(it.Element("pubDate")));

            items.Add(new Book(
                Title: title,
                Author: author,
                Link: link,
                BookId: bookId,
                ImageUrl: img,
                SmallImageUrl: imgSmall,
                Shelves: shelves,
                UserRating: userRating,
                UserReadAt: readAt,
                PubDate: pubDate
            ));
        }

        return items;
    }

    private static Task BackoffAsync(int attempt, CancellationToken ct)
    {
        // 1st retry ~1s, 2nd ~3s (with a little jitter)
        var baseDelayMs = attempt == 1 ? 1000 : 3000;
        var jitterMs = Random.Shared.Next(0, 400);
        return Task.Delay(baseDelayMs + jitterMs, ct);
    }
}
