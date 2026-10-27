namespace SkyrimTreader.Simulation;

public enum VisibleCaravanAction { Spawn, Keep, Retire }

public sealed class VisibleCaravanBinding
{
    public long ShipmentId { get; set; }
    public string Region { get; set; } = "";
    public uint LeaderFormId { get; set; }
    public uint PackFormId { get; set; }
    public uint GuardFormId { get; set; }
    public int WaypointIndex { get; set; }
    public int SpawnDay { get; set; }
}

public sealed class VisibleCaravanPlan
{
    public long ShipmentId { get; set; }
    public string RouteId { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Commodity { get; set; } = "";
    public int RemainingQuantity { get; set; }
    public int ArrivalDay { get; set; }
    public string Region { get; set; } = "";
    public uint LeaderFormId { get; set; }
    public uint PackFormId { get; set; }
    public uint GuardFormId { get; set; }
    public int WaypointIndex { get; set; }
    public VisibleCaravanAction Action { get; set; }
}
