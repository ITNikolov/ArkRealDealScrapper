using System;

namespace ArkRealDealScrapper.Core.Classifieds;

public static class ClassifiedsUrlBuilder
{
    public static string Build(
        string baseUrl,
        string itemName,
        int page,
        int quality,
        int killstreakTier,
        bool tradable,
        bool craftable,
        int? australium)
    {
        string normalizedItemName = NormalizeItemName(itemName);

        string url =
            baseUrl +
            "?tradable=" + (tradable ? "1" : "0") +
            "&craftable=" + (craftable ? "1" : "0");

        // australium:
        // -1 => exclude australium
        //  1 => only australium
        // null => don't include parameter (any)
        if (australium.HasValue)
        {
            url += "&australium=" + australium.Value;
        }

        url +=
            "&page=" + page +
            "&item=" + Uri.EscapeDataString(normalizedItemName) +
            "&quality=" + quality +
            "&killstreak_tier=" + killstreakTier;

        return url;
    }

    private static string NormalizeItemName(string itemName)
    {
        string s = itemName == null ? string.Empty : itemName.Trim();

        if (s.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
        {
            s = s.Substring(4).Trim();
        }

        return s;
    }
}