using AltusIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AltusIQ.Api.Controllers;

[ApiController]
[Route("api/flights")]
public class FlightsController(FlightQueryService queryService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetFlights(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var toDate = (to ?? DateTime.UtcNow).ToUniversalTime();
        var fromDate = (from ?? toDate.AddHours(-24)).ToUniversalTime();

        var flights = await queryService.GetFlightsAsync(fromDate, toDate, ct);
        return Ok(flights);
    }

    [HttpGet("{id:guid}/track")]
    public async Task<IActionResult> GetTrack(Guid id, CancellationToken ct)
    {
        var track = await queryService.GetTrackAsync(id, ct);
        if (track is null)
            return NotFound();

        return Ok(track);
    }
}