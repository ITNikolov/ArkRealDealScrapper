using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ArkRealDealScrapper.Worker;

public sealed class SeenStore
{
    private readonly string _filePath;
    private readonly HashSet<string> _seen;

    public SeenStore(string filePath)
    {
        _filePath = filePath;
        _seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        string json = await File.ReadAllTextAsync(_filePath, cancellationToken);

        List<string>? list = JsonSerializer.Deserialize<List<string>>(json);
        if (list == null)
        {
            return;
        }

        foreach (string id in list)
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                _seen.Add(id);
            }
        }
    }

    public bool HasSeen(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        return _seen.Contains(id);
    }

    public void MarkSeen(string id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            _seen.Add(id);
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath) ?? ".");

        List<string> list = new List<string>(_seen);
        string json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });

        await File.WriteAllTextAsync(_filePath, json, cancellationToken);
    }
}