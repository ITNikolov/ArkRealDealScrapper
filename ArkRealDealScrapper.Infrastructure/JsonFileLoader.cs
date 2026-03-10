using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ArkRealDealScrapper.Infrastructure;

public sealed class JsonFileLoader
{
    private readonly ILogger<JsonFileLoader> _logger;

    public JsonFileLoader(ILogger<JsonFileLoader> logger)
    {
        _logger = logger;
    }

    public async Task<T?> LoadAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _logger.LogWarning("JSON path is empty for type {Type}", typeof(T).Name);
            return default;
        }

        if (!File.Exists(path))
        {
            _logger.LogWarning("JSON file missing: {Path}", path);
            return default;
        }

        try
        {
            string json = await File.ReadAllTextAsync(path, cancellationToken);

            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            return JsonSerializer.Deserialize<T>(json, options);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Invalid JSON in file: {Path}", path);
            return default;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed reading JSON file: {Path}", path);
            return default;
        }
    }
}