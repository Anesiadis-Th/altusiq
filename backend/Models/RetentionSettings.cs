namespace AltusIQ.Api.Models;

public class RetentionSettings
{
    public bool Enabled { get; set; } = true;
    public int RetentionDays { get; set; } = 15;
    public int RunAtHourUtc { get; set; } = 6;
    public int BatchSize { get; set; } = 5000;
    public int DelayBetweenBatchesMs { get; set; } = 500;
}
