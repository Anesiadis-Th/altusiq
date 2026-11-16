using System.Text.Json;
using AltusIQ.Api.Models;

namespace AltusIQ.Api.Tests.Services;

public class AircraftSerializationTests
{
    // Same options Program.cs gives the SignalR JSON protocol. The broadcast is
    // the Aircraft list serialised with these, so this is what the browser sees.
    private static readonly JsonSerializerOptions HubOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    [Fact]
    public void Squawk_is_absent_from_the_hub_payload()
    {
        var aircraft = new Aircraft
        {
            Icao24 = "4b1806",
            Callsign = "SWR123",
            Longitude = 8.5,
            Latitude = 47.4,
            Squawk = "7700"
        };

        var json = JsonSerializer.Serialize(new[] { aircraft }, HubOptions);

        Assert.DoesNotContain("squawk", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("7700", json);
        // The rest of the frontend Aircraft contract is untouched.
        Assert.Contains("\"icao24\":\"4b1806\"", json);
        Assert.Contains("\"last_contact\"", json);
    }
}
