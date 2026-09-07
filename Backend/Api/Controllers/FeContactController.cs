using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Api.Models;

namespace Api.Controllers;

// DHL's "Contact list DataOne FE" reference sheet — FE ID (site/route code) → default address
// for that site. Admin re-imports the whole sheet whenever DHL sends an updated one (upsert by
// FeId, never hand-edited row by row). Tech.html uses Suggest to auto-fill FE ID + address on a
// brand-new withdraw/return request by matching the Ticket's TechName against FeName here — see
// Suggest below for exactly how forgiving that match is and why.
[ApiController]
[Route("[controller]")]
public class FeContactController : ControllerBase
{
    private readonly AppDbContext _context;

    public FeContactController(AppDbContext context)
    {
        _context = context;
    }

    // GET /FeContact
    [HttpGet]
    public IActionResult GetAll()
    {
        var contacts = _context.FeContacts.OrderBy(c => c.FeId).ToList();
        return Ok(contacts);
    }

    // GET /FeContact/suggest?techName=...
    // Best-effort match only — the sheet's FEName and our Ticket.TechName come from two different
    // systems (DHL's contact list vs. Aservice/KMM) that were never designed to line up exactly,
    // so this normalizes both sides (strip whitespace/honorifics, case-fold) and accepts an exact
    // match on the normalized name, then falls back to either name containing the other (handles
    // a middle name or nickname on one side only). No match at all → 204, and the tech form just
    // behaves as it always has (type it by hand / pick a SavedAddress).
    [HttpGet("suggest")]
    public IActionResult Suggest([FromQuery] string? techName)
    {
        if (string.IsNullOrWhiteSpace(techName)) return NoContent();

        var target = Normalize(techName);
        if (target.Length == 0) return NoContent();

        var contacts = _context.FeContacts.ToList();
        var exact = contacts.FirstOrDefault(c => Normalize(c.FeName) == target);
        var match = exact ?? contacts.FirstOrDefault(c =>
        {
            var name = Normalize(c.FeName);
            return name.Length > 0 && (name.Contains(target) || target.Contains(name));
        });

        if (match == null) return NoContent();
        return Ok(new { match.FeId, match.FeName, match.Address });
    }

    private static readonly string[] Honorifics = { "นาย", "นาง", "นางสาว", "คุณ", "mr.", "mr", "mrs.", "mrs", "ms.", "ms" };

    private static string Normalize(string name)
    {
        var s = name.Trim().ToLowerInvariant();
        foreach (var h in Honorifics)
            if (s.StartsWith(h)) { s = s[h.Length..]; break; }
        return new string(s.Where(ch => !char.IsWhiteSpace(ch)).ToArray());
    }

    // POST /FeContact/import — reads the "Contact list DataOne FE" sheet (falls back to scanning
    // every sheet by header, same approach as DailyReportController.ParseFile) and upserts every
    // row by FeId. Wipes nothing — an FE ID missing from this import just keeps its last-known
    // address until a future import updates or a person manually cleans it up.
    [HttpPost("import")]
    [Authorize(Policy = "CanWriteMasterData")]
    public IActionResult Import(IFormFile? file)
    {
        if (file == null || file.Length == 0) return BadRequest(new { message = "กรุณาแนบไฟล์" });
        if (Path.GetExtension(file.FileName).ToLowerInvariant() != ".xlsx")
            return BadRequest(new { message = "รองรับเฉพาะไฟล์ .xlsx" });

        List<(string FeId, string FeName, string? Tel, string Address, string? Postcode)> rows;
        try
        {
            using var stream = file.OpenReadStream();
            using var wb = new XLWorkbook(stream);

            var preferred = wb.Worksheets.Contains("Contact list DataOne FE")
                ? new[] { wb.Worksheet("Contact list DataOne FE") }.Concat(wb.Worksheets)
                : wb.Worksheets.AsEnumerable();

            IXLWorksheet? ws = null;
            int headerRow = -1, cFeId = -1, cFeName = -1, cTel = -1, cAddress = -1, cPostcode = -1;

            foreach (var candidate in preferred)
            {
                int r1 = -1, id1 = -1, nm1 = -1, tel1 = -1, ad1 = -1, pc1 = -1;
                var lastRowScan = Math.Min(5, candidate.LastRowUsed()?.RowNumber() ?? 1);
                var lastColScan = candidate.LastColumnUsed()?.ColumnNumber() ?? 1;
                for (int r = 1; r <= lastRowScan; r++)
                    for (int c = 1; c <= lastColScan; c++)
                    {
                        var val = candidate.Cell(r, c).GetString().Trim();
                        if (val.Equals("FE ID", StringComparison.OrdinalIgnoreCase)) { id1 = c; r1 = r; }
                        else if (val.Equals("FEName", StringComparison.OrdinalIgnoreCase)) nm1 = c;
                        else if (val.Equals("Tel.", StringComparison.OrdinalIgnoreCase) || val.Equals("Tel", StringComparison.OrdinalIgnoreCase)) tel1 = c;
                        else if (val.Equals("Address", StringComparison.OrdinalIgnoreCase)) ad1 = c;
                        else if (val.Replace(" ", "").Equals("Postcode", StringComparison.OrdinalIgnoreCase)) pc1 = c;
                    }
                if (id1 >= 0 && nm1 >= 0 && ad1 >= 0)
                {
                    ws = candidate; headerRow = r1; cFeId = id1; cFeName = nm1; cTel = tel1; cAddress = ad1; cPostcode = pc1;
                    break;
                }
            }

            if (ws == null)
                return BadRequest(new { message = "ไม่พบชีตที่มีคอลัมน์ FE ID, FEName และ Address — เช็คว่าเป็นไฟล์ Contact List ที่ถูกต้อง" });

            rows = new();
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRow;
            for (int r = headerRow + 1; r <= lastRow; r++)
            {
                var feId = ws.Cell(r, cFeId).GetString().Trim();
                if (string.IsNullOrWhiteSpace(feId)) continue;

                rows.Add((
                    FeId: feId,
                    FeName: ws.Cell(r, cFeName).GetString().Trim(),
                    Tel: cTel > 0 ? ws.Cell(r, cTel).GetString().Trim() : null,
                    Address: cAddress > 0 ? ws.Cell(r, cAddress).GetString().Trim() : "",
                    Postcode: cPostcode > 0 ? ws.Cell(r, cPostcode).GetString().Trim() : null
                ));
            }
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"อ่านไฟล์ไม่สำเร็จ: {ex.Message}" });
        }

        var byFeId = _context.FeContacts.ToDictionary(c => c.FeId);
        int added = 0, updated = 0;
        foreach (var row in rows)
        {
            if (byFeId.TryGetValue(row.FeId, out var existing))
            {
                existing.FeName = row.FeName;
                existing.Tel = row.Tel;
                existing.Address = row.Address;
                existing.Postcode = row.Postcode;
                existing.UpdatedAt = DateTime.Now;
                updated++;
            }
            else
            {
                _context.FeContacts.Add(new FeContact
                {
                    FeId = row.FeId, FeName = row.FeName, Tel = row.Tel,
                    Address = row.Address, Postcode = row.Postcode, UpdatedAt = DateTime.Now
                });
                added++;
            }
        }
        _context.SaveChanges();
        return Ok(new { message = "Imported.", added, updated, total = rows.Count });
    }
}
