using System.Text.Json;
using SkyrimTreader.Simulation;

var configPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../data/mvp-economy.json"));
var config = EconomyJson.LoadConfig(configPath);
var passed = 0;

Check("Deterministic 100-day run", () =>
{
    var a = new EconomyEngine(config, seed: 42);
    var b = new EconomyEngine(config, seed: 42);
    a.AdvanceDays(100);
    b.AdvanceDays(100);
    Equal(JsonSerializer.Serialize(a.State, EconomyJson.Options), JsonSerializer.Serialize(b.State, EconomyJson.Options));
});

Check("Stock ledger and bounds over 100 days", () =>
{
    var engine = new EconomyEngine(config, seed: 42);
    var initial = engine.TotalPhysicalQuantity();
    for (var day = 0; day < 100; day++)
    {
        engine.AdvanceDay();
        var expected = initial + engine.State.TotalProduced - engine.State.TotalConsumed - engine.State.TotalLost -
            engine.State.TotalSpoiled + engine.State.TotalShockDelta;
        Equal(expected, engine.TotalPhysicalQuantity());
        foreach (var markets in engine.State.Regions.Values)
        foreach (var market in markets.Values)
        {
            True(market.Stock >= 0);
            True(market.Multiplier >= config.MinMultiplier - 0.00001);
            True(market.Multiplier <= config.MaxMultiplier + 0.00001);
        }
    }
    True(engine.State.Shipments.All(x => x.SentQuantity >= config.MinShipmentQuantity));
});

Check("Save/load does not reroll shipments", () =>
{
    var uninterrupted = new EconomyEngine(config, seed: 42);
    uninterrupted.AdvanceDays(30);
    var interrupted = new EconomyEngine(config, seed: 42);
    interrupted.AdvanceDays(9);
    var reloaded = JsonSerializer.Deserialize<SimulationState>(JsonSerializer.Serialize(interrupted.State, EconomyJson.Options), EconomyJson.Options)!;
    new EconomyEngine(config, reloaded).AdvanceDays(21);
    Equal(JsonSerializer.Serialize(uninterrupted.State, EconomyJson.Options), JsonSerializer.Serialize(reloaded, EconomyJson.Options));
});

Check("Riften shock raises food price", () =>
{
    var withShock = new EconomyEngine(config, seed: 42);
    withShock.AdvanceDays(4);
    var noShockConfig = EconomyJson.LoadConfig(configPath);
    noShockConfig.Shocks.Clear();
    var withoutShock = new EconomyEngine(noShockConfig, seed: 42);
    withoutShock.AdvanceDays(4);
    True(withShock.Multiplier("riften", "food") > withoutShock.Multiplier("riften", "food"));
});

Check("Iron can cross Whiterun to Riften", () =>
{
    var engine = new EconomyEngine(config, seed: 42);
    engine.AdvanceDays(12);
    True(engine.State.Shipments.Any(x => x.From == "whiterun" && x.To == "riften" && x.Commodity == "iron" && x.Status == ShipmentStatus.Arrived));
});

Check("Changed config cannot silently resume a save", () =>
{
    var engine = new EconomyEngine(config);
    var changed = EconomyJson.LoadConfig(configPath);
    changed.MinShipmentQuantity++;
    try
    {
        _ = new EconomyEngine(changed, engine.State);
        throw new Exception("Expected config mismatch.");
    }
    catch (InvalidDataException) { }
});

Check("Player trade changes stock but not open-menu price", () =>
{
    var engine = new EconomyEngine(config);
    var before = engine.Multiplier("riften", "iron");
    True(engine.ApplyTrade(1, "riften", "iron", 10));
    Equal(65, engine.State.Regions["riften"]["iron"].Stock);
    Equal(before, engine.Multiplier("riften", "iron"));
    Equal(10L, engine.State.TotalPlayerTradeDelta);
});

Check("Catalog item price and trade units", () =>
{
    var catalogPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/fixtures/catalog.json"));
    var catalog = ItemCatalog.Load(catalogPath, config);
    var ingot = catalog.Find("05ACE4:Skyrim.esm")!;
    var unmapped = catalog.Find("000001:Skyrim.esm")!;
    var engine = new EconomyEngine(config);
    engine.AdvanceDay();
    True(engine.RegionalItemValue("markarth", ingot) < engine.RegionalItemValue("riften", ingot));
    Equal((uint)7, engine.RegionalItemValue("riften", unmapped));
    var beforeStock = engine.State.Regions["riften"]["iron"].Stock;
    True(engine.ApplyTradeItem(1, "riften", ingot, 5));
    Equal(beforeStock + 10, engine.State.Regions["riften"]["iron"].Stock);
});

