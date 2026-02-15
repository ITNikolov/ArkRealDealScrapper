using ArkRealDealScrapper.Worker;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        CancellationToken cancellationToken = cancellationTokenSource.Token;

        Console.CancelKeyPress += (object? sender, ConsoleCancelEventArgs e) =>
        {
            e.Cancel = true;
            cancellationTokenSource.Cancel();
            Console.WriteLine("Cancel requested...");
        };

        await using PlaywrightSession session = new PlaywrightSession();

        Console.WriteLine("Initializing session...");
        await session.InitAsync(cancellationToken);

        string baseUrl = "https://backpack.tf/classifieds";

        string html = await session.GetHtmlAsync(
            baseUrl: baseUrl,
            itemName: "Brass Beast",
            page: 1,
            quality: 11,
            killstreakTier: 3,
            cancellationToken: cancellationToken);

        Console.WriteLine("HTML length: " + html.Length);

        if (!string.IsNullOrWhiteSpace(html))
        {
            string outPath = Path.Combine(AppContext.BaseDirectory, "debug_page.html");
            await File.WriteAllTextAsync(outPath, html, cancellationToken);
            Console.WriteLine("Saved: " + outPath);
        }

        Console.WriteLine("Done.");
    }
}
