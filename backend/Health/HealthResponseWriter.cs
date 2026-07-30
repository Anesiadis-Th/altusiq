using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AltusIQ.Api.Health;

public static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions Options =
        new() { WriteIndented = true };

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            total_duration_ms =
                Math.Round(report.TotalDuration.TotalMilliseconds),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                data = entry.Value.Data
            })
        };

        return context.Response.WriteAsync(
            JsonSerializer.Serialize(payload, Options));
    }
}
