using System.Text.Json.Serialization;

namespace SkyrimTreader.Simulation;

[JsonSerializable(typeof(EconomyConfig))]
[JsonSerializable(typeof(SimulationState))]
[JsonSerializable(typeof(ItemCatalog))]
[JsonSerializable(typeof(PriceSnapshot))]
[JsonSerializable(typeof(CaravanManifest))]
[JsonSerializable(typeof(EconomyStatus))]
[JsonSerializable(typeof(VisibleCaravanPlan))]
[JsonSerializable(typeof(PhysicalCaravanRoutes))]
[JsonSerializable(typeof(PhysicalCaravanPlan))]
public partial class EconomyJsonContext : JsonSerializerContext;
