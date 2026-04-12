using ArkRealDealScrapper.Core.Models;
using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ArkRealDealScrapper.Infrastructure;

public sealed class ClassifiedsListingExtractor
{
    public async Task<List<SellListingDetails>> GetSellListingsFromCurrentPageAsync(
        IPage page,
        string currentPageUrl,
        CancellationToken cancellationToken)
    {
        List<SellListingDetails> results = new List<SellListingDetails>();

        if (page == null)
        {
            return results;
        }

        string itemSelector = "div.item[data-listing_intent='sell'][data-listing_price]";

        try
        {
            await page.WaitForSelectorAsync(itemSelector, new PageWaitForSelectorOptions
            {
                Timeout = 30000,
                State = WaitForSelectorState.Attached
            });
        }
        catch (TimeoutException)
        {
            Console.WriteLine("Timeout waiting for listings. URL: " + page.Url);
            return results;
        }

        IElementHandle? noItems = await page.QuerySelectorAsync(":has-text('No items found')");
        if (noItems != null)
        {
            return results;
        }

        IReadOnlyList<IElementHandle> items = await page.QuerySelectorAllAsync(itemSelector);

        foreach (IElementHandle item in items)
        {
            SellListingDetails details = await ExtractSingleSellListingAsync(page, item, currentPageUrl);
            results.Add(details);
        }

        return results;
    }

    public async Task<SellListingDetails> GetFirstSellListingDetailsAsync(
        IPage page,
        string currentPageUrl,
        CancellationToken cancellationToken)
    {
        List<SellListingDetails> all = await GetSellListingsFromCurrentPageAsync(page, currentPageUrl, cancellationToken);

        if (all.Count == 0)
        {
            return new SellListingDetails();
        }

        return all[0];
    }

    private static async Task<SellListingDetails> ExtractSingleSellListingAsync(
        IPage page,
        IElementHandle item,
        string currentPageUrl)
    {
        string itemIconUrl = string.Empty;

        IElementHandle? iconDiv = await item.QuerySelectorAsync(".item-icon");
        if (iconDiv != null)
        {
            string style = (await iconDiv.GetAttributeAsync("style")) ?? string.Empty;
            itemIconUrl = ExtractBackgroundImageUrl(style);
        }

        string priceRaw = (await item.GetAttributeAsync("data-listing_price")) ?? string.Empty;
        string sellerName = (await item.GetAttributeAsync("data-listing_name")) ?? string.Empty;
        string sheen = (await item.GetAttributeAsync("data-sheen")) ?? string.Empty;
        string killstreaker = (await item.GetAttributeAsync("data-killstreaker")) ?? string.Empty;
        string tradeOfferUrl = (await item.GetAttributeAsync("data-listing_offers_url")) ?? string.Empty;

        string listingId = (await item.GetAttributeAsync("data-id")) ?? string.Empty;
        string appId = (await item.GetAttributeAsync("data-app")) ?? "440";

        string listingUrl = string.IsNullOrWhiteSpace(listingId)
            ? currentPageUrl
            : currentPageUrl + "#listing-" + appId + "_" + listingId;

        string steamId64 = string.Empty;

        if (!string.IsNullOrWhiteSpace(listingId))
        {
            string userSelector = "li#listing-" + appId + "_" + listingId + " span.user-handle a.user-link";
            IElementHandle? userLink = await page.QuerySelectorAsync(userSelector);

            if (userLink != null)
            {
                steamId64 = (await userLink.GetAttributeAsync("data-id")) ?? string.Empty;
            }
        }

        SellListingDetails details = new SellListingDetails
        {
            PriceRaw = priceRaw,
            SellerName = sellerName,
            SellerSteamId64 = steamId64,
            ItemIconUrl = itemIconUrl,
            Sheen = sheen,
            Killstreaker = killstreaker,
            ListingUrl = listingUrl,
            TradeOfferUrl = tradeOfferUrl,
            ItemAssetId = listingId
        };

        return details;
    }

    private static string ExtractBackgroundImageUrl(string style)
    {
        if (string.IsNullOrWhiteSpace(style))
        {
            return string.Empty;
        }

        int start = style.IndexOf("url(", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return string.Empty;
        }

        start += 4;

        int end = style.IndexOf(")", start, StringComparison.OrdinalIgnoreCase);
        if (end < 0)
        {
            return string.Empty;
        }

        string url = style.Substring(start, end - start).Trim();
        url = url.Trim('\'', '"');

        return url;
    }
}