Check("Trade replay after save/load changes stock exactly once", () =>
{
    var engine = new EconomyEngine(config);
    var initial = engine.TotalPhysicalQuantity();
    True(engine.ApplyTrade(1, "riften", "iron", 10));
    var reloaded = JsonSerializer.Deserialize<SimulationState>(JsonSerializer.Serialize(engine.State, EconomyJson.Options), EconomyJson.Options)!;
    var resumed = new EconomyEngine(config, reloaded);
    True(!resumed.ApplyTrade(1, "riften", "iron", 10));
    Equal(65, resumed.State.Regions["riften"]["iron"].Stock);
    True(resumed.ApplyTrade(2, "riften", "iron", -4));
    Equal(61, resumed.State.Regions["riften"]["iron"].Stock);
    Equal(6L, resumed.State.TotalPlayerTradeDelta);
    Equal(initial + resumed.State.TotalPlayerTradeDelta, resumed.TotalPhysicalQuantity());
    try
    {
        resumed.ApplyTrade(4, "riften", "iron", 1);
        throw new Exception("Expected sequence gap to fail.");
    }
    catch (InvalidDataException) { }
});

Check("Physical caravan loss and arrival survive save/load exactly once", () =>
{
    var engine = new EconomyEngine(config, seed: 42);
    for (var i = 0; i < 20 && !engine.State.Shipments.Any(x => x.Status == ShipmentStatus.InTransit); i++)
        engine.AdvanceDay();
    var shipment = engine.State.Shipments.First(x => x.Status == ShipmentStatus.InTransit);
    var before = engine.TotalPhysicalQuantity();
    True(engine.ReportCaravanLoss(1, shipment.Id, 3));
    Equal(before - 3, engine.TotalPhysicalQuantity());
    var reloaded = JsonSerializer.Deserialize<SimulationState>(JsonSerializer.Serialize(engine.State, EconomyJson.Options), EconomyJson.Options)!;
    var resumed = new EconomyEngine(config, reloaded);
    True(!resumed.ReportCaravanLoss(1, shipment.Id, 3));
    var cargo = reloaded.Shipments.Single(x => x.Id == shipment.Id).RemainingQuantity;
    var destination = reloaded.Regions[shipment.To][shipment.Commodity];
    var stockBeforeArrival = destination.Stock;
    True(resumed.ReportCaravanArrival(2, shipment.Id));
    Equal(stockBeforeArrival + cargo, destination.Stock);
    Equal(before - 3, resumed.TotalPhysicalQuantity());
    True(!resumed.ReportCaravanArrival(2, shipment.Id));
    True(!resumed.ReportCaravanArrival(3, shipment.Id));
    Equal(stockBeforeArrival + cargo, destination.Stock);
    Equal(3L, reloaded.LastCaravanEventSequence);
    Equal(ShipmentStatus.Arrived, reloaded.Shipments.Single(x => x.Id == shipment.Id).Status);
});

Check("Price snapshot contains regional mapped items only", () =>
{
    var catalogPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/fixtures/catalog.json"));
    var catalog = ItemCatalog.Load(catalogPath, config);
    var engine = new EconomyEngine(config, seed: 42);
    engine.AdvanceDays(10);
    var snapshot = PriceSnapshot.Create(config, engine, catalog, "fixture-hash");
    Equal(10, snapshot.Day);
    Equal(3, snapshot.Regions.Count);
    Equal("fixture-hash", snapshot.CatalogFingerprint);
    Equal(EconomyJson.Fingerprint(config), snapshot.ConfigFingerprint);
    True(snapshot.Regions.Values.All(x => x.Count == 1 && x.ContainsKey("05ACE4:Skyrim.esm")));
    True(snapshot.Regions["markarth"]["05ACE4:Skyrim.esm"].Value < snapshot.Regions["riften"]["05ACE4:Skyrim.esm"].Value);
    Equal((uint)7, snapshot.Regions["markarth"]["05ACE4:Skyrim.esm"].BaseValue);
});

