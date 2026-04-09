using System;
using System.Collections.Generic;

namespace ArkRealDealScrapper.Worker;

public sealed class ProxyEntry
{
    public string Server { get; init; } = string.Empty;   // http://host:port
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
}

/// <summary>
/// Parses the PROXIES environment variable (one proxy per line, format: host:port:user:pass)
/// and provides structured proxy entries for Playwright.
/// </summary>
public sealed class ProxyRotator
{
    private readonly List<ProxyEntry> _proxies;
    private int _currentIndex;

    public bool HasProxies => _proxies.Count > 0;

    public ProxyRotator()
    {
        _proxies = new List<ProxyEntry>();

        string? raw = Environment.GetEnvironmentVariable("PROXIES");
        if (string.IsNullOrWhiteSpace(raw))
        {
            Console.WriteLine("No PROXIES env var set — running without proxy.");
            return;
        }

        string[] lines = raw.Split(new[] { '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            // Expected format: host:port:user:pass
            string[] parts = trimmed.Split(':');
            if (parts.Length != 4)
            {
                Console.WriteLine("Skipping invalid proxy entry (expected host:port:user:pass): " + trimmed);
                continue;
            }

            _proxies.Add(new ProxyEntry
            {
                Server = $"http://{parts[0].Trim()}:{parts[1].Trim()}",
                Username = parts[2].Trim(),
                Password = parts[3].Trim(),
                DisplayName = $"{parts[0].Trim()}:{parts[1].Trim()}"
            });
        }

        Shuffle(_proxies);
        Console.WriteLine($"Loaded {_proxies.Count} proxies.");
    }

    public ProxyEntry? Current => _proxies.Count > 0
        ? _proxies[_currentIndex % _proxies.Count]
        : null;

    public ProxyEntry? Rotate()
    {
        if (_proxies.Count == 0)
        {
            return null;
        }

        _currentIndex = (_currentIndex + 1) % _proxies.Count;
        Console.WriteLine($"Rotated to proxy {_proxies[_currentIndex].DisplayName} (index {_currentIndex}).");
        return Current;
    }

    private static void Shuffle<T>(List<T> list)
    {
        Random rng = new Random();
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
