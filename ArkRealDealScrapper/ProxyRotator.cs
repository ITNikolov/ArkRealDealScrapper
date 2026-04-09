using System;
using System.Collections.Generic;

namespace ArkRealDealScrapper.Worker;

/// <summary>
/// Parses the PROXIES environment variable (one proxy per line, format: host:port:user:pass)
/// and provides proxy URLs for Playwright in http://user:pass@host:port format.
/// </summary>
public sealed class ProxyRotator
{
    private readonly List<string> _proxyUrls;
    private int _currentIndex;

    public bool HasProxies => _proxyUrls.Count > 0;

    public ProxyRotator()
    {
        _proxyUrls = new List<string>();

        string? raw = Environment.GetEnvironmentVariable("PROXIES");
        if (string.IsNullOrWhiteSpace(raw))
        {
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
                Console.WriteLine("Skipping invalid proxy entry: " + trimmed);
                continue;
            }

            string host = parts[0].Trim();
            string port = parts[1].Trim();
            string user = parts[2].Trim();
            string pass = parts[3].Trim();

            string url = $"http://{user}:{pass}@{host}:{port}";
            _proxyUrls.Add(url);
        }

        // Shuffle so different deployments start on different proxies
        Shuffle(_proxyUrls);

        Console.WriteLine($"Loaded {_proxyUrls.Count} proxies.");
    }

    public string Current => _proxyUrls.Count > 0
        ? _proxyUrls[_currentIndex % _proxyUrls.Count]
        : string.Empty;

    public string Rotate()
    {
        if (_proxyUrls.Count == 0)
        {
            return string.Empty;
        }

        _currentIndex = (_currentIndex + 1) % _proxyUrls.Count;
        Console.WriteLine($"Rotated to proxy index {_currentIndex}.");
        return Current;
    }

    private static void Shuffle(List<string> list)
    {
        Random rng = new Random();
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
