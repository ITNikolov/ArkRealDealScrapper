namespace ArkRealDealScrapper.Core.Models;

public sealed class ItemEntry
{
    public string Name { get; set; } = string.Empty;

    public string QueryName { get; set; } = string.Empty;

    public string AustraliumMode { get; set; } = "Exclude";
}