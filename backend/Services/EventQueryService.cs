using System.Text.Json;
using AltusIQ.Api.Data;
using AltusIQ.Api.Models;
using AltusIQ.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AltusIQ.Api.Services;

public class EventQueryService(AltusIqDbContext db, IOptions<EventsSettings> settings)
{
    private readonly bool _exposeSensitive = settings.Value.ExposeSensitive;

    public async Task<IReadOnlyList<AnomalyEventSummaryDto>> GetEventsAsync(
        DateTime from, DateTime to, CancellationToken ct)
    {
        return await db.AnomalyEvents
            .ApplyVisibility(_exposeSensitive)
            .Where(e => e.ConfirmedAt >= from && e.ConfirmedAt <= to)
            .OrderByDescending(e => e.ConfirmedAt)
            .Take(200)
            .Select(e => new AnomalyEventSummaryDto(
                e.Id,
                e.Icao24,
                e.Callsign,
                e.OriginCountry,
                e.Squawk,
                e.Kind,
                e.ConfirmedAt,
                e.LastSeenAt,
                e.ClosedAt,
                e.Status,
                e.LastLatitude,
                e.LastLongitude,
                e.LastAltitude,
                e.InvestigationStatus,
                e.Grounded,
                e.InvestigationJson != null))
            .ToListAsync(ct);
    }

    public async Task<AnomalyEventDetailDto?> GetEventAsync(Guid id, CancellationToken ct)
    {
        var e = await db.AnomalyEvents
            .ApplyVisibility(_exposeSensitive)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        return e is null ? null : ToDetail(e);
    }

    internal static AnomalyEventDetailDto ToDetail(AnomalyEvent e) => new(
        e.Id,
        e.Icao24,
        e.Callsign,
        e.OriginCountry,
        e.Squawk,
        e.Kind,
        e.FirstSeenAt,
        e.ConfirmedAt,
        e.LastSeenAt,
        e.ClosedAt,
        e.Status,
        e.FirstLatitude,
        e.FirstLongitude,
        e.FirstAltitude,
        e.LastLatitude,
        e.LastLongitude,
        e.LastAltitude,
        ParseJson(e.EvidenceJson),
        ParseJson(e.InvestigationJson),
        e.InvestigationStatus,
        e.Grounded,
        e.PromptVersion,
        e.Provider,
        e.Model,
        e.InputTokens,
        e.OutputTokens,
        e.LatencyMs);

    private static JsonElement? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}

public static class EventVisibility
{
    /// <summary>
    /// The one rule the public API applies: 7500 rows stay out unless
    /// Events:ExposeSensitive is on. Kept as a queryable filter so it runs in
    /// SQL and can be tested against a plain list.
    /// </summary>
    public static IQueryable<AnomalyEvent> ApplyVisibility(
        this IQueryable<AnomalyEvent> events, bool exposeSensitive) =>
        exposeSensitive ? events : events.Where(e => !e.Sensitive);
}
