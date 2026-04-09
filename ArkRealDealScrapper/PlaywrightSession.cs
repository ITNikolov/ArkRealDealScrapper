using ArkRealDealScrapper.Core.Classifieds;
using ArkRealDealScrapper.Core.Models;
using ArkRealDealScrapper.Infrastructure;
using Microsoft.Playwright;


namespace ArkRealDealScrapper.Worker;

public sealed class PlaywrightSession : IAsyncDisposable
{
    private IPlaywright? _playwright;
    private IBrowserContext? _context;
    private IPage? _page;

    private readonly string _baseDir;
    private readonly string _userDataDir;
    private readonly string _backpackCookiePath;
    private readonly ClassifiedsListingExtractor _listingExtractor;
    public string LastNavigatedUrl { get; private set; } = string.Empty;
    public IBrowserContext BrowserContext
    {
        get
        {
            if (_context == null)
            {
                throw new InvalidOperationException("PlaywrightSession is not initialized.");
            }

            return _context;
        }
    }

    // Set the CF_CLEARANCE environment variable in Railway (expires every 1–7 days).
    // Get a fresh value from your browser's DevTools → Application → Cookies → backpack.tf → cf_clearance
    private static string CfClearanceValue =>
        Environment.GetEnvironmentVariable("CF_CLEARANCE") ?? string.Empty;

    private string _proxyUrl;

    public PlaywrightSession(string proxyUrl = "")
    {
        _listingExtractor = new ClassifiedsListingExtractor();
        _proxyUrl = proxyUrl;

        _baseDir = AppContext.BaseDirectory;
        _userDataDir = Path.Combine(_baseDir, "playwright_profile");
        _backpackCookiePath = Path.Combine(_baseDir, "playwright_profile", "cookies.backpack.json");
    }

    public async Task InitAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_userDataDir);

        _playwright = await Playwright.CreateAsync();

        var contextOptions = new BrowserTypeLaunchPersistentContextOptions
        {
            Headless = false,
            SlowMo = 50,
            ViewportSize = new ViewportSize { Width = 1280, Height = 720 },
            Args = new[]
            {
                "--no-sandbox",
                "--disable-setuid-sandbox",
                "--disable-gpu",
                "--disable-blink-features=AutomationControlled",
                "--disable-infobars",
                "--window-size=1280,720",
                "--disable-dev-shm-usage"
            },
            IgnoreDefaultArgs = new[] { "--enable-automation" }
        };

        if (!string.IsNullOrWhiteSpace(_proxyUrl))
        {
            contextOptions.Proxy = new Proxy { Server = _proxyUrl };
            Console.WriteLine("Using proxy: " + MaskProxy(_proxyUrl));
        }

        _context = await _playwright.Chromium.LaunchPersistentContextAsync(
            _userDataDir,
            contextOptions);

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

        _page = await _context.NewPageAsync();
    }

    public async Task<SellListingDetails> GetFirstSellListingDetailsAsync(
    string currentPageUrl,
    CancellationToken cancellationToken)
    {
        if (_page == null)
        {
            return new SellListingDetails();
        }

        return await _listingExtractor.GetFirstSellListingDetailsAsync(_page, currentPageUrl, cancellationToken);
    }

    public async Task<List<SellListingDetails>> GetSellListingsFromCurrentPageAsync(
    string currentPageUrl,
    CancellationToken cancellationToken)
    {
        List<SellListingDetails> results = new List<SellListingDetails>();

        if (_page == null)
        {
            return results;
        }

        return await _listingExtractor.GetSellListingsFromCurrentPageAsync(_page, currentPageUrl, cancellationToken);
    }

    public async Task<string> GetHtmlAsync(
    string baseUrl,
    string itemName = "",
    int page = 1,
    int? quality = null,
    int? killstreakTier = null,
    CancellationToken cancellationToken = default,
    int? australium = -1)
    {
        if (_context != null && (_page == null || _page.IsClosed))
        {
            _page = await _context.NewPageAsync();
        }

        if (_page == null)
        {
            return string.Empty;
        }

        try
        {
            int resolvedQuality = quality.HasValue ? quality.Value : 11;
            int resolvedKillstreakTier = killstreakTier.HasValue ? killstreakTier.Value : 3;

            string url = ClassifiedsUrlBuilder.Build(
            baseUrl: baseUrl,
            itemName: itemName,
            page: page,
            quality: resolvedQuality,
            killstreakTier: resolvedKillstreakTier,
            tradable: true,
            craftable: true,
            australium: australium);

            Console.WriteLine("→ Navigating: " + url);

            IResponse? response = await _page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 90000
            });

            LastNavigatedUrl = _page.Url;

            if (response != null && response.Ok != true)
            {
                Console.WriteLine("  HTTP → " + response.Status + " " + response.StatusText);
            }

            // Instead of fixed multi-second delays, rely on the real content wait.
            await BackpackPageWaiter.WaitForContentOrManualVerifyAsync(_page, cancellationToken);

            // Small settle (optional) so attributes are definitely present
            await Task.Delay(300, cancellationToken);

            string html = await _page.ContentAsync();

            if (html.Contains("turnstile", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("just a moment", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("attention required", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("cf-browser-verification", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("→ Protection page detected");
            }

            return html;
        }
        catch (Exception ex)
        {
            Console.WriteLine("GetHtmlAsync failed:");
            Console.WriteLine(ex.Message);

            if (ex.InnerException != null)
            {
                Console.WriteLine("Inner: " + ex.InnerException.Message);
            }

            return string.Empty;
        }
    }

    private async Task TryLoadCookiesAsync(string path, string domainHint, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine("Cookie file missing: " + path);
            return;
        }

        List<Cookie> cookies = await CookieFileLoader.LoadCookiesAsync(path, domainHint, ct);

        Console.WriteLine("Loaded " + cookies.Count + " cookies from " + Path.GetFileName(path));

        if (_context != null && cookies.Count > 0)
        {
            await _context.AddCookiesAsync(cookies);
        }
    }

    public async Task ReinitAsync(string newProxyUrl, CancellationToken ct = default)
    {
        if (_context != null)
        {
            await _context.CloseAsync().ContinueWith(_ => { });
            _context = null;
        }

        _playwright?.Dispose();
        _playwright = null;
        _page = null;

        _proxyUrl = newProxyUrl;
        await InitAsync(ct);
    }

    private static string MaskProxy(string url)
    {
        // Show host only, hide credentials
        try
        {
            Uri uri = new Uri(url);
            return uri.Host + ":" + uri.Port;
        }
        catch
        {
            return "(proxy)";
        }
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