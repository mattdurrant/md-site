using MattSite.Core;
using System.Text;

namespace Hello.Worker;

internal class Program
{
    static async Task<int> Main()
    {
        try
        {
            var outputBase = Environment.GetEnvironmentVariable("OUTPUT_DIR") ?? "out";
            var outDir = Path.Combine(outputBase, "hello");
            Directory.CreateDirectory(outDir);

            var body = new StringBuilder()
                .Append("<p>This is a placeholder page to prove the pipeline.</p>")
                .Append("<p>We’ll add Strava / Goodreads / Imgur workers next.</p>")
                .ToString();

            var html = Html.Page("Hello", body);
            await File.WriteAllTextAsync(Path.Combine(outDir, "index.html"), html, Encoding.UTF8);

            Console.WriteLine($"Wrote {Path.Combine(outDir, "index.html")}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("❌ " + ex.Message);
            return 1;
        }
    }
}
    