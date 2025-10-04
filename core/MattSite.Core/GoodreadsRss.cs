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
    static DateTime? D(string s)
        => DateTime.TryParse(s, out var dt) ? dt : null;

    public static async Task<List<Book>> FetchAsync(HttpClient http, string rssUrl, CancellationToken ct = default)
    {
        var xml = await http.GetStringAsync(rssUrl, ct);
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
                Title: title, Author: author, Link: link, BookId: bookId,
                ImageUrl: img, SmallImageUrl: imgSmall, Shelves: shelves,
                UserRating: userRating, UserReadAt: readAt, PubDate: pubDate
            ));
        }
        return items;
    }
}
