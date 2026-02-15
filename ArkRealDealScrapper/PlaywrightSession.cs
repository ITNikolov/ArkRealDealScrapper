using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace ArkRealDealScrapper.Worker;

public sealed class PlaywrightSession : IAsyncDisposable
{
    private IPlaywright? _playwright;
    private IBrowserContext? _context;
    private IPage? _page;

    private readonly string _baseDir;
    private readonly string _userDataDir;
    private readonly string _steamCookiePath;
    private readonly string _backpackCookiePath;

    // REPLACE THIS EVERY TIME IT EXPIRES (usually 1–7 days)
    private const string CfClearanceValue =
        "Sexoy5eVSqpNJ81uagy_rJH9BVEWBrEzKM6E_rCrdZ0-1770997613-1.2.1.1-wZGmNq.GeaV9X0p55CedSteT.UsW1xcizE9xNTXu51H3X14s70rcy6dNxZFKW2nv6sJzZS6Ub1rsjDuk_KjQtDRLL6_w7RIUoyLZA_Hfej8I1faj55xsC9IY5ysB0ftE98VIVCf1VYWVnYnDUGtJt658Dh5oZ3ta0p3_W1kbvnWFZ1YIftwz.5tC5214y86pVz7rtYL5.Br2CqmSiGnN2WXhLkqX0ptN50FiYVtprwU";

    public PlaywrightSession()
    {
        _baseDir = AppContext.BaseDirectory;
        _userDataDir = Path.Combine(_baseDir, "playwright_profile");
        _steamCookiePath = Path.Combine(_baseDir, "cookies.steam.json");
        _backpackCookiePath = Path.Combine(_baseDir, "cookies.backpack.json");
    }

