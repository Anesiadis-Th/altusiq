using AltusIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AltusIQ.Api.Controllers;

[ApiController]
[Route("api/routes")]
public class RoutesController(IRouteLookupService routeLookupService) : ControllerBase
{
    [HttpGet("{callsign}")]
    public async Task<IActionResult> GetRoute(string callsign, CancellationToken ct)
    {
        var route = await routeLookupService.GetRouteAsync(callsign, ct);
        return route is null ? NotFound() : Ok(route);
    }
}
