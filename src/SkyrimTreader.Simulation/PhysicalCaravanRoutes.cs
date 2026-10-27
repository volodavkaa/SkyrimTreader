using System.Text.Json;

namespace SkyrimTreader.Simulation;

public sealed class PhysicalCaravanRoutes
{
    public int SchemaVersion { get; set; } = 1;
    public List<PhysicalCaravanRoute> Routes { get; set; } = new();
    public List<PhysicalCargoItem> CargoItems { get; set; } = new();

    public static PhysicalCaravanRoutes Load(string path, EconomyConfig economy, ItemCatalog catalog)
    {
        var routes = JsonSerializer.Deserialize(File.ReadAllText(path), EconomyJson.Context.PhysicalCaravanRoutes)
            ?? throw new InvalidDataException("Physical caravan routes are empty.");
        routes.Validate(economy, catalog);
        return routes;
    }

    public void Validate(EconomyConfig economy, ItemCatalog catalog)
    {
        if (SchemaVersion != 1 || Routes is null || CargoItems is null)
            throw new InvalidDataException("Invalid physical caravan route schema.");
        var knownRoutes = economy.Routes.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var routeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var route in Routes)
        {
            if (route is null || string.IsNullOrWhiteSpace(route.RouteId) ||
                !routeIds.Add(route.RouteId) || !knownRoutes.ContainsKey(route.RouteId))
                throw new InvalidDataException($"Invalid physical route {route?.RouteId}.");
            if (!route.Enabled) continue;
            if (!route.NavmeshVerified || !IsFormKey(route.Worldspace) ||
                route.Anchors is null || route.Anchors.Count < 2 ||
                route.Anchors.Any(x => !IsFormKey(x)) ||
                route.Anchors.Distinct(StringComparer.OrdinalIgnoreCase).Count() != route.Anchors.Count ||
                !IsFormKey(route.SpawnMarker) || !IsFormKey(route.RetireMarker) ||
                route.MinPlayerDistance <= 0 || route.MaxPlayerDistance <= route.MinPlayerDistance ||
                route.StuckTimeoutSeconds < 10)
                throw new InvalidDataException($"Physical route {route.RouteId} lacks verified road data.");
        }
        var cargoKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cargo in CargoItems)
        {
            var catalogItem = cargo is null ? null : catalog.Find(cargo.FormKey);
            if (cargo is null || !IsFormKey(cargo.FormKey) || !cargoKeys.Add(cargo.FormKey) ||
                catalogItem is null || catalogItem.Commodity != cargo.Commodity ||
                catalogItem.Units != cargo.Units || cargo.Units < 1)
                throw new InvalidDataException($"Invalid physical cargo item {cargo?.FormKey}.");
        }
        if (Routes.Any(x => x.Enabled) && CargoItems.Count == 0)
            throw new InvalidDataException("Enabled physical routes need at least one cargo mapping.");
    }

    public HashSet<string> EnabledRouteIds() =>
        Routes.Where(x => x.Enabled).Select(x => x.RouteId).ToHashSet(StringComparer.Ordinal);

    public HashSet<string> SupportedCommodities() =>
        CargoItems.Select(x => x.Commodity).ToHashSet(StringComparer.Ordinal);

    public PhysicalCaravanPlan? Plan(EconomyEngine engine, string playerRegion, bool visibleEnabled = true)
    {
        var shipment = engine.PlanVisibleCaravan(playerRegion, visibleEnabled,
            EnabledRouteIds(), SupportedCommodities());
        if (shipment is null) return null;
        var route = Routes.SingleOrDefault(x => x.RouteId == shipment.RouteId && x.Enabled);
        var cargo = new List<PhysicalCargoStack>();
        var unrepresented = shipment.RemainingQuantity;
        if (shipment.Action != VisibleCaravanAction.Retire && route is not null)
        {
            foreach (var item in CargoItems.Where(x => x.Commodity == shipment.Commodity)
                         .OrderByDescending(x => x.Units).ThenBy(x => x.FormKey, StringComparer.OrdinalIgnoreCase))
            {
                var count = unrepresented / item.Units;
                if (count == 0) continue;
                cargo.Add(new PhysicalCargoStack { FormKey = item.FormKey, Count = count, UnitsEach = item.Units });
                unrepresented -= checked(count * item.Units);
            }
        }
        return new PhysicalCaravanPlan
        {
            Shipment = shipment, Route = route, Cargo = cargo, UnrepresentedUnits = unrepresented
        };
    }

    private static bool IsFormKey(string? value)
    {
        if (value is null || value.Length < 11 || value[6] != ':' || value[7..].Trim().Length == 0) return false;
        return value[..6].All(Uri.IsHexDigit);
    }
}

public sealed class PhysicalCaravanRoute
{
    public string RouteId { get; set; } = "";
    public bool Enabled { get; set; }
    public bool NavmeshVerified { get; set; }
    public string Worldspace { get; set; } = "";
    public List<string> Anchors { get; set; } = new();
    public string SpawnMarker { get; set; } = "";
    public string RetireMarker { get; set; } = "";
    public int MinPlayerDistance { get; set; }
    public int MaxPlayerDistance { get; set; }
    public int StuckTimeoutSeconds { get; set; }
}

public sealed class PhysicalCargoItem
{
    public string FormKey { get; set; } = "";
    public string Commodity { get; set; } = "";
    public int Units { get; set; }
}

public sealed class PhysicalCargoStack
{
    public string FormKey { get; set; } = "";
    public int Count { get; set; }
    public int UnitsEach { get; set; }
}

public sealed class PhysicalCaravanPlan
{
    public VisibleCaravanPlan Shipment { get; set; } = new();
    public PhysicalCaravanRoute? Route { get; set; }
    public List<PhysicalCargoStack> Cargo { get; set; } = new();
    public int UnrepresentedUnits { get; set; }
}
