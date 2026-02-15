using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ArkRealDealScrapper.Worker;

public sealed class PlaywrightSmokeTest : IAsyncDisposable
{
    private IPlaywright? _playwright;
    private IBrowserContext? _context;
    private IPage? _sharedPage;

    // Option B: everything next to the exe (bin\Debug\net8.0 at runtime)
    private readonly string _baseDir;
    private readonly string _profilePath;
    private readonly string _backpackCookiePath;
    private readonly string _steamCookiePath;

    public PlaywrightSmokeTest()
    {
        _baseDir = AppContext.BaseDirectory;

        _profilePath = Path.Combine(_baseDir, "playwright_profile");
        _backpackCookiePath = Path.Combine(_baseDir, "cookies.backpack.json");
        _steamCookiePath = Path.Combine(_baseDir, "cookies.steam.json");
    }

    public async Task InitAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_profilePath);

        _playwright = await Playwright.CreateAsync();

        _context = await _playwright.Chromium.LaunchPersistentContextAsync(
            _profilePath,
            new BrowserTypeLaunchPersistentContextOptions
            {
                Headless = false,
                SlowMo = 50,
                ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
            });

        // Load Steam cookies (optional)
        if (File.Exists(_steamCookiePath))
        {
            await ImportCookiesFromFileAsync(_context, _steamCookiePath, cancellationToken, "steamcommunity.com");
        }
        else
        {
            Console.WriteLine("cookies.steam.json not found (optional). Expected path: " + _steamCookiePath);
        }

        // Load Backpack cookies (optional)
        if (File.Exists(_backpackCookiePath))
        {
            await ImportCookiesFromFileAsync(_context, _backpackCookiePath, cancellationToken, "backpack.tf");
        }
        else
        {
            Console.WriteLine("cookies.backpack.json not found (optional). Expected path: " + _backpackCookiePath);
        }

        _sharedPage = await _context.NewPageAsync();
    }

    public async Task<string> FetchPageContentAsync(string url, CancellationToken cancellationToken)
    {
        if (_context == null)
        {
            Console.WriteLine("Error: Browser context is not initialized. Call InitAsync() first.");
            return string.Empty;
        }

        if (_sharedPage == null || _sharedPage.IsClosed)
        {
            _sharedPage = await _context.NewPageAsync();
        }

        try
        {
            await _sharedPage.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 60000
            });

            await WaitForContentOrManualVerifyAsync(_sharedPage, cancellationToken);

            if (_sharedPage.IsClosed)
            {
                Console.WriteLine("Page closed before reading content.");
                return string.Empty;
            }

            string title = await _sharedPage.TitleAsync();
            Console.WriteLine("Page title: " + title);

            return await _sharedPage.ContentAsync();
        }
        catch (PlaywrightException ex) when (IsTargetClosed(ex))
        {
            Console.WriteLine("Browser/page was closed. Don’t close the window while it’s running.");
            return string.Empty;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Canceled.");
            return string.Empty;
        }
        catch (PlaywrightException ex)
        {
            Console.WriteLine("Playwright error: " + ex.Message);
            return string.Empty;
        }
    }

    private static async Task ImportCookiesFromFileAsync(
        IBrowserContext context,
        string filePath,
        CancellationToken cancellationToken,
        string requiredDomainContains)
    {
        string json = await File.ReadAllTextAsync(filePath, cancellationToken);
        string trimmed = json.Trim();

        JsonSerializerOptions options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        List<CookieExport> exported = ParseCookieExports(trimmed, options);

        List<Cookie> cookiesToAdd = new List<Cookie>();

        foreach (CookieExport item in exported)
        {
            if (string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Domain))
            {
                continue;
            }

            if (item.Domain.IndexOf(requiredDomainContains, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            Cookie cookie = new Cookie
            {
                Name = item.Name,
                Value = item.Value ?? string.Empty,
                Domain = item.Domain,
                Path = string.IsNullOrWhiteSpace(item.Path) ? "/" : item.Path,
                HttpOnly = item.HttpOnly,
                Secure = item.Secure,
                Expires = item.ExpirationDate.HasValue ? (float?)item.ExpirationDate.Value : null,
                SameSite = MapSameSite(item.SameSite)
            };

            cookiesToAdd.Add(cookie);
        }

        await context.AddCookiesAsync(cookiesToAdd);

        Console.WriteLine("Cookies imported from file (" + requiredDomainContains + "): " +
            cookiesToAdd.Count.ToString(CultureInfo.InvariantCulture));
    }

    private static async Task WaitForContentOrManualVerifyAsync(IPage page, CancellationToken cancellationToken)
    {
        Task<IElementHandle?> listingsTask = page.WaitForSelectorAsync("li.listing", new PageWaitForSelectorOptions
        {
            Timeout = 15000,
            State = WaitForSelectorState.Attached
        });

        Task<IElementHandle?> noItemsTask = page.WaitForSelectorAsync(":has-text('No items found')", new PageWaitForSelectorOptions
        {
            Timeout = 15000,
            State = WaitForSelectorState.Attached
        });

        Task<IElementHandle?> verifyTask = page.WaitForSelectorAsync("text=Verify you are human", new PageWaitForSelectorOptions
        {
            Timeout = 15000,
            State = WaitForSelectorState.Attached
        });

        Task cancelTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        Task finished = await Task.WhenAny(listingsTask, noItemsTask, verifyTask, cancelTask);

        if (finished == cancelTask)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (finished == verifyTask)
        {
            Console.WriteLine("Verification detected.");
            Console.WriteLine("Solve it manually in the browser window, then press ENTER here...");
            Console.ReadLine();

            Task<IElementHandle?> listingsAfter = page.WaitForSelectorAsync("li.listing", new PageWaitForSelectorOptions
            {
                Timeout = 60000,
                State = WaitForSelectorState.Attached
            });

            Task<IElementHandle?> noItemsAfter = page.WaitForSelectorAsync(":has-text('No items found')", new PageWaitForSelectorOptions
            {
                Timeout = 60000,
                State = WaitForSelectorState.Attached
            });

            Task cancelTask2 = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            Task finished2 = await Task.WhenAny(listingsAfter, noItemsAfter, cancelTask2);

            if (finished2 == cancelTask2)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }
    }

    private static List<CookieExport> ParseCookieExports(string json, JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            List<CookieExport>? list = JsonSerializer.Deserialize<List<CookieExport>>(json, options);
            return list ?? new List<CookieExport>();
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("cookies", out JsonElement cookiesEl) &&
                cookiesEl.ValueKind == JsonValueKind.Array)
            {
                List<CookieExport>? list = JsonSerializer.Deserialize<List<CookieExport>>(cookiesEl.GetRawText(), options);
                return list ?? new List<CookieExport>();
            }

            CookieExport? single = JsonSerializer.Deserialize<CookieExport>(json, options);
            return single == null ? new List<CookieExport>() : new List<CookieExport> { single };
        }

        return new List<CookieExport>();
    }

    private static SameSiteAttribute MapSameSite(string? sameSite)
    {
        if (string.IsNullOrWhiteSpace(sameSite))
        {
            return SameSiteAttribute.Lax;
        }

        if (string.Equals(sameSite, "no_restriction", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sameSite, "none", StringComparison.OrdinalIgnoreCase))
        {
            return SameSiteAttribute.None;
        }

        if (string.Equals(sameSite, "strict", StringComparison.OrdinalIgnoreCase))
        {
            return SameSiteAttribute.Strict;
        }

        return SameSiteAttribute.Lax;
    }

    private static bool IsTargetClosed(PlaywrightException ex)
    {
        string msg = ex.Message ?? string.Empty;

        return msg.IndexOf("Target page, context or browser has been closed", StringComparison.OrdinalIgnoreCase) >= 0
            || msg.IndexOf("target closed", StringComparison.OrdinalIgnoreCase) >= 0
            || msg.IndexOf("has been closed", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public async ValueTask DisposeAsync()
    {
        if (_context != null)
        {
            await _context.CloseAsync();
            _context = null;
        }

        _sharedPage = null;

        _playwright?.Dispose();
        _playwright = null;
    }

    private sealed class CookieExport
    {
        public string? Domain { get; set; }
        public bool HttpOnly { get; set; }
        public string? Name { get; set; }
        public string? Path { get; set; }
        public string? SameSite { get; set; }
        public bool Secure { get; set; }
        public double? ExpirationDate { get; set; }
        public string? Value { get; set; }
    }
}
