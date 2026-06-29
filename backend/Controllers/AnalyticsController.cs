using AltusIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AltusIQ.Api.Controllers;

[ApiController]
[Route("api/analytics")]
public class AnalyticsController(AnalyticsService analyticsService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAnalytics(CancellationToken ct)
    {
        var analytics = await analyticsService.GetAnalyticsAsync(ct);
        return Ok(analytics);
    }
}
