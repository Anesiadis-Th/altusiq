namespace AltusIQ.Api.Models;

public class EnrichmentSettings
{
    public int IntervalSeconds { get; set; } = 300;
    public int BatchSize { get; set; } = 5;
    public int MinAgeMinutes { get; set; } = 60;
    public int MaxAttempts { get; set; } = 3;
    public int WindowBufferHours { get; set; } = 12;
}
