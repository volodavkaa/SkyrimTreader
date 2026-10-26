using System.Text.Json;

namespace SkyrimTreader.Simulation;

public sealed class ItemCatalog
{
    private Dictionary<string, CatalogItem>? _index;
    public int SchemaVersion { get; set; }
    public List<string> LoadOrder { get; set; } = new();
    public List<CatalogItem> Records { get; set; } = new();

    public static ItemCatalog Load(string path, EconomyConfig config)
    {
        var catalog = JsonSerializer.Deserialize(File.ReadAllText(path), EconomyJson.Context.ItemCatalog)
            ?? throw new InvalidDataException("Item catalog is empty.");
        if (catalog.SchemaVersion != 1) throw new InvalidDataException("Unsupported item catalog schema.");
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in catalog.Records)
        {
            if (!keys.Add(item.FormKey)) throw new InvalidDataException($"Duplicate catalog item {item.FormKey}.");
            if (item.Commodity is null) continue;
            if (!config.Commodities.ContainsKey(item.Commodity) || item.Units <= 0)
                throw new InvalidDataException($"Invalid commodity mapping for {item.FormKey}.");
        }
        catalog._index = catalog.Records.ToDictionary(x => x.FormKey, StringComparer.OrdinalIgnoreCase);
        return catalog;
    }

    public CatalogItem? Find(string formKey)
    {
        _index ??= Records.ToDictionary(x => x.FormKey, StringComparer.OrdinalIgnoreCase);
        return _index.GetValueOrDefault(formKey);
    }
}

public sealed class CatalogItem
{
    public string FormKey { get; set; } = "";
    public string? EditorId { get; set; }
    public string Type { get; set; } = "";
    public uint BaseValue { get; set; }
    public string WinningPlugin { get; set; } = "";
    public string? Commodity { get; set; }
    public int Units { get; set; }
}
