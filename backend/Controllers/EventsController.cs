using AltusIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AltusIQ.Api.Controllers;

[ApiController]
[Route("api/events")]
public class EventsController(EventQueryService queryService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetEvents(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var toDate = AsUtc(to) ?? DateTime.UtcNow;
        var fromDate = AsUtc(from) ?? toDate.AddDays(-7);

        var events = await queryService.GetEventsAsync(fromDate, toDate, ct);
        return Ok(events);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetEvent(Guid id, CancellationToken ct)
    {
        var detail = await queryService.GetEventAsync(id, ct);
        if (detail is null)
            return NotFound();

        return Ok(detail);
    }

    private static DateTime? AsUtc(DateTime? value)
    {
        if (value is null)
            return null;

        return value.Value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            : value.Value.ToUniversalTime();
    }
}
