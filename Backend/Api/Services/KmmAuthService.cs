using System.Text;
using System.Text.Json;
using System.Linq;

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
    private readonly IConfiguration _config;

    public KmmAuthService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(60);
        _config = config;
    }

    public record LoginOutcome(bool Reachable, bool Success, string? Name, string? Detail, string? Token);
    public record EngineerInfo(bool Found, string? Name, string? RawJson);
    public record JobTicketInfo(string JobNo, string? AtmCode, string? AtmName, string? AtmAddress, string? ProblemDetail, string? MainProblem, string? ServiceType, string? AserviceStatus, string? ZoneName, DateTime? OpenDatetime, DateTime? AppointDatetime);

    // Cached token for the shared "service account" (KmmApi:ServiceUsername/ServicePassword) used
    // by endpoints that aren't tied to a specific tech's own login (e.g. GetTechSupportListAsync).
    // Re-logs in whenever there's no cached token or the last call got a 401, rather than tracking
    // real JWT expiry (API_KMM doesn't document one) — cheap enough since this is called rarely.
    private string? _serviceToken;

    // GET /Employee/Techsupport — list of technical advisors, used to replace TechSupportController's
    // mock data. Logs in with the shared service account (KmmApi:ServiceUsername/ServicePassword)
    // to get a Bearer token, since every API_KMM endpoint requires one per its Swagger spec.
    public async Task<List<string>> GetTechSupportListAsync(CancellationToken ct = default)
    {
        var names = await FetchTechSupportAsync(ct);
        if (names != null) return names;

        // Retry once with a fresh token in case the cached one had gone stale/invalid.
        _serviceToken = null;
        names = await FetchTechSupportAsync(ct);
        return names ?? new List<string>();
    }

    private async Task<List<string>?> FetchTechSupportAsync(CancellationToken ct)
    {
        if (!await EnsureServiceTokenAsync(ct)) return null;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "/Employee/Techsupport");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _serviceToken);
            using var res = await _http.SendAsync(req, ct);
            var json = await res.Content.ReadAsStringAsync(ct);
            Console.WriteLine($"🔎 KMM /Employee/Techsupport -> HTTP {(int)res.StatusCode}, body: {json}");

            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized) return null; // caller retries with fresh token
            if (!res.IsSuccessStatusCode || string.IsNullOrWhiteSpace(json)) return new List<string>();

            using var doc = JsonDocument.Parse(json);
            var result = new List<string>();
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        result.Add(item.GetString()!);
                    }
                    else if (item.ValueKind == JsonValueKind.Object)
                    {
                        // Real shape: { employeeInfo: { emp_name, emp_surname, ... }, role, zone }.
                        if (item.TryGetProperty("employeeInfo", out var info) && info.ValueKind == JsonValueKind.Object)
                        {
                            var first = info.TryGetProperty("emp_name", out var fn) && fn.ValueKind == JsonValueKind.String ? fn.GetString() : null;
                            var last = info.TryGetProperty("emp_surname", out var ln) && ln.ValueKind == JsonValueKind.String ? ln.GetString() : null;
                            var fullName = string.Join(" ", new[] { first, last }.Where(s => !string.IsNullOrWhiteSpace(s)));
                            if (!string.IsNullOrWhiteSpace(fullName)) { result.Add(fullName); continue; }
                        }
                        foreach (var key in new[] { "name", "Name", "techsupport_name", "full_name", "emp_name" })
                            if (item.TryGetProperty(key, out var n) && n.ValueKind == JsonValueKind.String)
                            { result.Add(n.GetString()!); break; }
                    }
                }
            }
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            Console.WriteLine($"🔎 KMM /Employee/Techsupport failed: {ex.Message}");
            return null;
        }
    }

    // GET /JobTicket/Technician?emp_id=... — every currently-open job ticket assigned to one
    // technician. Used to let a tech pick their case from a dropdown instead of typing the Case
    // No./ATM code/problem description by hand (see nrCaseNo etc. in tech.html). Same shared
    // service-account token as GetTechSupportListAsync.
    public async Task<List<JobTicketInfo>> GetJobTicketsAsync(string empId, CancellationToken ct = default)
    {
        var tickets = await FetchJobTicketsAsync(empId, ct);
        if (tickets != null) return tickets;

        _serviceToken = null;
        tickets = await FetchJobTicketsAsync(empId, ct);
        return tickets ?? new List<JobTicketInfo>();
    }

    private async Task<List<JobTicketInfo>?> FetchJobTicketsAsync(string empId, CancellationToken ct)
    {
        if (!await EnsureServiceTokenAsync(ct)) return null;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"/JobTicket/Technician?emp_id={Uri.EscapeDataString(empId)}");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _serviceToken);
            using var res = await _http.SendAsync(req, ct);
            var json = await res.Content.ReadAsStringAsync(ct);
            Console.WriteLine($"🔎 KMM /JobTicket/Technician?emp_id={empId} -> HTTP {(int)res.StatusCode}, body length={json.Length}");

            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized) return null;
            if (!res.IsSuccessStatusCode || string.IsNullOrWhiteSpace(json)) return new List<JobTicketInfo>();

            using var doc = JsonDocument.Parse(json);
            var result = new List<JobTicketInfo>();
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (!item.TryGetProperty("jobDetail", out var jd) || jd.ValueKind != JsonValueKind.Object) continue;

                string? Str(JsonElement obj, string key) => obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

                var jobNo = Str(jd, "job_no");
                if (string.IsNullOrWhiteSpace(jobNo)) continue;

                string? atmCode = null, atmName = null, atmAddress = null;
                if (item.TryGetProperty("device", out var dev) && dev.ValueKind == JsonValueKind.Object)
                {
                    atmCode = Str(dev, "term_id");
                    atmName = Str(dev, "term_name");
                    atmAddress = Str(dev, "term_addr");
                }
                string? zoneName = null;
                if (item.TryGetProperty("zone", out var zone) && zone.ValueKind == JsonValueKind.Object)
                    zoneName = Str(zone, "zone_name_th");

                DateTime? openDt = null;
                if (item.TryGetProperty("stepOpenJob", out var step) && step.ValueKind == JsonValueKind.Object
                    && step.TryGetProperty("open_datetime", out var od) && od.ValueKind == JsonValueKind.String
                    && DateTime.TryParse(od.GetString(), out var parsed))
                    openDt = parsed;

                // stepAssignJob.appoint_datetime is the CIT/engineer's scheduled appointment time —
                // used as "วันที่ต้องการอะไหล่" (parts-needed-by) since parts should arrive by then.
                DateTime? appointDt = null;
                if (item.TryGetProperty("stepAssignJob", out var assign) && assign.ValueKind == JsonValueKind.Object
                    && assign.TryGetProperty("appoint_datetime", out var ad) && ad.ValueKind == JsonValueKind.String
                    && DateTime.TryParse(ad.GetString(), out var appointParsed))
                    appointDt = appointParsed;

                result.Add(new JobTicketInfo(
                    JobNo: jobNo!,
                    AtmCode: atmCode,
                    AtmName: atmName,
                    AtmAddress: atmAddress,
                    ProblemDetail: Str(jd, "problem_detail"),
                    MainProblem: Str(jd, "main_problem"),
                    ServiceType: Str(jd, "service_type"),
                    AserviceStatus: Str(jd, "aservice_status"),
                    ZoneName: zoneName,
                    OpenDatetime: openDt,
                    AppointDatetime: appointDt));
            }
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            Console.WriteLine($"🔎 KMM /JobTicket/Technician failed: {ex.Message}");
            return null;
        }
    }

    // Shared service-account token acquisition for endpoints not tied to a specific tech's own
    // login (Techsupport list, JobTicket-by-technician). Logs in once, caches, re-logs-in on demand.
    private async Task<bool> EnsureServiceTokenAsync(CancellationToken ct)
    {
        if (_serviceToken != null) return true;

        var username = _config["KmmApi:ServiceUsername"];
        var password = _config["KmmApi:ServicePassword"];
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return false;

        var login = await LoginAsync(username, password, ct);
        if (!login.Success || string.IsNullOrWhiteSpace(login.Token))
            return false;
        _serviceToken = login.Token;
        return true;
    }

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
            // Real shape (confirmed via live call): { employeeInfo: { emp_name, emp_surname, ... },
            // role, zone } — same nested shape as /Employee/Techsupport. Keep the flat-field guesses
            // as a fallback in case a differently-shaped response ever comes back.
            string? name = null;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("employeeInfo", out var info) && info.ValueKind == JsonValueKind.Object)
            {
                var first = info.TryGetProperty("emp_name", out var fn) && fn.ValueKind == JsonValueKind.String ? fn.GetString() : null;
                var last = info.TryGetProperty("emp_surname", out var ln) && ln.ValueKind == JsonValueKind.String ? ln.GetString() : null;
                name = string.Join(" ", new[] { first, last }.Where(s => !string.IsNullOrWhiteSpace(s)));
                if (string.IsNullOrWhiteSpace(name)) name = null;
            }
            if (name == null)
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
