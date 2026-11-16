using AltusIQ.Api.Models;
using AltusIQ.Api.Services;

namespace AltusIQ.Api.Tests.Services;

public class EventVisibilityTests
{
    private static AnomalyEvent Event(string squawk, bool sensitive) => new()
    {
        Id = Guid.NewGuid(),
        Icao24 = "abc123",
        Squawk = squawk,
        Kind = sensitive ? "unlawful_interference" : "general_emergency",
        Sensitive = sensitive
    };

    private static readonly List<AnomalyEvent> Rows =
    [
        Event("7500", sensitive: true),
        Event("7700", sensitive: false),
        Event("7600", sensitive: false)
    ];

    [Fact]
    public void The_public_endpoint_hides_sensitive_events_by_default()
    {
        // Same filter EventQueryService applies to both /api/events and
        // /api/events/{id}; Events:ExposeSensitive defaults to false.
        var visible = Rows.AsQueryable().ApplyVisibility(new EventsSettings().ExposeSensitive).ToList();

        Assert.Equal(2, visible.Count);
        Assert.DoesNotContain(visible, e => e.Squawk == "7500");
    }

    [Fact]
    public void Sensitive_events_are_returned_only_when_explicitly_exposed()
    {
        var visible = Rows.AsQueryable().ApplyVisibility(exposeSensitive: true).ToList();

        Assert.Equal(3, visible.Count);
    }
}
