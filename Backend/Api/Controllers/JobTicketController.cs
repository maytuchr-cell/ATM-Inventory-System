using Microsoft.AspNetCore.Mvc;
using Api.Services;

namespace Api.Controllers;

// Read-only passthrough to API_KMM's job ticket data — lets tech.html's "New Request" form
// offer a dropdown of a technician's currently-open field-service tickets (Case No., ATM code,
// problem description, zone) instead of the tech typing all of that by hand. See
// KmmAuthService.GetJobTicketsAsync for the API_KMM call and response-shape parsing.
[ApiController]
[Route("[controller]")]
public class JobTicketController : ControllerBase
{
    private readonly KmmAuthService _kmm;

    public JobTicketController(KmmAuthService kmm)
    {
        _kmm = kmm;
    }

    [HttpGet("technician")]
    public async Task<IActionResult> GetForTechnician([FromQuery] string empId)
    {
        if (string.IsNullOrWhiteSpace(empId))
            return BadRequest(new { message = "empId is required." });

        var tickets = await _kmm.GetJobTicketsAsync(empId.Trim());
        return Ok(tickets);
    }
}
