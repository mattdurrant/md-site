namespace ImgurAuthTool;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

internal class Program
{
    static async Task Main()
    {
        var clientId = Env("IMGUR_CLIENT_ID");
        var clientSecret = Env("IMGUR_CLIENT_SECRET");
        var redirect = "http://localhost:53684/callback/"; // note trailing slash

        var authUrl =
            "https://api.imgur.com/oauth2/authorize" +
            $"?client_id={Uri.EscapeDataString(clientId)}" +
            "&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(redirect)}" +
            "&state=md-site&scope=read";

        Console.WriteLine("Opening browser for Imgur consent…");
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(authUrl) { UseShellExecute = true }); }
        catch { Console.WriteLine(authUrl); }

        using var listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:53684/callback/");
        listener.Start();
        Console.WriteLine("Waiting for Imgur callback…");

        var ctx = await listener.GetContextAsync();
        var code = ctx.Request.QueryString["code"];
        using (var w = new StreamWriter(ctx.Response.OutputStream))
            await w.WriteAsync("<h1>✅ Imgur auth complete. You can close this window.</h1>");
        ctx.Response.Close();
        listener.Stop();

        if (string.IsNullOrWhiteSpace(code)) { Console.WriteLine("No code received."); return; }

        using var http = new HttpClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.imgur.com/oauth2/token");
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["grant_type"] = "authorization_code",
            ["code"] = code!,
            ["redirect_uri"] = redirect
        });

        var res = await http.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();
        Console.WriteLine("Raw token response:\n" + body);
        if (!res.IsSuccessStatusCode) return;

        using var doc = JsonDocument.Parse(body);
        var refresh = doc.RootElement.GetProperty("refresh_token").GetString();
        Console.WriteLine("\n🔑 REFRESH TOKEN (save this as a GitHub secret):\n" + refresh);
    }

    private static string Env(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(v)) throw new InvalidOperationException($"Missing env: {name}");
        return v;
    }
}
