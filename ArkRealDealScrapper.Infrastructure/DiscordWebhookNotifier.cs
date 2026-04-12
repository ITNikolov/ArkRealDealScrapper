using ArkRealDealScrapper.Core.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ArkRealDealScrapper.Infrastructure;

public sealed class DiscordWebhookNotifier
{
    private readonly HttpClient _httpClient;
    private readonly string _webhookUrl;
    private readonly string _username;

    public DiscordWebhookNotifier(HttpClient httpClient, string webhookUrl, string username)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _webhookUrl = webhookUrl ?? string.Empty;
        _username = string.IsNullOrWhiteSpace(username) ? "Trade Bot" : username;
    }

    public async Task SendMatchAsync(
        string itemName,
        string baselinePriceRaw,
        SellListingDetails listing,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_webhookUrl))
        {
            Console.WriteLine("Discord webhook URL is empty. Skipping notify.");
            return;
        }

        if (listing == null)
        {
            Console.WriteLine("Listing is null. Skipping notify.");
            return;
        }

        string sellerMarkdown = string.IsNullOrWhiteSpace(listing.SteamProfileUrl)
            ? listing.SellerName
            : "[" + listing.SellerName + "](" + listing.SteamProfileUrl + ")";

        string quickLinks =
            "[Backpack.tf Listing](" + listing.ListingUrl + ")\n" +
            (string.IsNullOrWhiteSpace(listing.TradeOfferUrl) ? string.Empty : "[Send Trade Offer](" + listing.TradeOfferUrl + ")\n") +
            (string.IsNullOrWhiteSpace(listing.SteamInventoryItemUrl) ? string.Empty : "[Steam Inventory](" + listing.SteamInventoryItemUrl + ")\n") +
            (string.IsNullOrWhiteSpace(listing.BackpackProfileUrl) ? string.Empty : "[Backpack Profile](" + listing.BackpackProfileUrl + ")\n");

        DiscordEmbedThumbnail? thumbnail = null;

        if (!string.IsNullOrWhiteSpace(listing.ItemIconUrl))
        {
            thumbnail = new DiscordEmbedThumbnail
            {
                Url = listing.ItemIconUrl
            };
        }

        DiscordWebhookPayload payload = new DiscordWebhookPayload
        {
            Username = _username,
            Embeds = new List<DiscordEmbed>
            {
                new DiscordEmbed
                {
                    Title = "New TF2 match",
                    Description =
                        "**Item:** " + itemName + "\n" +
                        "**Matched:** killstreak combo",
                    Url = listing.ListingUrl,
                    Thumbnail = thumbnail,
                    Fields = new List<DiscordField>
                    {
                        new DiscordField
                        {
                            Name = "KILLSTREAK",
                            Value =
                                "Killstreaker: " + listing.Killstreaker + "\n" +
                                "Sheen: " + listing.Sheen,
                            Inline = false
                        },
                        new DiscordField
                        {
                            Name = "Listed for",
                            Value = listing.PriceRaw,
                            Inline = true
                        },
                        new DiscordField
                        {
                            Name = "Lowest listing atm",
                            Value = baselinePriceRaw,
                            Inline = true
                        },
                        new DiscordField
                        {
                            Name = "SELLER",
                            Value = sellerMarkdown,
                            Inline = false
                        },
                        new DiscordField
                        {
                            Name = "Quick links",
                            Value = quickLinks.Trim(),
                            Inline = false
                        }
                    }
                }
            }
        };

        JsonSerializerOptions jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        string json = JsonSerializer.Serialize(payload, jsonOptions);

        using StringContent content = new StringContent(json, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _httpClient.PostAsync(_webhookUrl, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            Console.WriteLine("Discord webhook failed: " + (int)response.StatusCode + " " + response.ReasonPhrase);
            Console.WriteLine(body);
        }
    }

    private sealed class DiscordWebhookPayload
    {
        public string Username { get; set; } = "Trade Bot";
        public List<DiscordEmbed> Embeds { get; set; } = new List<DiscordEmbed>();
    }

    private sealed class DiscordEmbed
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;

        public DiscordEmbedThumbnail? Thumbnail { get; set; }

        public List<DiscordField> Fields { get; set; } = new List<DiscordField>();
    }

    private sealed class DiscordEmbedThumbnail
    {
        public string Url { get; set; } = string.Empty;
    }

    private sealed class DiscordField
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public bool Inline { get; set; }
    }
}