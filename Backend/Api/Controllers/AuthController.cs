using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Api.Models;
using Api.Services;

namespace Api.Controllers;

[ApiController]
[Route("[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _config;
    private readonly KmmAuthService _kmm;

    public AuthController(AppDbContext context, IConfiguration config, KmmAuthService kmm)
    {
        _context = context;
        _config = config;
        _kmm = kmm;
    }

    // POST /api/Auth/login — Admin/Staff/Auditor accounts are always verified locally (never
    // touch API_KMM). A "Tech" account — existing or brand new — tries the local password first
    // (fast path, and how a previously-KMM-verified tech logs back in even if API_KMM/Neo4j is
    // down right now); only on a local miss does it fall through to API_KMM + the stricter
    // /Employee/Engineer check, auto-provisioning the local Users row on success. See
    // KmmAuthService for why this order — it's what makes "fallback to local" actually mean
    // something instead of just failing the moment API_KMM is unreachable.
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
            return BadRequest(new { message = "Email and password are required." });

        var email = dto.Email.Trim().ToLower();
        var user = _context.Users.FirstOrDefault(u => u.Email == email && u.IsActive);

        if (user != null && PasswordHasher.Verify(dto.Password, user.PasswordHash))
            return Ok(BuildLoginResponse(user));

        // A non-Tech account (or a wrong password against one) never falls through to API_KMM —
        // Admin/Staff/Auditor login is 100% local, unaffected by this feature.
        if (user != null && !user.Role.Equals("Tech", StringComparison.OrdinalIgnoreCase))
            return Unauthorized(new { message = "Invalid email or password." });

        // From here: either no local account at all (a tech logging in for the first time), or a
        // local Tech account whose password didn't match (e.g. changed on the API_KMM side) —
        // both cases are worth trying against API_KMM before giving up.
        var kmmLogin = await _kmm.LoginAsync(email, dto.Password);
        if (!kmmLogin.Reachable)
            return Unauthorized(new { message = "ระบบยืนยันตัวตนช่างขัดข้องชั่วคราว กรุณาลองใหม่อีกครั้ง" });
        if (!kmmLogin.Success)
            return Unauthorized(new { message = "Invalid email or password." });

        var engineer = await _kmm.GetEngineerAsync(email, kmmLogin.Token ?? "");
        if (!engineer.Found)
            return Unauthorized(new { message = "บัญชีนี้ไม่ได้ลงทะเบียนเป็นวิศวกร/ช่าง ไม่สามารถเข้าใช้งานได้" });

        if (user == null)
        {
            user = new User
            {
                Email = email,
                Name = engineer.Name ?? email,
                Role = "Tech",
                IsActive = true,
                PasswordHash = PasswordHasher.Hash(dto.Password) // cached for local fallback next time
            };
            _context.Users.Add(user);
        }
        else
        {
            // Existing Tech record, password changed on the API_KMM side — refresh our cached hash
            // so the next login can succeed locally even if API_KMM is down.
            user.PasswordHash = PasswordHasher.Hash(dto.Password);
            if (!string.IsNullOrWhiteSpace(engineer.Name)) user.Name = engineer.Name;
        }
        _context.SaveChanges();

        return Ok(BuildLoginResponse(user));
    }

    private object BuildLoginResponse(User user) => new
    {
        token = JwtHelper.Generate(user, _config),
        role = user.Role.ToLower(),
        email = user.Email,
        name = user.Name
    };

    // GET /api/Auth/me — returns the current authenticated user (requires valid token)
    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        return Ok(new
        {
            email = User.FindFirstValue(ClaimTypes.Email),
            name = User.FindFirstValue(ClaimTypes.Name),
            role = (User.FindFirstValue(ClaimTypes.Role) ?? "").ToLower()
        });
    }
}

public class LoginDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
