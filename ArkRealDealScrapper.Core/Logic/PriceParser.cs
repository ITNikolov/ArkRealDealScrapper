using System;
using System.Globalization;

namespace ArkRealDealScrapper.Core.Logic;

public static class PriceParser
{
    public static decimal ToTotalRef(string priceRaw, decimal keyPriceRef)
    {
        if (string.IsNullOrWhiteSpace(priceRaw))
        {
            return 0m;
        }

        string s = priceRaw.ToLowerInvariant();

        int keys = 0;
        decimal refValue = 0m;

        // keys
        int keyIndex = s.IndexOf("key", StringComparison.Ordinal);
        if (keyIndex >= 0)
        {
            string beforeKey = s.Substring(0, keyIndex).Trim();
            int commaIndex = beforeKey.IndexOf(",", StringComparison.Ordinal);
            if (commaIndex >= 0)
            {
                beforeKey = beforeKey.Substring(0, commaIndex).Trim();
            }

            string keyNumber = beforeKey.Trim();
            int.TryParse(keyNumber, NumberStyles.Any, CultureInfo.InvariantCulture, out keys);
        }

        // ref
        int refIndex = s.IndexOf("ref", StringComparison.Ordinal);
        if (refIndex >= 0)
        {
            int commaIndex = s.IndexOf(",", StringComparison.Ordinal);
            string refPart = commaIndex >= 0 ? s.Substring(commaIndex + 1) : s;

            int refWordIndex = refPart.IndexOf("ref", StringComparison.Ordinal);
            if (refWordIndex >= 0)
            {
                refPart = refPart.Substring(0, refWordIndex);
            }

            refPart = refPart.Replace("keys", string.Empty).Replace("key", string.Empty).Trim();

            decimal.TryParse(refPart, NumberStyles.Any, CultureInfo.InvariantCulture, out refValue);
        }

        decimal total = (keys * keyPriceRef) + refValue;
        return total;
    }
}