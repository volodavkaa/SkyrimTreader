using SkyrimTreader.Simulation;
using System.Globalization;
using System.Security.Cryptography;

if (args.Length > 0 && args[0] == "quote") return Quote(args);
if (args.Length > 0 && args[0] == "export-prices") return ExportPrices(args);
if (args.Length > 0 && args[0] == "verify-prices") return VerifyPrices(args);
if (args.Length > 0 && args[0] == "export-caravans") return ExportCaravans(args);
if (args.Length > 0 && args[0] is "trade" or "caravan-loss" or "caravan-arrival") return ApplyEvent(args);

if (args.Length < 2 || !int.TryParse(args[1], out var days) || days < 0)
{
    Console.Error.WriteLine("Usage: SkyrimTreader.Cli <config.json> <days> [state.json]");
    Console.Error.WriteLine("   or: SkyrimTreader.Cli quote <config.json> <catalog.json> <region> <FormKey> <days>");
    Console.Error.WriteLine("   or: SkyrimTreader.Cli export-prices <config.json> <catalog.json> <days> <output.json> [state.json]");
    Console.Error.WriteLine("   or: SkyrimTreader.Cli verify-prices <config.json> <catalog.json> <snapshot.json>");
    Console.Error.WriteLine("   or: SkyrimTreader.Cli export-caravans <config.json> <state.json> <output.json>");
    Console.Error.WriteLine("   or: SkyrimTreader.Cli trade <config.json> <catalog.json> <state.json> <sequence> <region> <FormKey> <netItemCount>");
    Console.Error.WriteLine("   or: SkyrimTreader.Cli caravan-loss <config.json> <state.json> <sequence> <shipmentId> <lostUnits>");
    Console.Error.WriteLine("   or: SkyrimTreader.Cli caravan-arrival <config.json> <state.json> <sequence> <shipmentId>");
    return 2;
}

