using Microsoft.Playwright;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArkRealDealScrapper.Infrastructure;

public static class BackpackPageWaiter
{
    public static async Task WaitForContentOrManualVerifyAsync(IPage page, CancellationToken ct)
    {
        IElementHandle? alreadyHasListings = await page.QuerySelectorAsync("li.listing, div.item[data-listing_price]");
        if (alreadyHasListings != null)
        {
            return;
        }

        PageWaitForSelectorOptions shortWait = new PageWaitForSelectorOptions
        {
            Timeout = 20000
        };

        Task<IElementHandle?> listingsTask = page.WaitForSelectorAsync("li.listing, div.item[data-listing_price]", shortWait);
        Task<IElementHandle?> noItemsTask = page.WaitForSelectorAsync(":has-text('No items found')", shortWait);

        Task<IElementHandle?> cfTask = page.WaitForSelectorAsync(
            "text=/performing security verification|verify you are human|attention required|just a moment/i," +
            "input[name='cf-turnstile-response']," +
            "iframe[title*='Cloudflare']," +
            "#challenge-form," +
            ".cf-turnstile",
            shortWait);

        Task finished = await Task.WhenAny(
            listingsTask,
            noItemsTask,
            cfTask,
            Task.Delay(Timeout.Infinite, ct));

        if (finished == cfTask)
        {
            IElementHandle? hasListingsNow = await page.QuerySelectorAsync("li.listing, div.item[data-listing_price]");
            IElementHandle? hasNoItemsNow = await page.QuerySelectorAsync(":has-text('No items found')");

            if (hasListingsNow != null || hasNoItemsNow != null)
            {
                return;
            }

            // Cloudflare JS challenge detected — with a real browser on a residential proxy
            // the challenge should auto-solve. Wait up to 40 seconds for the browser to pass it.
            Console.WriteLine($"Cloudflare challenge detected on: {page.Url}");
            Console.WriteLine("Waiting up to 40s for browser to auto-solve...");

            PageWaitForSelectorOptions autoSolveWait = new PageWaitForSelectorOptions
            {
                Timeout = 40000
            };

            Task<IElementHandle?> solvedListings = page.WaitForSelectorAsync("li.listing, div.item[data-listing_price]", autoSolveWait);
            Task<IElementHandle?> solvedNoItems = page.WaitForSelectorAsync(":has-text('No items found')", autoSolveWait);

            Task solveResult = await Task.WhenAny(solvedListings, solvedNoItems, Task.Delay(40000, ct));

            if (solveResult == solvedListings || solveResult == solvedNoItems)
            {
                // Let any post-challenge redirects and cookie writes fully settle
                await Task.Delay(3000, ct);
                Console.WriteLine($"Cloudflare challenge auto-solved. Final URL: {page.Url}");
            }
            else
            {
                Console.WriteLine($"Cloudflare challenge not solved within 40s — proxy may be blocked. URL: {page.Url}");
            }
        }
    }
}