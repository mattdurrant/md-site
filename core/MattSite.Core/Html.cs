using System.Text;

namespace MattSite.Core;

public static class Html
{
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
</style>
</head><body class=""albums-page""><div class=""container"">
<header>
  <div class=""site-nav""><a href=""https://www.mattdurrant.com/"">← Home</a></div>
  <h1>").Append(E(title)).Append(@"</h1>");
        if (!string.IsNullOrWhiteSpace(navHtml)) sb.Append(navHtml);
        sb.Append("</header><main>").Append(bodyHtml).Append("</main>");
        sb.Append(@"<div class=""footer"">Last updated: ")
          .Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'"))
          .Append("</div></div></body></html>");
        return sb.ToString();
    }

    public static string E(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
