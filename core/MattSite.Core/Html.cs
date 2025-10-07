using System.Text;

namespace MattSite.Core;

public static class Html
{
    private static string DefaultNav() => @"
<nav class=""top-nav"">
  <a href=""/"">Home</a>
  <span>·</span>
  <a href=""/albums/"">Albums</a>
  <span>·</span>
  <a href=""/ebay/"">eBay</a>
  <span>·</span>
  <a href=""/photos/"">Photos</a>
  <span>·</span>
  <a href=""/books/"">Books</a>
  <span>·</span>
  <a href=""/strava/"">Strava</a>
</nav>";


    public static string Page(string title, string bodyHtml, string? navHtml = null)
    {
        var sb = new StringBuilder();
        sb.Append(@"<!doctype html><html lang=""en""><head><meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>").Append(E(title)).Append(@"</title>
<link rel=""stylesheet"" type=""text/css"" href=""https://www.mattdurrant.com/styles.css"">
<link rel=""stylesheet"" type=""text/css"" href=""https://www.mattdurrant.com/albums.css"">
<style>
      .site-nav{margin:8px 0;font-size:.95em}
      .site-nav a{text-decoration:none}
      .container{max-width:900px;margin:0 auto;padding:16px}
    .top-nav{display:flex;gap:.6rem;flex-wrap:wrap;margin:6px 0 16px}
    .top-nav a{text-decoration:none}
    .top-nav span{color:#999}

</style>
</head><body class=""albums-page""><div class=""container"">
<header>
  <div class=""site-nav""><a href=""https://www.mattdurrant.com/"">← Home</a></div>
  <h1>").Append(E(title)).Append(@"</h1>");

        sb.Append(!string.IsNullOrWhiteSpace(navHtml) ? navHtml : DefaultNav());

        sb.Append("</header><main>").Append(bodyHtml).Append("</main>");
        sb.Append(@"<div class=""footer"">Last updated: ")
          .Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'"))
          .Append("</div></div></body></html>");
        return sb.ToString();
    }

 

    public static string E(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
