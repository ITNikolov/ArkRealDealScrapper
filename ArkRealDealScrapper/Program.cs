using ArkRealDealScrapper.Worker;
using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        using var cts = new CancellationTokenSource();
        CancellationToken ct = cts.Token;

        Console.CancelKeyPress += (sender, e) =>
        {
            Console.WriteLine("\nCancel requested...");
            cts.Cancel();
            e.Cancel = true;
        };

        await using var session = new PlaywrightSession();

        try
        {
            Console.WriteLine("Initializing browser session...");
            await session.InitAsync(ct);
            Console.WriteLine("Session initialized.");

            string baseUrl = "https://backpack.tf/classifieds";

            string itemName = "Brass Beast";
            int? quality = 11;
            int? killstreakTier = 3;
            int page = 1;

            Console.WriteLine($"Fetching: {itemName} (q:{quality}, ks:{killstreakTier}, page {page})");

            string html = await session.GetHtmlAsync(
                baseUrl: baseUrl,
                itemName: itemName,
                page: page,
                quality: quality,
                killstreakTier: killstreakTier,
                cancellationToken: ct
            );

            Console.WriteLine($"HTML length: {html.Length}");

            if (string.IsNullOrWhiteSpace(html))
            {
                Console.WriteLine("→ Got empty response");
            }
            else if (html.Contains("li.listing"))
            {
                Console.WriteLine("→ Looks like success (found li.listing)");
                await File.WriteAllTextAsync("debug_page.html", html, ct);
                Console.WriteLine("→ Saved to debug_page.html");
            }
            else if (html.Contains("Just a moment") ||
                     html.Contains("turnstile") ||
                     html.Contains("Attention Required") ||
                     html.Contains("cf-browser-verification"))
            {
                Console.WriteLine("→ Cloudflare is still blocking (visible challenge or managed page)");
            }
            else if (html.Contains("No items found"))
            {
                Console.WriteLine("→ No items found with these filters");
            }
            else
            {
                Console.WriteLine("→ Unexpected content – check debug_page.html");
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Operation canceled.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Exception:");
            Console.WriteLine(ex);
            if (ex.InnerException != null)
                Console.WriteLine("Inner: " + ex.InnerException.Message);
        }

        Console.WriteLine("\nDone. Press any key to exit...");
        Console.ReadKey(true);
    }
}