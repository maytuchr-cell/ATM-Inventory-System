using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Api.Models;
using Api.Services;

namespace Api.Controllers;

// User & role management — SystemAdmin only.
[ApiController]
[Route("[controller]")]
[Authorize(Policy = "SystemAdminOnly")]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly AuditService _audit;
    private static readonly string[] ValidRoles = { "SystemAdmin", "Staff", "Auditor", "Tech" };

    public UsersController(AppDbContext context, AuditService audit)
    {
        _context = context;
        _audit = audit;
    }

    // GET /api/Users
    [HttpGet]
    public IActionResult GetAll()
    {
        var users = _context.Users
            .OrderBy(u => u.Email)
            .Select(u => new { u.Id, u.Email, u.Name, u.Role, u.IsActive, u.CreatedAt })
            .ToList();
        return Ok(users);
    }

    // GET /api/Users/roles
    [HttpGet("roles")]
    public IActionResult GetRoles() => Ok(ValidRoles);

    // POST /api/Users
    [HttpPost]
    public IActionResult Create([FromBody] UserCreateDto dto)
    {
        var email = dto.Email?.Trim().ToLowerInvariant() ?? "";
        if (string.IsNullOrWhiteSpace(email)) return BadRequest(new { message = "Email is required." });
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { message = "Name is required." });
        if (!ValidRoles.Contains(dto.Role)) return BadRequest(new { message = $"Role must be one of: {string.Join(", ", ValidRoles)}." });
        if (string.IsNullOrWhiteSpace(dto.Password) || dto.Password.Length < 6)
            return BadRequest(new { message = "Password must be at least 6 characters." });
        if (_context.Users.Any(u => u.Email == email))
            return BadRequest(new { message = $"Email '{email}' already exists." });

        var user = new User
        {
            Email        = email,
            Name         = dto.Name.Trim(),
            Role         = dto.Role,
            PasswordHash = PasswordHasher.Hash(dto.Password),
            IsActive     = true,
            CreatedAt    = DateTime.UtcNow
        };
        _context.Users.Add(user);
        _context.SaveChanges();
        _audit.Log(User, "User", user.Id.ToString(), "CREATE", null, new { user.Email, user.Role });
        return Ok(new { user.Id, user.Email, user.Name, user.Role, user.IsActive });
    }

    // PUT /api/Users/{id} — update name/role/active (not password)
    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] UserUpdateDto dto)
    {
        var user = _context.Users.FirstOrDefault(u => u.Id == id);
        if (user == null) return NotFound();
        if (!ValidRoles.Contains(dto.Role)) return BadRequest(new { message = $"Role must be one of: {string.Join(", ", ValidRoles)}." });

        // Guard: don't allow removing the last active SystemAdmin (lockout protection).
        if (user.Role == "SystemAdmin" && dto.Role != "SystemAdmin"
            && _context.Users.Count(u => u.Role == "SystemAdmin" && u.IsActive) <= 1)
            return BadRequest(new { message = "Cannot change the role of the last active SystemAdmin." });

        var old = new { user.Name, user.Role, user.IsActive };
        user.Name     = string.IsNullOrWhiteSpace(dto.Name) ? user.Name : dto.Name.Trim();
        user.Role     = dto.Role;
        user.IsActive = dto.IsActive;
        _context.SaveChanges();
        _audit.Log(User, "User", id.ToString(), "UPDATE", System.Text.Json.JsonSerializer.Serialize(old), new { user.Name, user.Role, user.IsActive });
        return Ok(new { user.Id, user.Email, user.Name, user.Role, user.IsActive });
    }

    // PUT /api/Users/{id}/password — reset password
    [HttpPut("{id}/password")]
    public IActionResult ResetPassword(int id, [FromBody] PasswordResetDto dto)
    {
        var user = _context.Users.FirstOrDefault(u => u.Id == id);
        if (user == null) return NotFound();
        if (string.IsNullOrWhiteSpace(dto.Password) || dto.Password.Length < 6)
            return BadRequest(new { message = "Password must be at least 6 characters." });

        user.PasswordHash = PasswordHasher.Hash(dto.Password);
        _context.SaveChanges();
        _audit.Log(User, "User", id.ToString(), "UPDATE", null, new { passwordReset = true });
        return Ok(new { message = "Password reset." });
    }
}

public class UserCreateDto
{
    public string? Email { get; set; }
    public string? Name { get; set; }
    public string Role { get; set; } = "Staff";
    public string? Password { get; set; }
}

public class UserUpdateDto
{
    public string? Name { get; set; }
    public string Role { get; set; } = "Staff";
    public bool IsActive { get; set; } = true;
}

public class PasswordResetDto
{
    public string? Password { get; set; }
}
