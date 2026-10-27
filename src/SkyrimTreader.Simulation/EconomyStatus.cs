using System.Text.Json;

namespace SkyrimTreader.Simulation;

public sealed class EconomyStatus
{
    public int SchemaVersion { get; set; } = 1;
    public int Day { get; set; }
    public string ConfigFingerprint { get; set; } = "";
    public double PriceIntensity { get; set; } = 1;
    public bool VisibleCaravansEnabled { get; set; } = true;
    public Dictionary<string, Dictionary<string, MarketStatus>> Regions { get; set; } = new();
    public List<CaravanManifestEntry> Caravans { get; set; } = new();
    public VisibleCaravanBinding? VisibleCaravan { get; set; }
    public List<string> RecentEvents { get; set; } = new();

    public static EconomyStatus Create(EconomyConfig config, EconomyEngine engine)
    {
        var status = new EconomyStatus
        {
            Day = engine.State.Day,
            ConfigFingerprint = engine.State.ConfigFingerprint,
            Caravans = CaravanManifest.Create(engine).Caravans,
            VisibleCaravan = engine.State.VisibleCaravan,
            RecentEvents = engine.State.Journal.TakeLast(20).ToList()
        };
        foreach (var (regionId, region) in config.Regions.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var markets = new Dictionary<string, MarketStatus>();
            foreach (var (commodity, rule) in region.Markets.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var current = engine.State.Regions[regionId][commodity];
                markets[commodity] = new MarketStatus
                {
                    Stock = current.Stock,
                    TargetStock = rule.TargetStock,
                    PriceMultiplier = current.Multiplier,
                    RegionalBaseValue = engine.RegionalBaseValue(regionId, commodity)
                };
            }
            status.Regions[regionId] = markets;
        }
        return status;
    }

    public void Save(string path)
    {
        var absolutePath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        var temporaryPath = absolutePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this, EconomyJson.Context.EconomyStatus));
        File.Move(temporaryPath, absolutePath, true);
    }
}

public sealed class MarketStatus
{
    public int Stock { get; set; }
    public int TargetStock { get; set; }
    public double PriceMultiplier { get; set; }
    public int RegionalBaseValue { get; set; }
}
