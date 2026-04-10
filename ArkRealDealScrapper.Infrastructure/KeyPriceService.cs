using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace ArkRealDealScrapper.Infrastructure;

public sealed class KeyPriceService
{
    private readonly IBrowserContext _context;
    private IPage? _keyPage;

    private decimal _cachedKeyPriceRef;
    private DateTime _cachedKeyPriceUtc;

    public KeyPriceService(IBrowserContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<decimal> GetKeyPriceRefAsync(CancellationToken cancellationToken)
    {
        TimeSpan cacheTtl = TimeSpan.FromMinutes(30);

        if (_cachedKeyPriceRef > 0m && (DateTime.UtcNow - _cachedKeyPriceUtc) < cacheTtl)
        {
            return _cachedKeyPriceRef;
        }

        if (_keyPage == null || _keyPage.IsClosed)
        {
            _keyPage = await _context.NewPageAsync();
        }

        string url =
            "https://backpack.tf/classifieds" +
            "?tradable=1&craftable=1" +
            "&page=1" +
            "&item=" + Uri.EscapeDataString("Mann Co. Supply Crate Key") +
            "&quality=6";

        await _keyPage.GotoAsync(url, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 90000
        });

        await BackpackPageWaiter.WaitForContentOrManualVerifyAsync(_keyPage, cancellationToken);

        string selector = "div.item[data-listing_intent='sell'][data-listing_price]";

        try
        {
            await _keyPage.WaitForSelectorAsync(selector, new PageWaitForSelectorOptions
            {
                Timeout = 30000,
                State = WaitForSelectorState.Attached
            });
        }
        catch (TimeoutException)
        {
            Console.WriteLine("Key price timeout waiting for listings. URL: " + _keyPage.Url);
            return 0m;
        }

        IReadOnlyList<IElementHandle> items = await _keyPage.QuerySelectorAllAsync(selector);

        List<decimal> pricesRef = new List<decimal>();

        foreach (IElementHandle item in items)
        {
            if (pricesRef.Count >= 5)
            {
                break;
            }

            string raw = (await item.GetAttributeAsync("data-listing_price")) ?? string.Empty;

            decimal refOnly = ParseRefOnly(raw);
            if (refOnly > 0m)
            {
                pricesRef.Add(refOnly);
            }
        }

        if (pricesRef.Count == 0)
        {
            return 0m;
        }

        decimal sum = 0m;
        foreach (decimal p in pricesRef)
        {
            sum += p;
        }

        decimal avg = sum / pricesRef.Count;

        _cachedKeyPriceRef = avg;
        _cachedKeyPriceUtc = DateTime.UtcNow;

        Console.WriteLine("KeyPriceRef updated: " + avg.ToString(CultureInfo.InvariantCulture));

        return avg;
    }

    private static decimal ParseRefOnly(string priceRaw)
    {
        if (string.IsNullOrWhiteSpace(priceRaw))
        {
            return 0m;
        }

        string s = priceRaw.ToLowerInvariant();

        if (s.Contains("key"))
        {
            return 0m;
        }

        int refIndex = s.IndexOf("ref", StringComparison.Ordinal);
        if (refIndex < 0)
        {
            return 0m;
        }

        string numberPart = s.Substring(0, refIndex).Trim();

        decimal value;
        bool ok = decimal.TryParse(
            numberPart,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out value);

        return ok ? value : 0m;
    }
}