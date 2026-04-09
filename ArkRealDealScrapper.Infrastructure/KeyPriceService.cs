using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ArkRealDealScrapper.Infrastructure;

/// <summary>
/// Fetches the Mann Co. Supply Crate Key price in ref via the backpack.tf IGetCurrencies API.
/// No browser / Cloudflare involvement — a plain HTTPS call.
/// </summary>
public sealed class KeyPriceService
{
    private static readonly HttpClient _http = new HttpClient();

    private decimal _cachedKeyPriceRef;
    private DateTime _cachedKeyPriceUtc;

    private static string ApiKey =>
        Environment.GetEnvironmentVariable("BACKPACK_TF_API_KEY") ?? string.Empty;

    public async Task<decimal> GetKeyPriceRefAsync(CancellationToken cancellationToken)
    {
        TimeSpan cacheTtl = TimeSpan.FromMinutes(30);

        if (_cachedKeyPriceRef > 0m && (DateTime.UtcNow - _cachedKeyPriceUtc) < cacheTtl)
        {
            return _cachedKeyPriceRef;
        }

        // KEY_PRICE_REF env var lets you pin the real market price (e.g. "57.94").
        // The backpack.tf IGetCurrencies API returns the community suggestion price
        // which can lag the actual trading price by weeks. Set this in Railway and
        // update it when key prices shift significantly.
        string? pinned = Environment.GetEnvironmentVariable("KEY_PRICE_REF");
        if (!string.IsNullOrWhiteSpace(pinned) &&
            decimal.TryParse(pinned, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal pinnedPrice) &&
            pinnedPrice > 0m)
        {
            Console.WriteLine($"KeyPriceRef from env var KEY_PRICE_REF: {pinnedPrice}");
            _cachedKeyPriceRef = pinnedPrice;
            _cachedKeyPriceUtc = DateTime.UtcNow;
            return pinnedPrice;
        }

        string url = $"https://backpack.tf/api/IGetCurrencies/v1?key={ApiKey}";

        HttpResponseMessage response = await _http.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"IGetCurrencies failed: HTTP {(int)response.StatusCode}");
            return _cachedKeyPriceRef > 0m ? _cachedKeyPriceRef : 0m;
        }

        string json = await response.Content.ReadAsStringAsync(cancellationToken);

        decimal price = ParseKeyPriceRef(json);

        if (price > 0m)
        {
            _cachedKeyPriceRef = price;
            _cachedKeyPriceUtc = DateTime.UtcNow;
            Console.WriteLine("KeyPriceRef updated: " + price.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            Console.WriteLine("Could not parse key price from IGetCurrencies response.");
        }

        return _cachedKeyPriceRef > 0m ? _cachedKeyPriceRef : price;
    }

    private static decimal ParseKeyPriceRef(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            // Response shape: { "response": { "currencies": { "keys": { "price": { "value": 53.77 } } } } }
            JsonElement currencies = root
                .GetProperty("response")
                .GetProperty("currencies");

            if (!currencies.TryGetProperty("keys", out JsonElement keys))
            {
                return 0m;
            }

            JsonElement price = keys.GetProperty("price");

            // value is in ref when currency == "metal", otherwise it's a key count
            if (price.TryGetProperty("currency", out JsonElement currencyEl) &&
                currencyEl.GetString() == "metal" &&
                price.TryGetProperty("value", out JsonElement valueEl))
            {
                return valueEl.GetDecimal();
            }

            // Fallback: try value directly (may already be in ref)
            if (price.TryGetProperty("value", out JsonElement fallback))
            {
                return fallback.GetDecimal();
            }

            return 0m;
        }
        catch (Exception ex)
        {
            Console.WriteLine("ParseKeyPriceRef error: " + ex.Message);
            return 0m;
        }
    }
}