    public async Task InitAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_userDataDir);

        _playwright = await Playwright.CreateAsync();

        _context = await _playwright.Chromium.LaunchPersistentContextAsync(
            _userDataDir,
            new BrowserTypeLaunchPersistentContextOptions
            {
                Headless = false,
                SlowMo = 50,
                ViewportSize = new ViewportSize { Width = 1280, Height = 720 },
                Args = new[]
                {
                    "--disable-blink-features=AutomationControlled",
                    "--disable-infobars",
                    "--window-size=1280,720",
                    "--disable-dev-shm-usage"
                },
                IgnoreDefaultArgs = new[] { "--enable-automation" }
            });

        await TryLoadCookiesAsync(_steamCookiePath, "steamcommunity.com", ct);
        await TryLoadCookiesAsync(_backpackCookiePath, "backpack.tf", ct);

        if (!string.IsNullOrWhiteSpace(CfClearanceValue) &&
            CfClearanceValue != "YOUR_CF_CLEARANCE_VALUE_HERE")
        {
            await _context.AddCookiesAsync(new[]
            {
                new Cookie
                {
                    Name = "cf_clearance",
                    Value = CfClearanceValue,
                    Domain = "backpack.tf",
                    Path = "/"
                }
            });
            Console.WriteLine("→ Added cf_clearance cookie");
        }
        else
        {
            Console.WriteLine("WARNING: cf_clearance is not set!");
        }

        // Debug cookies
        var cookies = await _context.CookiesAsync(["https://backpack.tf"]);
        Console.WriteLine($"backpack.tf cookies count: {cookies.Count}");
        foreach (var c in cookies)
            Console.WriteLine($"  • {c.Name,-22} {c.Domain,-18} {c.Path}");

        _page = await _context.NewPageAsync();
    }

    public async Task<string> GetHtmlAsync(
        string baseUrl,
        string itemName = "",
        int page = 1,
        int? quality = null,
        int? killstreakTier = null,
        CancellationToken cancellationToken = default)
    {
        if (_page == null || _page.IsClosed)
            _page = await _context!.NewPageAsync();

        try
        {
            var query = new List<string> { "tradable=1", "craftable=1", "australium=-1", $"page={page}" };

            if (!string.IsNullOrWhiteSpace(itemName))
                query.Add($"item={Uri.EscapeDataString(StripLeadingThe(itemName.Trim()))}");

            if (quality.HasValue) query.Add($"quality={quality.Value}");
            if (killstreakTier.HasValue) query.Add($"killstreak_tier={killstreakTier.Value}");

            string url = $"{baseUrl}?{string.Join("&", query)}";
            Console.WriteLine($"→ Navigating: {url}");

            var response = await _page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 90000
            });

            if (response?.Ok != true)
            {
                Console.WriteLine($"  HTTP → {response?.Status} {response?.StatusText}");
            }

            await Task.Delay(3500, cancellationToken);     // Cloudflare needs time
            await WaitForContentOrManualVerifyAsync(_page, cancellationToken);
            await Task.Delay(1500, cancellationToken);

            string html = await _page.ContentAsync();

            if (html.Contains("turnstile") || html.Contains("Just a moment") ||
                html.Contains("Attention Required") || html.Contains("cf-browser-verification"))
            {
                Console.WriteLine("→ Cloudflare protection page detected");
            }

            return html;
        }
        catch (Exception ex)
        {
            Console.WriteLine("GetHtmlAsync failed:");
            Console.WriteLine(ex.Message);
            if (ex.InnerException != null)
                Console.WriteLine("Inner: " + ex.InnerException.Message);
            return "";
        }
    }

    private async Task TryLoadCookiesAsync(string path, string domainHint, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"Cookie file missing: {path}");
            return;
        }

        List<Cookie> cookies;

        try
        {
            cookies = await CookieFileLoader.LoadCookiesAsync(path, domainHint, ct);
        }
        catch
        {
            Console.WriteLine($"Loader failed → falling back to manual parse: {path}");
            cookies = await ImportCookiesFromFileAsync(path);
        }

        Console.WriteLine($"Loaded {cookies.Count} cookies from {Path.GetFileName(path)}");

        if (cookies.Count > 0)
            await _context!.AddCookiesAsync(cookies);
    }

    private static async Task<List<Cookie>> ImportCookiesFromFileAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        var doc = JsonDocument.Parse(json);
        var list = new List<Cookie>();

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            list.Add(new Cookie
            {
                Name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                Value = item.TryGetProperty("value", out var v) ? v.GetString() ?? "" : "",
                Domain = item.TryGetProperty("domain", out var d) ? d.GetString() ?? "" : "",
                Path = item.TryGetProperty("path", out var p) ? p.GetString() ?? "/" : "/"
            });
        }

        return list;
    }

    private static async Task WaitForContentOrManualVerifyAsync(IPage page, CancellationToken ct)
    {
        var listings = page.WaitForSelectorAsync("li.listing", new() { Timeout = 20000 });
        var noItems = page.WaitForSelectorAsync(":has-text('No items found')", new() { Timeout = 20000 });
        var verify = page.WaitForSelectorAsync("text=/verify|human|turnstile|just a moment/i", new() { Timeout = 20000 });

        var finished = await Task.WhenAny(listings, noItems, verify, Task.Delay(Timeout.Infinite, ct));

        if (finished == verify)
        {
            Console.WriteLine("\n!!! VERIFICATION DETECTED !!!");
            Console.WriteLine("Solve the challenge in the browser, then press ENTER here...");
            Console.ReadLine();
            Console.WriteLine("Continuing after manual solve...\n");

            await Task.WhenAny(
                page.WaitForSelectorAsync("li.listing", new() { Timeout = 60000 }),
                page.WaitForSelectorAsync(":has-text('No items found')", new() { Timeout = 60000 })
            );
        }
    }

    private static string StripLeadingThe(string s)
    {
        s = s?.Trim() ?? "";
        return s.StartsWith("The ", StringComparison.OrdinalIgnoreCase)
            ? s[4..].Trim()
            : s;
    }

    public async ValueTask DisposeAsync()
    {
        if (_context != null)
        {
            await _context.CloseAsync().ContinueWith(_ => { });
            _context = null;
        }
        _page = null;
        _playwright?.Dispose();
        _playwright = null;
    }
}