using AltusIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AltusIQ.Api.Controllers;

[ApiController]
[Route("api/flights")]
public class FlightsController(
    FlightQueryService queryService,
    FlightIngestionService ingestionService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetFlights(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var toDate = AsUtc(to) ?? DateTime.UtcNow;
        var fromDate = AsUtc(from) ?? toDate.AddHours(-24);

        var flights = await queryService.GetFlightsAsync(fromDate, toDate, ct);
        return Ok(flights);
    }

    private static DateTime? AsUtc(DateTime? value)
    {
        if (value is null)
            return null;

        return value.Value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            : value.Value.ToUniversalTime();
    }

    [HttpGet("{id:guid}/track")]
    public async Task<IActionResult> GetTrack(Guid id, CancellationToken ct)
    {
        var track = await queryService.GetTrackAsync(id, ct);
        if (track is null)
            return NotFound();

        return Ok(track);
    }

    [HttpGet("active/{icao24}/track")]
    public async Task<IActionResult> GetActiveTrack(string icao24, CancellationToken ct)
    {
        var track = await ingestionService.GetActiveTrackAsync(icao24, ct);
        if (track is null)
            return NotFound();

        return Ok(track);
    }
}