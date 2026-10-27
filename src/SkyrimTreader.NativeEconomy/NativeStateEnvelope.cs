using System.Text.Json.Serialization;
using SkyrimTreader.Simulation;

namespace SkyrimTreader.NativeEconomy;

public sealed class NativeStateEnvelope
{
    public int SchemaVersion { get; set; } = 1;
    public string CatalogFingerprint { get; set; } = "";
    public int? WorldDayOrigin { get; set; }
    public SimulationState State { get; set; } = new();
}

[JsonSerializable(typeof(NativeStateEnvelope))]
[JsonSerializable(typeof(RuntimeSettings))]
public partial class NativeJsonContext : JsonSerializerContext;
