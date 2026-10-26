using System.Text.Json.Serialization;

namespace SkyrimTreader.Simulation;

[JsonSerializable(typeof(EconomyConfig))]
[JsonSerializable(typeof(SimulationState))]
[JsonSerializable(typeof(ItemCatalog))]
[JsonSerializable(typeof(PriceSnapshot))]
[JsonSerializable(typeof(CaravanManifest))]
public partial class EconomyJsonContext : JsonSerializerContext;
