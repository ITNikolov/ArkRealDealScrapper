namespace ArkRealDealScrapper.Core.Models;

public sealed class SellListingDetails
{

    public string PriceRaw { get; set; } = string.Empty;
    public string SellerName { get; set; } = string.Empty;
    public string SellerSteamId64 { get; set; } = string.Empty;
    public string ItemIconUrl { get; set; } = string.Empty;

    public string Sheen { get; set; } = string.Empty;
    public string Killstreaker { get; set; } = string.Empty;

    public string ListingUrl { get; set; } = string.Empty;
    public string TradeOfferUrl { get; set; } = string.Empty;
    public string ItemAssetId { get; set; } = string.Empty;

    public string SteamProfileUrl
    {
        get
        {
            return string.IsNullOrWhiteSpace(SellerSteamId64)
                ? string.Empty
                : "https://steamcommunity.com/profiles/" + SellerSteamId64;
        }
    }

    public string SteamInventoryItemUrl
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SellerSteamId64) || string.IsNullOrWhiteSpace(ItemAssetId))
            {
                return string.Empty;
            }

            return "https://steamcommunity.com/profiles/" + SellerSteamId64 + "/inventory/#440_2_" + ItemAssetId;
        }
    }

    public string BackpackProfileUrl
    {
        get
        {
            return string.IsNullOrWhiteSpace(SellerSteamId64)
                ? string.Empty
                : "https://backpack.tf/u/" + SellerSteamId64;
        }
    }
}