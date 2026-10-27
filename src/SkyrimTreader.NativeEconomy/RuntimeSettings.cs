namespace SkyrimTreader.NativeEconomy;

public sealed class RuntimeSettings
{
    public int SchemaVersion { get; set; } = 1;
    public double PriceIntensity { get; set; } = 1;
    public bool VisibleCaravansEnabled { get; set; } = true;

    public void Validate()
    {
        if (SchemaVersion != 1 || !double.IsFinite(PriceIntensity) || PriceIntensity is < 0 or > 1)
            throw new InvalidDataException("Invalid runtime settings.");
    }
}
