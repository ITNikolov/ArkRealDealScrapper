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

            Console.WriteLine("\n!!! VERIFICATION DETECTED !!!");
            Console.WriteLine("Solve the challenge in the browser, then press ENTER here...");
            Console.ReadLine();
            Console.WriteLine("Continuing after manual solve...\n");

            PageWaitForSelectorOptions longWait = new PageWaitForSelectorOptions
            {
                Timeout = 60000
            };

            await Task.WhenAny(
                page.WaitForSelectorAsync("li.listing, div.item[data-listing_price]", longWait),
                page.WaitForSelectorAsync(":has-text('No items found')", longWait));
        }
    }
}