using System.Text.Json;

namespace SkyrimTreader.Simulation;

public sealed class PriceSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public int Day { get; set; }
    public string ConfigFingerprint { get; set; } = "";
    public string CatalogFingerprint { get; set; } = "";
    public List<string> LoadOrder { get; set; } = new();
    public Dictionary<string, Dictionary<string, PriceEntry>> Regions { get; set; } = new();

    public static PriceSnapshot Load(string path) =>
        JsonSerializer.Deserialize<PriceSnapshot>(File.ReadAllText(path), EconomyJson.Options)
        ?? throw new InvalidDataException("Price snapshot is empty.");

    public void Validate(EconomyConfig config, ItemCatalog catalog, string catalogFingerprint)
    {
        if (SchemaVersion != 1 || Day < 0)
            throw new InvalidDataException("Unsupported or invalid price snapshot.");
        if (ConfigFingerprint != EconomyJson.Fingerprint(config))
            throw new InvalidDataException("Price snapshot config fingerprint differs from current config.");
        if (!string.Equals(CatalogFingerprint, catalogFingerprint, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Price snapshot catalog fingerprint differs from current catalog.");
        if (LoadOrder is null || !LoadOrder.SequenceEqual(catalog.LoadOrder, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Price snapshot load order differs from current catalog.");
        if (Regions is null || Regions.Count != config.Regions.Count ||
            config.Regions.Keys.Any(region => !Regions.ContainsKey(region)))
            throw new InvalidDataException("Price snapshot regions differ from current config.");

        foreach (var (regionId, entries) in Regions)
        {
            if (entries is null) throw new InvalidDataException($"Region {regionId} has no price entries.");
            var expected = catalog.Records.Where(item => item.Commodity is not null &&
                config.Regions[regionId].Markets.ContainsKey(item.Commodity)).ToList();
            if (entries.Count != expected.Count)
                throw new InvalidDataException($"Region {regionId} has incomplete price entries.");
            foreach (var item in expected)
            {
                if (!entries.TryGetValue(item.FormKey, out var entry) || entry is null ||
                    entry.BaseValue != item.BaseValue || entry.Value == 0 ||
                    !string.Equals(entry.Commodity, item.Commodity, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Invalid price entry for {item.FormKey} in {regionId}.");
            }
        }
    }

    public static PriceSnapshot Create(EconomyConfig config, EconomyEngine engine, ItemCatalog catalog, string catalogFingerprint)
    {
        if (engine.State.ConfigFingerprint != EconomyJson.Fingerprint(config))
            throw new InvalidDataException("Economy engine and config do not match.");
        if (string.IsNullOrWhiteSpace(catalogFingerprint))
            throw new ArgumentException("Catalog fingerprint is required.", nameof(catalogFingerprint));

        var snapshot = new PriceSnapshot
        {
            Day = engine.State.Day,
            ConfigFingerprint = engine.State.ConfigFingerprint,
            CatalogFingerprint = catalogFingerprint,
            LoadOrder = new List<string>(catalog.LoadOrder)
        };
        foreach (var regionId in config.Regions.Keys.OrderBy(x => x, StringComparer.Ordinal))
        {
            var prices = new Dictionary<string, PriceEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in catalog.Records.Where(x => x.Commodity is not null).OrderBy(x => x.FormKey, StringComparer.Ordinal))
            {
                if (!config.Regions[regionId].Markets.ContainsKey(item.Commodity!)) continue;
                prices.Add(item.FormKey, new PriceEntry
                {
                    BaseValue = item.BaseValue,
                    Value = engine.RegionalItemValue(regionId, item),
                    Commodity = item.Commodity!
                });
            }
            snapshot.Regions.Add(regionId, prices);
        }
        return snapshot;
    }

    public void Save(string path)
    {
        var absolutePath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        var temporaryPath = absolutePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this, EconomyJson.Options));
        File.Move(temporaryPath, absolutePath, true);
    }
}

public sealed class PriceEntry
{
    public uint BaseValue { get; set; }
    public uint Value { get; set; }
    public string Commodity { get; set; } = "";
}