Check("Price snapshot rejects stale load order and item value", () =>
{
    var catalogPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/fixtures/catalog.json"));
    var catalog = ItemCatalog.Load(catalogPath, config);
    var snapshot = PriceSnapshot.Create(config, new EconomyEngine(config), catalog, "fixture-hash");
    snapshot.Validate(config, catalog, "fixture-hash");
    ThrowsInvalidSnapshot(() => snapshot.Validate(config, catalog, "different-hash"));
    snapshot.LoadOrder.Add("Unexpected.esp");
    ThrowsInvalidSnapshot(() => snapshot.Validate(config, catalog, "fixture-hash"));
    snapshot.LoadOrder.RemoveAt(snapshot.LoadOrder.Count - 1);
    snapshot.Regions["markarth"]["05ACE4:Skyrim.esm"].BaseValue++;
    ThrowsInvalidSnapshot(() => snapshot.Validate(config, catalog, "fixture-hash"));
});

Check("Corrupt saved stock and caravan cargo are rejected", () =>
{
    var engine = new EconomyEngine(config, seed: 42);
    engine.AdvanceDay();
    var saved = JsonSerializer.Serialize(engine.State, EconomyJson.Options);

    var badStock = JsonSerializer.Deserialize<SimulationState>(saved, EconomyJson.Options)!;
    badStock.Regions["riften"]["iron"].Stock++;
    ThrowsInvalidState(badStock);

    var badCargo = JsonSerializer.Deserialize<SimulationState>(saved, EconomyJson.Options)!;
    var shipment = badCargo.Shipments.First();
    shipment.RemainingQuantity = shipment.SentQuantity + 1;
    ThrowsInvalidState(badCargo);

    var duplicateId = JsonSerializer.Deserialize<SimulationState>(saved, EconomyJson.Options)!;
    duplicateId.Shipments.Add(JsonSerializer.Deserialize<Shipment>(
        JsonSerializer.Serialize(duplicateId.Shipments.First(), EconomyJson.Options), EconomyJson.Options)!);
    ThrowsInvalidState(duplicateId);
});

Check("Caravan manifest follows active cargo and stable shipment IDs", () =>
{
    var engine = new EconomyEngine(config, seed: 42);
    engine.AdvanceDay();
    var shipment = engine.State.Shipments.First();
    True(engine.ReportCaravanLoss(1, shipment.Id, 3));
    var manifest = CaravanManifest.Create(engine);
    Equal(engine.State.Day, manifest.Day);
    Equal(engine.State.ConfigFingerprint, manifest.ConfigFingerprint);
    Equal(engine.State.Shipments.Count(x => x.Status is ShipmentStatus.InTransit or ShipmentStatus.Delayed or ShipmentStatus.PartiallyLost), manifest.Caravans.Count);
    var entry = manifest.Caravans.Single(x => x.ShipmentId == shipment.Id);
    Equal(shipment.RemainingQuantity, entry.RemainingQuantity);
    Equal(ShipmentStatus.PartiallyLost, entry.Status);
    True(engine.ReportCaravanArrival(2, shipment.Id));
    True(CaravanManifest.Create(engine).Caravans.All(x => x.ShipmentId != shipment.Id));
});

Check("Invalid non-finite economy settings are rejected", () =>
{
    var invalid = EconomyJson.LoadConfig(configPath);
    invalid.Routes[0].Risk = double.NaN;
    try
    {
        _ = new EconomyEngine(invalid);
        throw new Exception("Expected invalid route risk to fail.");
    }
    catch (InvalidDataException) { }
});

Console.WriteLine($"{passed} checks passed.");
return 0;

void Check(string name, Action action)
{
    action();
    passed++;
    Console.WriteLine($"PASS {name}");
}

void True(bool condition)
{
    if (!condition) throw new Exception("Assertion failed.");
}

void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected {expected}; got {actual}.");
}

void ThrowsInvalidState(SimulationState state)
{
    try
    {
        _ = new EconomyEngine(config, state);
        throw new Exception("Expected invalid saved state to fail.");
    }
    catch (InvalidDataException) { }
}

void ThrowsInvalidSnapshot(Action action)
{
    try
    {
        action();
        throw new Exception("Expected invalid price snapshot to fail.");
    }
    catch (InvalidDataException) { }
}
