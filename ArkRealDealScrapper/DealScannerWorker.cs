using ArkRealDealScrapper.Core.Logic;
using ArkRealDealScrapper.Core.Models;
using ArkRealDealScrapper.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ArkRealDealScrapper.Worker;

public sealed class DealScannerWorker : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<ScanOptions> _scanOptionsMonitor;
    private readonly IOptionsMonitor<DiscordOptions> _discordOptionsMonitor;
    private readonly ILogger<DealScannerWorker> _logger;
    private readonly JsonFileLoader _json;

    public DealScannerWorker(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<ScanOptions> scanOptionsMonitor,
        IOptionsMonitor<DiscordOptions> discordOptionsMonitor,
        ILogger<DealScannerWorker> logger,
        JsonFileLoader json)
    {
        _httpClientFactory = httpClientFactory;
        _scanOptionsMonitor = scanOptionsMonitor;
        _discordOptionsMonitor = discordOptionsMonitor;
        _logger = logger;
        _json = json;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string baseUrl = "https://backpack.tf/classifieds";
        Random random = new Random();

        await using PlaywrightSession session = new PlaywrightSession();
        await session.InitAsync(stoppingToken);

        KeyPriceService keyPriceService = new KeyPriceService(session.BrowserContext);

        while (!stoppingToken.IsCancellationRequested)
        {
            ScanOptions scan = _scanOptionsMonitor.CurrentValue;
            DiscordOptions discord = _discordOptionsMonitor.CurrentValue;

            System.Diagnostics.Stopwatch cycleWatch = System.Diagnostics.Stopwatch.StartNew();
            int pagesScanned = 0;
            int listingsSeen = 0;
            int matchesSent = 0;

            string itemsPath = Path.Combine(AppContext.BaseDirectory, scan.ItemsFile);
            string combosPath = Path.Combine(AppContext.BaseDirectory, scan.CombosFile);

            string dataDir = Environment.GetEnvironmentVariable("DATA_DIR")
                ?? (Directory.Exists("/data") ? "/data" : AppContext.BaseDirectory);
            string seenPath = Path.Combine(dataDir, "seen.json");

            SeenStore seenStore = new SeenStore(seenPath);
            await seenStore.LoadAsync(stoppingToken);

            HttpClient httpClient = _httpClientFactory.CreateClient();
            DiscordWebhookNotifier notifier = new DiscordWebhookNotifier(
                httpClient,
                discord.WebhookUrl,
                discord.Username);

            try
            {
                List<ItemEntry> items = await _json.LoadAsync<List<ItemEntry>>(itemsPath, stoppingToken) ?? new List<ItemEntry>();
                List<DesiredCombo> combos = await _json.LoadAsync<List<DesiredCombo>>(combosPath, stoppingToken) ?? new List<DesiredCombo>();

                if (items.Count == 0)
                {
                    _logger.LogWarning("Items list is empty. Waiting for next cycle.");
                    cycleWatch.Stop();
                    _logger.LogInformation("Cycle finished. DurationMs={DurationMs} PagesScanned={Pages} ListingsSeen={Listings} MatchesSent={Matches}",
                        cycleWatch.ElapsedMilliseconds, pagesScanned, listingsSeen, matchesSent);

                    await Task.Delay(GetJitteredDelayMs(scan.CycleDelayMs, scan.JitterPercent, random), stoppingToken);
                    continue;
                }

                decimal keyPriceRef = await keyPriceService.GetKeyPriceRefAsync(stoppingToken);
                if (keyPriceRef <= 0m)
                {
                    _logger.LogWarning("KeyPriceRef is 0. Skipping this cycle.");
                    cycleWatch.Stop();
                    _logger.LogInformation("Cycle finished. DurationMs={DurationMs} PagesScanned={Pages} ListingsSeen={Listings} MatchesSent={Matches}",
                        cycleWatch.ElapsedMilliseconds, pagesScanned, listingsSeen, matchesSent);

                    await Task.Delay(GetJitteredDelayMs(scan.CycleDelayMs, scan.JitterPercent, random), stoppingToken);
                    continue;
                }

                _logger.LogInformation("New scan cycle started. KeyPriceRef={KeyPriceRef}", keyPriceRef);

                foreach (ItemEntry entry in items)
                {
                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

                    string itemName = entry.Name ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(itemName))
                    {
                        continue;
                    }

                    string queryName = string.IsNullOrWhiteSpace(entry.QueryName) ? itemName : entry.QueryName;

                    try
                    {
                        int australium = GetAustraliumValue(entry.AustraliumMode);

                        List<SellListingDetails> combined = new List<SellListingDetails>();
                        bool page1Attempted = false;
                        bool page1HasListings = false;

                        for (int page = 1; page <= scan.Pages; page++)
                        {
                            if (stoppingToken.IsCancellationRequested)
                            {
                                break;
                            }

                            try
                            {
                                await session.GetHtmlAsync(
                                    baseUrl: baseUrl,
                                    itemName: queryName,
                                    page: page,
                                    quality: 11,
                                    killstreakTier: 3,
                                    cancellationToken: stoppingToken,
                                    australium: australium);

                                List<SellListingDetails> pageListings =
                                    await session.GetSellListingsFromCurrentPageAsync(session.LastNavigatedUrl, stoppingToken);

                                _logger.LogInformation("Scan page done. Item={Item} Page={Page} SellCount={SellCount}",
                                    itemName, page, pageListings.Count);

                                if (page == 1)
                                {
                                    page1Attempted = true;
                                    page1HasListings = pageListings.Count > 0;
                                }

                                combined.AddRange(pageListings);

                                pagesScanned++;
                                listingsSeen += pageListings.Count;
                            }
                            catch (TimeoutException)
                            {
                                _logger.LogWarning("Timeout reading listings. Item={Item} Page={Page} Url={Url}",
                                    itemName, page, session.LastNavigatedUrl);

                                if (page == 1)
                                {
                                    page1Attempted = true;
                                    page1HasListings = false;
                                }

                                await Task.Delay(GetJitteredDelayMs(4000, scan.JitterPercent, random), stoppingToken);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Page scan failed. Item={Item} Page={Page} Url={Url}",
                                    itemName, page, session.LastNavigatedUrl);

                                if (page == 1)
                                {
                                    page1Attempted = true;
                                    page1HasListings = false;
                                }

                                await Task.Delay(GetJitteredDelayMs(3000, scan.JitterPercent, random), stoppingToken);
                            }

                            await Task.Delay(GetJitteredDelayMs(scan.DelayMsBetweenPages, scan.JitterPercent, random), stoppingToken);
                        }

                        if (page1Attempted && !page1HasListings)
                        {
                            _logger.LogWarning("Skipping item this cycle (page 1 had no listings). Item={Item}", itemName);
                            await Task.Delay(GetJitteredDelayMs(scan.DelayMsBetweenItems, scan.JitterPercent, random), stoppingToken);
                            continue;
                        }

                        if (combined.Count == 0)
                        {
                            await Task.Delay(GetJitteredDelayMs(scan.DelayMsBetweenItems, scan.JitterPercent, random), stoppingToken);
                            continue;
                        }

                        SellListingDetails baseline = combined[0];
                        decimal baselineTotalRef = PriceParser.ToTotalRef(baseline.PriceRaw, keyPriceRef);
                        decimal threshold = baselineTotalRef * (1m + (scan.WigglePercent / 100m));

                        int matches = 0;

                        foreach (SellListingDetails listing in combined)
                        {
                            if (stoppingToken.IsCancellationRequested)
                            {
                                break;
                            }

                            if (!IsDesiredCombo(listing, combos))
                            {
                                continue;
                            }

                            decimal listingTotalRef = PriceParser.ToTotalRef(listing.PriceRaw, keyPriceRef);
                            if (listingTotalRef <= 0m || listingTotalRef > threshold)
                            {
                                continue;
                            }

                            if (!string.IsNullOrWhiteSpace(listing.ItemAssetId) && seenStore.HasSeen(listing.ItemAssetId))
                            {
                                continue;
                            }

                            matches++;

                            try
                            {
                                await notifier.SendMatchAsync(
                                    itemName: itemName,
                                    baselinePriceRaw: baseline.PriceRaw,
                                    listing: listing,
                                    cancellationToken: stoppingToken);

                                matchesSent++;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Discord send failed. Item={Item}", itemName);
                            }

                            if (!string.IsNullOrWhiteSpace(listing.ItemAssetId))
                            {
                                seenStore.MarkSeen(listing.ItemAssetId);
                            }
                        }

                        if (matches > 0)
                        {
                            await seenStore.SaveAsync(stoppingToken);
                        }

                        _logger.LogInformation("Item scan complete. Item={Item} Matches={Matches}", itemName, matches);

                        await Task.Delay(GetJitteredDelayMs(scan.DelayMsBetweenItems, scan.JitterPercent, random), stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Item loop failed (continuing). Item={Item}", itemName);
                        await Task.Delay(GetJitteredDelayMs(3000, scan.JitterPercent, random), stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cycle failed (continuing).");
            }

            cycleWatch.Stop();
            _logger.LogInformation("Cycle finished. DurationMs={DurationMs} PagesScanned={Pages} ListingsSeen={Listings} MatchesSent={Matches}",
                cycleWatch.ElapsedMilliseconds, pagesScanned, listingsSeen, matchesSent);

            await Task.Delay(GetJitteredDelayMs(scan.CycleDelayMs, scan.JitterPercent, random), stoppingToken);
        }
    }

    private static int GetJitteredDelayMs(int baseDelayMs, int jitterPercent, Random random)
    {
        if (baseDelayMs <= 0)
        {
            return 0;
        }

        if (jitterPercent < 0)
        {
            jitterPercent = 0;
        }

        if (jitterPercent > 80)
        {
            jitterPercent = 80;
        }

        decimal fraction = jitterPercent / 100m;

        int jitter = (int)(baseDelayMs * fraction);
        int min = baseDelayMs - jitter;
        int max = baseDelayMs + jitter;

        if (min < 0)
        {
            min = 0;
        }

        return random.Next(min, max + 1);
    }

    private static int GetAustraliumValue(string? australiumMode)
    {
        if (string.Equals(australiumMode, "Only", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return -1;
    }

    private static bool IsDesiredCombo(SellListingDetails listing, List<DesiredCombo> combos)
    {
        foreach (DesiredCombo combo in combos)
        {
            bool sheenOk = string.Equals(listing.Sheen, combo.Sheen, StringComparison.OrdinalIgnoreCase);
            bool ksOk = string.Equals(listing.Killstreaker, combo.Killstreaker, StringComparison.OrdinalIgnoreCase);

            if (sheenOk && ksOk)
            {
                return true;
            }
        }

        return false;
    }

}