try
{
    var config = EconomyJson.LoadConfig(args[0]);
    var state = args.Length > 2 && File.Exists(args[2]) ? EconomyJson.LoadState(args[2]) : null;
    var engine = new EconomyEngine(config, state, seed: 42);
    engine.AdvanceDays(days);
    Console.WriteLine($"Day {engine.State.Day}");
    Console.WriteLine("Region       Commodity    Stock  Price x  Base value");
    foreach (var (regionId, region) in config.Regions.OrderBy(x => x.Key, StringComparer.Ordinal))
    foreach (var commodity in region.Markets.Keys.OrderBy(x => x, StringComparer.Ordinal))
    {
        var stock = engine.State.Regions[regionId][commodity].Stock;
        var multiplier = engine.Multiplier(regionId, commodity).ToString("0.00", CultureInfo.InvariantCulture);
        Console.WriteLine($"{regionId,-12} {commodity,-12} {stock,5}  {multiplier,7}  {engine.RegionalBaseValue(regionId, commodity),10}");
    }
    Console.WriteLine($"Shipments: {engine.State.Shipments.Count}; in transit: {engine.State.Shipments.Count(x => x.Status is ShipmentStatus.InTransit or ShipmentStatus.Delayed or ShipmentStatus.PartiallyLost)}");
    foreach (var entry in engine.State.Journal.TakeLast(15)) Console.WriteLine(entry);
    if (args.Length > 2) EconomyJson.SaveState(args[2], engine.State);
    return 0;
}
catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException or ArgumentException or OverflowException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static int Quote(string[] arguments)
{
    if (arguments.Length != 6 || !int.TryParse(arguments[5], out var days) || days < 0)
    {
        Console.Error.WriteLine("Usage: SkyrimTreader.Cli quote <config.json> <catalog.json> <region> <FormKey> <days>");
        return 2;
    }
    try
    {
        var config = EconomyJson.LoadConfig(arguments[1]);
        var catalog = ItemCatalog.Load(arguments[2], config);
        var item = catalog.Find(arguments[4]) ?? throw new InvalidDataException($"Item {arguments[4]} not found.");
        var engine = new EconomyEngine(config, seed: 42);
        engine.AdvanceDays(days);
        var value = engine.RegionalItemValue(arguments[3], item);
        Console.WriteLine($"Day {days}: {item.EditorId ?? item.FormKey} in {arguments[3]}: base {item.BaseValue}, regional {value}, category {item.Commodity ?? "unmapped"}");
        return 0;
    }
    catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException or ArgumentException or OverflowException)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static int ExportPrices(string[] arguments)
{
    if (arguments.Length is not (5 or 6) || !int.TryParse(arguments[3], out var days) || days < 0 ||
        (arguments.Length == 6 && days != 0))
    {
        Console.Error.WriteLine("Usage: SkyrimTreader.Cli export-prices <config.json> <catalog.json> <days> <output.json> [state.json]");
        Console.Error.WriteLine("When state.json is supplied, days must be 0; advance the saved state with the simulation command first.");
        return 2;
    }
    try
    {
        var config = EconomyJson.LoadConfig(arguments[1]);
        var catalog = ItemCatalog.Load(arguments[2], config);
        var state = arguments.Length == 6 ? EconomyJson.LoadState(arguments[5]) : null;
        var engine = new EconomyEngine(config, state, seed: 42);
        engine.AdvanceDays(days);
        var catalogFingerprint = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(arguments[2])));
        var snapshot = PriceSnapshot.Create(config, engine, catalog, catalogFingerprint);
        snapshot.Save(arguments[4]);
        Console.WriteLine($"Day {snapshot.Day}: exported {snapshot.Regions.Count} regions and {snapshot.Regions.Values.Sum(x => x.Count)} price entries to {arguments[4]}.");
        return 0;
    }
    catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException or ArgumentException or OverflowException)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static int VerifyPrices(string[] arguments)
{
    if (arguments.Length != 4)
    {
        Console.Error.WriteLine("Usage: SkyrimTreader.Cli verify-prices <config.json> <catalog.json> <snapshot.json>");
        return 2;
    }
    try
    {
        var config = EconomyJson.LoadConfig(arguments[1]);
        var catalog = ItemCatalog.Load(arguments[2], config);
        var fingerprint = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(arguments[2])));
        var snapshot = PriceSnapshot.Load(arguments[3]);
        snapshot.Validate(config, catalog, fingerprint);
        Console.WriteLine($"Verified day {snapshot.Day}: {snapshot.LoadOrder.Count} plugins, {snapshot.Regions.Count} regions, {snapshot.Regions.Values.Sum(x => x.Count)} prices.");
        return 0;
    }
    catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException or ArgumentException)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static int ApplyEvent(string[] arguments)
{
    var kind = arguments[0];
    var expectedLength = kind switch
    {
        "trade" => 8,
        "caravan-loss" => 6,
        "caravan-arrival" => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(arguments))
    };
    if (arguments.Length != expectedLength)
    {
        Console.Error.WriteLine($"Invalid arguments for {kind}; run without arguments to see usage.");
        return 2;
    }
    try
    {
        var config = EconomyJson.LoadConfig(arguments[1]);
        var statePath = kind == "trade" ? arguments[3] : arguments[2];
        var engine = new EconomyEngine(config, EconomyJson.LoadState(statePath));
        var sequenceIndex = kind == "trade" ? 4 : 3;
        if (!long.TryParse(arguments[sequenceIndex], NumberStyles.None, CultureInfo.InvariantCulture, out var sequence))
            throw new ArgumentException("Invalid event sequence.");
        bool applied;
        if (kind == "trade")
        {
            if (!int.TryParse(arguments[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
                throw new ArgumentException("Invalid item count.");
            var catalog = ItemCatalog.Load(arguments[2], config);
            var item = catalog.Find(arguments[6]) ?? throw new InvalidDataException($"Item {arguments[6]} not found.");
            applied = engine.ApplyTradeItem(sequence, arguments[5], item, count);
        }
        else
        {
            if (!long.TryParse(arguments[4], NumberStyles.None, CultureInfo.InvariantCulture, out var shipmentId))
                throw new ArgumentException("Invalid shipment ID.");
            if (kind == "caravan-loss")
            {
                if (!int.TryParse(arguments[5], NumberStyles.None, CultureInfo.InvariantCulture, out var lostUnits))
                    throw new ArgumentException("Invalid lost units.");
                applied = engine.ReportCaravanLoss(sequence, shipmentId, lostUnits);
            }
            else applied = engine.ReportCaravanArrival(sequence, shipmentId);
        }
        EconomyJson.SaveState(statePath, engine.State);
        Console.WriteLine($"{kind} #{sequence}: {(applied ? "applied" : "ignored (replay or completed shipment)")}; day {engine.State.Day}.");
        return 0;
    }
    catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException or ArgumentException or OverflowException)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static int ExportCaravans(string[] arguments)
{
    if (arguments.Length != 4)
    {
        Console.Error.WriteLine("Usage: SkyrimTreader.Cli export-caravans <config.json> <state.json> <output.json>");
        return 2;
    }
    try
    {
        var config = EconomyJson.LoadConfig(arguments[1]);
        var engine = new EconomyEngine(config, EconomyJson.LoadState(arguments[2]));
        var manifest = CaravanManifest.Create(engine);
        manifest.Save(arguments[3]);
        Console.WriteLine($"Day {manifest.Day}: exported {manifest.Caravans.Count} active caravans to {arguments[3]}.");
        return 0;
    }
    catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException or ArgumentException or OverflowException)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}
