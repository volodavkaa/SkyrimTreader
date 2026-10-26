using System.Text.Json;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using SkyrimTreader.Catalog;

if (args.Length == 3 && args[0] == "--find-locations")
{
    using var locationMod = SkyrimMod.CreateFromBinaryOverlay(Path.GetFullPath(args[1]), SkyrimRelease.SkyrimSE);
    foreach (var location in locationMod.Locations
                 .Where(x => x.EditorID?.Contains(args[2], StringComparison.OrdinalIgnoreCase) == true)
                 .OrderBy(x => x.EditorID, StringComparer.OrdinalIgnoreCase))
        Console.WriteLine($"{location.FormKey} {location.EditorID}");
    return 0;
}

if (args.Length == 3 && args[0] == "--find-globals")
{
    using var globalMod = SkyrimMod.CreateFromBinaryOverlay(Path.GetFullPath(args[1]), SkyrimRelease.SkyrimSE);
    foreach (var global in globalMod.Globals
                 .Where(x => x.EditorID?.Contains(args[2], StringComparison.OrdinalIgnoreCase) == true)
                 .OrderBy(x => x.EditorID, StringComparer.OrdinalIgnoreCase))
        Console.WriteLine($"{global.FormKey} {global.EditorID}");
    return 0;
}

if ((args.Length != 4 || args[0] != "--load-order") &&
    (args.Length != 5 || args[0] != "--mo2-profile"))
{
    Console.Error.WriteLine("Usage: SkyrimTreader.Catalog --load-order <Data folder> <plugins list> <output.json>");
    Console.Error.WriteLine("   or: SkyrimTreader.Catalog --mo2-profile <Data folder> <MO2 profile folder> <item-rules.json> <output.json>");
    Console.Error.WriteLine("   or: SkyrimTreader.Catalog --find-locations <plugin.esm> <EditorID fragment>");
    Console.Error.WriteLine("   or: SkyrimTreader.Catalog --find-globals <plugin.esm> <EditorID fragment>");
    return 2;
}

try
{
    var dataFolder = Path.GetFullPath(args[1]);
    var mo2Profile = args[0] == "--mo2-profile";
    var inputPath = Path.GetFullPath(args[2]);
    var outputPath = Path.GetFullPath(args[mo2Profile ? 4 : 3]);
    var rulesPath = mo2Profile ? Path.GetFullPath(args[3]) : Path.Combine(Path.GetDirectoryName(inputPath)!, "item-rules.json");
    var rules = JsonSerializer.Deserialize<Dictionary<string, CommodityRule>>(File.ReadAllText(rulesPath), JsonOptions())
        ?? throw new InvalidDataException("Item rules are empty.");
    if (rules.Any(x => string.IsNullOrWhiteSpace(x.Value.Commodity) || x.Value.Units <= 0))
        throw new InvalidDataException("Item rules contain an invalid commodity or unit count.");
    List<PluginSource> sources;
    if (mo2Profile)
        sources = Mo2Profile.Resolve(dataFolder, inputPath);
    else
    {
        var plugins = File.ReadAllLines(inputPath)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0 && !x.StartsWith('#'))
            .ToList();
        if (plugins.Count == 0 || plugins.Count != plugins.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            throw new InvalidDataException("Plugin list is empty or contains duplicates.");
        sources = plugins.Select(plugin => new PluginSource(plugin, Path.Combine(dataFolder, plugin))).ToList();
    }

    var winners = new Dictionary<string, CatalogRecord>(StringComparer.OrdinalIgnoreCase);
    foreach (var source in sources)
    {
        var plugin = source.Name;
        if (plugin != Path.GetFileName(plugin)) throw new InvalidDataException($"Plugin name must be a filename: {plugin}");
        if (!File.Exists(source.Path)) throw new FileNotFoundException($"Plugin missing: {plugin}", source.Path);
        using var mod = SkyrimMod.CreateFromBinaryOverlay(source.Path, SkyrimRelease.SkyrimSE);
        foreach (var item in mod.MiscItems)
            Add(winners, item.FormKey.ToString(), item.EditorID, "MISC", item.Value, plugin, item.IsDeleted);
        foreach (var item in mod.Ingestibles)
            Add(winners, item.FormKey.ToString(), item.EditorID, "ALCH", item.Value, plugin, item.IsDeleted);
        foreach (var item in mod.Weapons)
            Add(winners, item.FormKey.ToString(), item.EditorID, "WEAP", item.BasicStats?.Value ?? 0, plugin, item.IsDeleted);
        foreach (var item in mod.Armors)
            Add(winners, item.FormKey.ToString(), item.EditorID, "ARMO", item.Value, plugin, item.IsDeleted);
        Console.WriteLine($"Read {plugin}: {winners.Count} current records");
    }

    var records = winners.Values
        .Select(x => rules.TryGetValue(x.FormKey, out var rule)
            ? x with { Commodity = rule.Commodity, Units = rule.Units }
            : x)
        .OrderBy(x => x.FormKey, StringComparer.Ordinal)
        .ToList();
    var output = new CatalogOutput(1, sources.Select(x => x.Name).ToList(), records);
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    File.WriteAllText(outputPath, JsonSerializer.Serialize(output, JsonOptions()));
    Console.WriteLine($"Exported {records.Count} winning records; {records.Count(x => x.Commodity is not null)} classified.");
    return 0;
}
catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or JsonException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static void Add(Dictionary<string, CatalogRecord> winners, string key, string? editorId, string type,
    uint value, string plugin, bool deleted)
{
    if (deleted) winners.Remove(key);
    else winners[key] = new CatalogRecord(key, editorId, type, value, plugin, null, 0);
}

static JsonSerializerOptions JsonOptions() => new()
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = true
};

internal sealed record CommodityRule(string Commodity, int Units);
internal sealed record CatalogRecord(string FormKey, string? EditorId, string Type, uint BaseValue,
    string WinningPlugin, string? Commodity, int Units);
internal sealed record CatalogOutput(int SchemaVersion, List<string> LoadOrder, List<CatalogRecord> Records);
