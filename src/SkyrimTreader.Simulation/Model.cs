using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace SkyrimTreader.Simulation;

public sealed class EconomyConfig
{
    public int SchemaVersion { get; set; } = 1;
    public Dictionary<string, CommodityConfig> Commodities { get; set; } = new();
    public Dictionary<string, RegionConfig> Regions { get; set; } = new();
    public List<RouteConfig> Routes { get; set; } = new();
    public List<ShockConfig> Shocks { get; set; } = new();
    public double PriceSensitivity { get; set; } = 0.45;
    public double MinMultiplier { get; set; } = 0.70;
    public double MaxMultiplier { get; set; } = 1.60;
    public double MaxDailyPriceChange { get; set; } = 0.15;
    public double MinTradeMargin { get; set; } = 0.08;
    public int MinShipmentQuantity { get; set; } = 10;
}

public sealed class CommodityConfig
{
    public string Name { get; set; } = "";
    public int BaseValue { get; set; }
    public double SurplusSpoilageRate { get; set; }
}

public sealed class RegionConfig
{
    public string Name { get; set; } = "";
    public Dictionary<string, MarketConfig> Markets { get; set; } = new();
}

public sealed class MarketConfig
{
    public int InitialStock { get; set; }
    public int TargetStock { get; set; }
    public int DailyProduction { get; set; }
    public int DailyConsumption { get; set; }
    public int ReserveStock { get; set; }
}

public sealed class RouteConfig
{
    public string Id { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public int TravelDays { get; set; }
    public int Capacity { get; set; }
    public double CostFraction { get; set; }
    public double Risk { get; set; }
}

public sealed class ShockConfig
{
    public string Id { get; set; } = "";
    public int Day { get; set; }
    public string Region { get; set; } = "";
    public string Commodity { get; set; } = "";
    public int StockDelta { get; set; }
    public string? BlockRoute { get; set; }
    public int BlockDays { get; set; }
}

public sealed class SimulationState
{
    public int SchemaVersion { get; set; } = 1;
    public string ConfigFingerprint { get; set; } = "";
    public int Day { get; set; }
    public ulong RandomState { get; set; }
    public long NextShipmentId { get; set; } = 1;
    public Dictionary<string, Dictionary<string, MarketState>> Regions { get; set; } = new();
    public List<Shipment> Shipments { get; set; } = new();
    public VisibleCaravanBinding? VisibleCaravan { get; set; }
    public HashSet<long> RetiredVisibleShipments { get; set; } = new();
    public Dictionary<string, int> RouteBlockedUntilDay { get; set; } = new();
    public HashSet<string> AppliedShocks { get; set; } = new();
    public long LastTradeSequence { get; set; }
    public long TotalPlayerTradeDelta { get; set; }
    public long LastCaravanEventSequence { get; set; }
    public long TotalProduced { get; set; }
    public long TotalConsumed { get; set; }
    public long TotalLost { get; set; }
    public long TotalSpoiled { get; set; }
    public long TotalShockDelta { get; set; }
    public List<string> Journal { get; set; } = new();
}

public sealed class MarketState
{
    public int Stock { get; set; }
    public double Multiplier { get; set; } = 1;
}

public enum ShipmentStatus { InTransit, Delayed, PartiallyLost, Arrived, Destroyed }

public sealed class Shipment
{
    public long Id { get; set; }
    public string RouteId { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Commodity { get; set; } = "";
    public int SentQuantity { get; set; }
    public int RemainingQuantity { get; set; }
    public int DepartureDay { get; set; }
    public int ArrivalDay { get; set; }
    public ShipmentStatus Status { get; set; }
    public bool EventResolved { get; set; }
}

public static class EconomyJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = {
            new System.Text.Json.Serialization.JsonStringEnumConverter<ShipmentStatus>(),
            new System.Text.Json.Serialization.JsonStringEnumConverter<VisibleCaravanAction>()
        }
    };
    public static readonly EconomyJsonContext Context = new(Options);

    public static EconomyConfig LoadConfig(string path) =>
        JsonSerializer.Deserialize(File.ReadAllText(path), Context.EconomyConfig)
        ?? throw new InvalidDataException("Config is empty.");

    public static string Fingerprint(EconomyConfig config) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(config, Context.EconomyConfig))));

    public static SimulationState LoadState(string path) =>
        JsonSerializer.Deserialize(File.ReadAllText(path), Context.SimulationState)
        ?? throw new InvalidDataException("State is empty.");

    public static void SaveState(string path, SimulationState state)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, Context.SimulationState));
        File.Move(temporary, path, true);
    }
}
