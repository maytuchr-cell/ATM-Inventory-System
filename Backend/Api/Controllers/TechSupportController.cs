using Microsoft.AspNetCore.Mvc;
using Api.Services;

namespace Api.Controllers;

// The list of technical advisors a tech can consult before submitting a withdraw request.
// Backed by API_KMM's GET /Employee/Techsupport (via a shared service-account login, see
// KmmAuthService.GetTechSupportListAsync) — falls back to a small mock list if API_KMM is
// unreachable or returns nothing, so the dropdown never ends up empty.
[ApiController]
[Route("[controller]")]
public class TechSupportController : ControllerBase
{
    private readonly KmmAuthService _kmm;

    public TechSupportController(KmmAuthService kmm)
    {
        _kmm = kmm;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var names = await _kmm.GetTechSupportListAsync();
        if (names.Count == 0)
            return Ok(new[] { "Tech A", "Tech B" });
        return Ok(names);
    }
}
