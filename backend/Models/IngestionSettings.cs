namespace AltusIQ.Api.Models;

public class IngestionSettings
{
    public double MinLon { get; set; } = 4.0;
    public double MaxLon { get; set; } = 32.0;
    public double MinLat { get; set; } = 54.0;
    public double MaxLat { get; set; } = 72.0;
    public int GapThresholdSeconds { get; set; } = 120;
    public int MinPointIntervalSeconds { get; set; } = 30;
    public int MaxTrackPoints { get; set; } = 300;
}