using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ArkRealDealScrapper.Worker;

public static class CookieFileLoader
{
    public static async Task<List<Cookie>> LoadCookiesAsync(
        string filePath,
        string? requiredDomainContains,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return new List<Cookie>();
        }

        string json = await File.ReadAllTextAsync(filePath, cancellationToken);
        string trimmed = json.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return new List<Cookie>();
        }

        JsonSerializerOptions options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        List<CookieExport> exported = DeserializeCookieExports(trimmed, options);

        List<Cookie> cookies = new List<Cookie>();

        foreach (CookieExport item in exported)
        {
            if (string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Domain))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(requiredDomainContains))
            {
                if (item.Domain.IndexOf(requiredDomainContains, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
            }

            Cookie cookie = new Cookie
            {
                Name = item.Name,
                Value = item.Value ?? string.Empty,
                Domain = item.Domain,
                Path = string.IsNullOrWhiteSpace(item.Path) ? "/" : item.Path,
                HttpOnly = item.HttpOnly,
                Secure = item.Secure,
                Expires = item.ExpirationDate.HasValue ? (float?)item.ExpirationDate.Value : null,
                SameSite = MapSameSite(item.SameSite)
            };

            cookies.Add(cookie);
        }

        return cookies;
    }

    // Supports:
    // 1) [ {cookie}, {cookie} ]
    // 2) {cookie}
    // 3) { "cookies": [ {cookie} ] }
    private static List<CookieExport> DeserializeCookieExports(string json, JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            List<CookieExport>? list = JsonSerializer.Deserialize<List<CookieExport>>(json, options);
            return list ?? new List<CookieExport>();
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("cookies", out JsonElement cookiesEl) &&
                cookiesEl.ValueKind == JsonValueKind.Array)
            {
                List<CookieExport>? list = JsonSerializer.Deserialize<List<CookieExport>>(cookiesEl.GetRawText(), options);
                return list ?? new List<CookieExport>();
            }

            CookieExport? single = JsonSerializer.Deserialize<CookieExport>(json, options);
            return single == null ? new List<CookieExport>() : new List<CookieExport> { single };
        }

        return new List<CookieExport>();
    }

    private static SameSiteAttribute MapSameSite(string? sameSite)
    {
        if (string.IsNullOrWhiteSpace(sameSite))
        {
            return SameSiteAttribute.Lax;
        }

        if (string.Equals(sameSite, "no_restriction", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sameSite, "none", StringComparison.OrdinalIgnoreCase))
        {
            return SameSiteAttribute.None;
        }

        if (string.Equals(sameSite, "strict", StringComparison.OrdinalIgnoreCase))
        {
            return SameSiteAttribute.Strict;
        }

        return SameSiteAttribute.Lax;
    }

    private sealed class CookieExport
    {
        public string? Domain { get; set; }
        public bool HttpOnly { get; set; }
        public string? Name { get; set; }
        public string? Path { get; set; }
        public string? SameSite { get; set; }
        public bool Secure { get; set; }
        public double? ExpirationDate { get; set; }
        public string? Value { get; set; }

        // These exist in some exports but we ignore them safely:
        public bool? HostOnly { get; set; }
        public bool? Session { get; set; }
        public string? StoreId { get; set; }
    }
}
