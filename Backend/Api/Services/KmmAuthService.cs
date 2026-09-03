using System.Text;
using System.Text.Json;

namespace Api.Services;

// Talks to the separate API_KMM service (Aservice-adjacent identity/engineer lookup, deployed at
// Config "KmmApi:BaseUrl") for the tech-login path only — see AuthController.Login. Admin/Staff/
// Auditor accounts never touch this; they're always verified against the local Users table.
//
// API_KMM's own /Auth/Login currently depends on a Neo4j server that's intermittently unreachable
// (can take ~40s to time out internally before still succeeding) — the timeout here is set long
// enough to not cut off a slow-but-genuine success, and every failure mode (bad creds, timeout,
// network error) is surfaced distinctly so AuthController can decide whether a local-password
// fallback is possible.
public class KmmAuthService
{
    private readonly HttpClient _http;

    public KmmAuthService(HttpClient http)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    public record LoginOutcome(bool Reachable, bool Success, string? Name, string? Detail, string? Token);
    public record EngineerInfo(bool Found, string? Name, string? RawJson);

    // POST /Auth/Login — {username, password} -> {status, detail, token}. Reachable=false means
    // we couldn't even get a response (network/timeout) — the caller should fall back to a local
    // password check in that case. Reachable=true, Success=false means API_KMM itself said the
    // credentials are wrong — no fallback, that's a real answer. The returned Token must be passed
    // to GetEngineerAsync — per API_KMM's Swagger spec, every endpoint (including /Employee/
    // Engineer) requires this Bearer token, not just the ones that look auth-related.
    public async Task<LoginOutcome> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { username, password });
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var res = await _http.PostAsync("/Auth/Login", content, ct);
            var json = await res.Content.ReadAsStringAsync(ct);
            Console.WriteLine($"🔎 KMM /Auth/Login -> HTTP {(int)res.StatusCode}, body: {json}");
            if (!res.IsSuccessStatusCode)
                return new LoginOutcome(Reachable: true, Success: false, Name: null, Detail: $"HTTP {(int)res.StatusCode}", Token: null);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var status = root.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.True;
            var detail = root.TryGetProperty("detail", out var d) ? d.GetString() : null;
            var token = root.TryGetProperty("token", out var tk) && tk.ValueKind == JsonValueKind.String ? tk.GetString() : null;
            return new LoginOutcome(Reachable: true, Success: status, Name: null, Detail: detail, Token: token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return new LoginOutcome(Reachable: false, Success: false, Name: null, Detail: ex.Message, Token: null);
        }
    }

    // GET /Employee/Engineer?emp_code=... — used as a second, stricter check after a successful
    // API_KMM login: proves this person is actually a registered field engineer, not just anyone
    // holding valid API_KMM credentials (e.g. office staff on a different team). Requires the
    // Bearer token from LoginAsync — every API_KMM endpoint does, per its Swagger spec's
    // root-level "security" requirement.
    public async Task<EngineerInfo> GetEngineerAsync(string empCode, string bearerToken, CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"/Employee/Engineer?emp_code={Uri.EscapeDataString(empCode)}");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
            using var res = await _http.SendAsync(req, ct);
            var json = await res.Content.ReadAsStringAsync(ct);
            Console.WriteLine($"🔎 KMM /Employee/Engineer?emp_code={empCode} -> HTTP {(int)res.StatusCode}, token len={bearerToken?.Length ?? 0}, body: {json}");
            if (!res.IsSuccessStatusCode || string.IsNullOrWhiteSpace(json) || json.Trim() is "null" or "[]" or "{}")
                return new EngineerInfo(Found: false, Name: null, RawJson: json);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            // Response shape isn't documented beyond "200 OK" in the Swagger we have — try a few
            // likely field names for the display name, fall back to null (still "Found").
            string? name = null;
            foreach (var key in new[] { "name", "Name", "engineer_name", "full_name", "emp_name" })
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var n) && n.ValueKind == JsonValueKind.String)
                { name = n.GetString(); break; }

            return new EngineerInfo(Found: true, Name: name, RawJson: json);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            // Unreachable here means we genuinely can't verify — treat as not found (fail closed,
            // this is the strict check, not the login step that has a local fallback).
            return new EngineerInfo(Found: false, Name: null, RawJson: ex.Message);
        }
    }
}
