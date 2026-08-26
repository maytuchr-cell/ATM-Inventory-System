using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

// The list of technical advisors a tech can consult before submitting a withdraw request.
// Currently mock data — the real list is planned to come from the KMM mobile app's own roster
// via an external API later. When that's wired up, only this one method needs to change; nothing
// else in the app (Ticket.TechSupportName, tech.html's dropdown) needs to know where the names
// came from.
[ApiController]
[Route("[controller]")]
public class TechSupportController : ControllerBase
{
    [HttpGet]
    public IActionResult GetAll()
    {
        return Ok(new[] { "Tech A", "Tech B" });
    }
}
