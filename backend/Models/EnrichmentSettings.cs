namespace AltusIQ.Api.Models;

public class EnrichmentSettings
{
    public int RunAtHourUtc { get; set; } = 4;
    public int MinAgeMinutes { get; set; } = 240;
    public int MaxAttempts { get; set; } = 3;
    public int MaxLookbackDays { get; set; } = 2;
    public int WindowHours { get; set; } = 2;
}
