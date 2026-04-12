namespace ArkRealDealScrapper.Worker;

public sealed class ScanOptions
{
    public int Pages { get; set; } = 2;
    public decimal WigglePercent { get; set; } = 10m;
    public int JitterPercent { get; set; } = 35;

    public int DelayMsBetweenPages { get; set; } = 1000;
    public int DelayMsBetweenItems { get; set; } = 3000;

    public int CycleDelayMs { get; set; } = 150000; // <- how long to wait before scanning again

    public string ItemsFile { get; set; } = "data/items.json";
    public string CombosFile { get; set; } = "data/combos.json";
    public string SeenFile { get; set; } = "data/seen.json";
}