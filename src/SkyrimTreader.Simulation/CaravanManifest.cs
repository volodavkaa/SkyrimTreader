using System.Text.Json;

namespace SkyrimTreader.Simulation;

public sealed class CaravanManifest
{
    public int SchemaVersion { get; set; } = 1;
    public int Day { get; set; }
    public string ConfigFingerprint { get; set; } = "";
    public List<CaravanManifestEntry> Caravans { get; set; } = new();

    public static CaravanManifest Create(EconomyEngine engine)
    {
        var manifest = new CaravanManifest
        {
            Day = engine.State.Day,
            ConfigFingerprint = engine.State.ConfigFingerprint
        };
        foreach (var shipment in engine.State.Shipments
                     .Where(x => x.Status is ShipmentStatus.InTransit or ShipmentStatus.Delayed or ShipmentStatus.PartiallyLost)
                     .OrderBy(x => x.Id))
        {
            manifest.Caravans.Add(new CaravanManifestEntry
            {
                ShipmentId = shipment.Id,
                RouteId = shipment.RouteId,
                From = shipment.From,
                To = shipment.To,
                Commodity = shipment.Commodity,
                RemainingQuantity = shipment.RemainingQuantity,
                DepartureDay = shipment.DepartureDay,
                ArrivalDay = shipment.ArrivalDay,
                Status = shipment.Status
            });
        }
        return manifest;
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

public sealed class CaravanManifestEntry
{
    public long ShipmentId { get; set; }
    public string RouteId { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Commodity { get; set; } = "";
    public int RemainingQuantity { get; set; }
    public int DepartureDay { get; set; }
    public int ArrivalDay { get; set; }
    public ShipmentStatus Status { get; set; }
}
