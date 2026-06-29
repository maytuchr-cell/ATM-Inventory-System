using Api.Models;
using Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
});
builder.Services.AddEndpointsApiExplorer();

// SQLite for demo — switch to MySQL for production.
// For MySQL: set ConnectionStrings:DefaultConnection (e.g. "server=localhost;port=3306;database=atm_inventory;user=root;password=...")
// and swap the line below to:
//   options.UseMySQL(builder.Configuration.GetConnectionString("DefaultConnection")!);
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=AtmInventory.db"));

builder.Services.AddScoped<StockService>();
builder.Services.AddScoped<AuditService>();

// ── JWT Authentication ──
var jwtKey = builder.Configuration["Jwt:Key"] ?? JwtHelper.DefaultKey;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        context.Database.EnsureCreated();

        // ── Lightweight migration: add new Part catalog columns if missing (preserves data) ──
        try
        {
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = context.Database.GetDbConnection().CreateCommand())
            {
                if (cmd.Connection!.State != System.Data.ConnectionState.Open) cmd.Connection.Open();
                cmd.CommandText = "PRAGMA table_info(Parts);";
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) existing.Add(reader.GetString(1)); // column name is index 1
            }
            foreach (var (col, type) in new[] { ("MainUnit", "TEXT"), ("Remark", "TEXT"), ("ImagePath", "TEXT") })
            {
                if (!existing.Contains(col))
                {
                    context.Database.ExecuteSqlRaw($"ALTER TABLE Parts ADD COLUMN {col} {type};");
                    Console.WriteLine($"✅ Migration: added Parts.{col}");
                }
            }
            // Concurrency token (NOT NULL DEFAULT '' so existing rows match in the WHERE check)
            if (!existing.Contains("RowVersion"))
            {
                context.Database.ExecuteSqlRaw("ALTER TABLE Parts ADD COLUMN RowVersion TEXT NOT NULL DEFAULT '';");
                Console.WriteLine("✅ Migration: added Parts.RowVersion");
            }
            // StockQuantity is no longer a column — totals are computed from PartStock.
            // Drop the legacy NOT NULL column so inserts of new parts don't violate it.
            if (existing.Contains("StockQuantity"))
            {
                context.Database.ExecuteSqlRaw("ALTER TABLE Parts DROP COLUMN StockQuantity;");
                Console.WriteLine("✅ Migration: dropped legacy Parts.StockQuantity");
            }
        }
        catch (Exception mex) { Console.WriteLine($"⚠ Migration skipped: {mex.Message}"); }

        // ── Lightweight migration: PartStock concurrency + audit columns on existing DBs ──
        try
        {
            var psCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = context.Database.GetDbConnection().CreateCommand())
            {
                if (cmd.Connection!.State != System.Data.ConnectionState.Open) cmd.Connection.Open();
                cmd.CommandText = "PRAGMA table_info(PartStocks);";
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) psCols.Add(reader.GetString(1));
            }
            if (!psCols.Contains("UpdatedAt"))
            {
                // SQLite ALTER ADD COLUMN requires a CONSTANT default (CURRENT_TIMESTAMP is rejected).
                context.Database.ExecuteSqlRaw("ALTER TABLE PartStocks ADD COLUMN UpdatedAt TEXT NOT NULL DEFAULT '1970-01-01 00:00:00';");
                context.Database.ExecuteSqlRaw("UPDATE PartStocks SET UpdatedAt = datetime('now') WHERE UpdatedAt = '1970-01-01 00:00:00';");
                Console.WriteLine("✅ Migration: added PartStocks.UpdatedAt");
            }
            if (!psCols.Contains("RowVersion"))
            {
                context.Database.ExecuteSqlRaw("ALTER TABLE PartStocks ADD COLUMN RowVersion TEXT NOT NULL DEFAULT '';");
                Console.WriteLine("✅ Migration: added PartStocks.RowVersion");
            }
        }
        catch (Exception mex) { Console.WriteLine($"⚠ PartStock columns migration skipped: {mex.Message}"); }

        // ── Lightweight migration: create PartUnit table on existing DBs (EnsureCreated only
        //    builds tables for a brand-new DB, so add it here for databases created earlier) ──
        try
        {
            context.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS PartUnits (
                    Id INTEGER NOT NULL CONSTRAINT PK_PartUnits PRIMARY KEY AUTOINCREMENT,
                    PartId INTEGER NOT NULL,
                    LocationId INTEGER NULL,
                    SerialNo TEXT NOT NULL,
                    Condition TEXT NOT NULL,
                    ExpiryDate TEXT NULL,
                    IsUnrepairable INTEGER NOT NULL,
                    ReceivedAt TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    CONSTRAINT FK_PartUnits_Parts FOREIGN KEY (PartId) REFERENCES Parts (Id) ON DELETE CASCADE,
                    CONSTRAINT FK_PartUnits_Locations FOREIGN KEY (LocationId) REFERENCES Locations (Id) ON DELETE SET NULL
                );");
            context.Database.ExecuteSqlRaw(
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_PartUnits_SerialNo ON PartUnits (SerialNo);");
        }
        catch (Exception mex) { Console.WriteLine($"⚠ PartUnit migration skipped: {mex.Message}"); }

        // ── Lightweight migration: add StockMovement.PartId (FK to Part) on existing DBs ──
        try
        {
            var smCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = context.Database.GetDbConnection().CreateCommand())
            {
                if (cmd.Connection!.State != System.Data.ConnectionState.Open) cmd.Connection.Open();
                cmd.CommandText = "PRAGMA table_info(StockMovements);";
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) smCols.Add(reader.GetString(1));
            }
            if (!smCols.Contains("PartId"))
            {
                context.Database.ExecuteSqlRaw("ALTER TABLE StockMovements ADD COLUMN PartId INTEGER NOT NULL DEFAULT 0;");
                Console.WriteLine("✅ Migration: added StockMovements.PartId");
            }
        }
        catch (Exception mex) { Console.WriteLine($"⚠ StockMovement.PartId migration skipped: {mex.Message}"); }

        // ── Lightweight migration: add PartId (FK to Part) to transaction tables on existing DBs ──
        foreach (var tbl in new[] { "GoodsReceiptLines", "ReturnRequests", "StockTransfers",
                                    "StockCountLines", "DisposalRequests", "AtmModelParts", "EquivalentGroupMembers" })
        {
            try
            {
                var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var cmd = context.Database.GetDbConnection().CreateCommand())
                {
                    if (cmd.Connection!.State != System.Data.ConnectionState.Open) cmd.Connection.Open();
                    cmd.CommandText = $"PRAGMA table_info({tbl});";
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read()) cols.Add(reader.GetString(1));
                }
                if (!cols.Contains("PartId"))
                {
                    context.Database.ExecuteSqlRaw($"ALTER TABLE {tbl} ADD COLUMN PartId INTEGER NOT NULL DEFAULT 0;");
                    Console.WriteLine($"✅ Migration: added {tbl}.PartId");
                }
            }
            catch (Exception mex) { Console.WriteLine($"⚠ {tbl}.PartId migration skipped: {mex.Message}"); }
        }

        // ── Migrate ImagePath from /uploads/parts/ → /assets/parts/ (one-time, idempotent) ──
        try
        {
            var updated = context.Database.ExecuteSqlRaw(
                "UPDATE Parts SET ImagePath = REPLACE(ImagePath, '/uploads/parts/', '/assets/parts/') WHERE ImagePath LIKE '/uploads/parts/%'");
            if (updated > 0) Console.WriteLine($"✅ Migrated {updated} ImagePath(s): /uploads/parts/ → /assets/parts/");
        }
        catch (Exception mex) { Console.WriteLine($"⚠ ImagePath migration skipped: {mex.Message}"); }

        // ── Users (login credentials) ──
        if (!context.Users.Any())
        {
            context.Users.AddRange(
                new User { Email = "admin@atm.com", Name = "Administrator", Role = "Admin", PasswordHash = PasswordHasher.Hash("admin123") },
                new User { Email = "tech@atm.com",  Name = "Technician",    Role = "Tech",  PasswordHash = PasswordHasher.Hash("tech123") }
            );
            context.SaveChanges();
            Console.WriteLine("✅ Seeded users: admin@atm.com / admin123  |  tech@atm.com / tech123");
        }

        // ── Categories (from GRG Parts Catalog) ──
        if (!context.Categories.Any())
        {
            var catNames = new[] {
                "AC Box",
                "Action Key",
                "Alarm",
                "Barcode Reader",
                "C/R External Capacitor",
                "CT",
                "Cabinet Parts",
                "Camera",
                "Card Reader",
                "Cassette",
                "Doc Scanner",
                "EPP",
                "FE Report",
                "Fingerprint reader",
                "IPC",
                "Key",
                "Keyboard",
                "MDM",
                "NF/NE Sensor",
                "NV Module",
                "PCB",
                "Pen",
                "Phone",
                "Power Supply",
                "Printer",
                "Safe Lock",
                "Screen Monitor",
                "Switch",
                "USB HUB",
                "Voice Amplifier",
            };
            foreach (var name in catNames)
                context.Categories.Add(new Category { Name = name, IsActive = true });
            context.SaveChanges();
            Console.WriteLine("✅ Categories seeded.");
        }

        // ── Parts (522 real GRG parts) ──
        if (!context.Parts.Any())
        {
            var catLookup = context.Categories.ToDictionary(c => c.Name, c => (int?)c.Id);
            int? C(string n) => catLookup.GetValueOrDefault(n);
            context.Parts.AddRange(new Part[] {
                new Part { PartNo="208010040-H", PartName="AC box/YT-AC BOX-001", CategoryId=C("AC Box"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011292", PartName="4x1 FKP - PCB", CategoryId=C("Action Key"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011292-H", PartName="4x1 FKP - PCB", CategoryId=C("Action Key"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="602010410001", PartName="4x1 FKP - metal casing", CategoryId=C("Action Key"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="602010410001-H", PartName="4x1 FKP - metal casing", CategoryId=C("Action Key"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="726021449", PartName="4x1 FKP - silicon rubber", CategoryId=C("Action Key"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="726021449-H", PartName="4x1 FKP - silicon rubber", CategoryId=C("Action Key"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="727020429001", PartName="4x1 FKP - button cap", CategoryId=C("Action Key"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="726011415-H", PartName="key cap", CategoryId=C("Action Key"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="724010207-10PCS", PartName="4x1 FKP - M4x10 tapping screw (10PCS./PACK)", CategoryId=C("Action Key"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="B. V030001", PartName="BOX ALARM", CategoryId=C("Alarm"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="R. V030002", PartName="REMOTE", CategoryId=C("Alarm"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="AD. V030004", PartName="ADAPTER", CategoryId=C("Alarm"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="SP. V030005", PartName="SPEAKER", CategoryId=C("Alarm"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="An.J V030006", PartName="ANTI JUMP", CategoryId=C("Alarm"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="SC. V030007", PartName="SET CABLE", CategoryId=C("Alarm"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="H. V030008", PartName="HEAT", CategoryId=C("Alarm"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="202060224", PartName="barcode reader(NT0810-1/for VTM)", CategoryId=C("Barcode Reader"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="202030277-H", PartName="2D BARCODE SCANNER NLS-FM30", CategoryId=C("Barcode Reader"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="202030008-H", PartName="\"2D barcode reader(N5683SR/HONEYWELL)", CategoryId=C("Barcode Reader"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="702040508", PartName="C/R External Capacitor MK-30V-P0.4FHS", CategoryId=C("C/R External Capacitor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="208030001-H", PartName="C/R External Capacitor C/REP-001", CategoryId=C("C/R External Capacitor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502015117-H", PartName="Card return on Power failure", CategoryId=C("C/R External Capacitor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="725041954", PartName="H68NL-829 CASH WITHDRAWAL", CategoryId=C("Camera"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="211010193-H", PartName="USB camera Logitech C920", CategoryId=C("Camera"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="211010195-H", PartName="USB WDR camera DV-U3303WCP205", CategoryId=C("Camera"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="211010408", PartName="GDZS-DC004-EJVL dual camera", CategoryId=C("Camera"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="211010253", PartName="camera(LHT-820CM301 7B/VTM /Linhuitong)", CategoryId=C("Camera"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502018807", PartName="SANKYO-0180 EASD module", CategoryId=C("Card Reader"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="501010429002", PartName="Anti-damage reader assembly", CategoryId=C("Card Reader"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502012281-H", PartName="Bezel assy.", CategoryId=C("Card Reader"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301010425-H", PartName="Sankyo Card Reader Control Board", CategoryId=C("Card Reader"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="708050003-H", PartName="Preread magnetic head", CategoryId=C("Card Reader"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502012560-H", PartName="Motor Assy", CategoryId=C("Card Reader"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="708050014-H", PartName="R/W Head Assy.(0188 type)", CategoryId=C("Card Reader"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="714070084-H", PartName="Capture Roller", CategoryId=C("Card Reader"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="202010014-H", PartName="Card Reader (Sankyo ICT3Q8-3A0179)", CategoryId=C("Card Reader"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="202010014", PartName="Card Reader (Sankyo ICT3Q8-3H0180-S)", CategoryId=C("Card Reader"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="202010017-H", PartName="Card Reader (Sankyo ICT3Q8-3A0179)", CategoryId=C("Card Reader"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="202011056-H", PartName="Card Reader (Sankyo ICT3Q8-3A0179)", CategoryId=C("Card Reader"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="203010188006-H", PartName="Deliver card module for D1-GRG", CategoryId=C("Card Reader"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301011536", PartName="IC CHIP MODULE(S57A595A/NIDEC SANKYO)", CategoryId=C("Card Reader"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010215003", PartName="CRM9250 AC Cassette(English)", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010215003-H", PartName="CRM9250 AC Cassette(English)", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301010146", PartName="CRM9250 AC control board", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301010146-H", PartName="CRM9250 AC control board", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011508002-H", PartName="AC Upper Plate Assy", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011535-H", PartName="AC base plate assembly", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011510-H", PartName="AC mid-plate assembly", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="401010900-H", PartName="AC-XS1 connector (male)", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="726010353-H", PartName="AC Top-plate", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="720110182-H", PartName="Cassette Lock -ABA", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011938-H", PartName="Note Cassette CDM8240-NC-001 (CNY100)", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502014949002", PartName="CRM9250N RC cassette (English, CNY100)", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301010700", PartName="Recycle Cassette Control Board", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="714110030", PartName="RC IMPELLER 1", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="714110031", PartName="RC IMPELLER 2", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502014949070", PartName="CRM9250NRC", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502014949052", PartName="RECYCLING CASSATTE", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010214009-H", PartName="Recycling cassette (English, spare part)", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="726010527-H", PartName="Belt-lock for Top plate belt", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="726010333-H", PartName="RC top plate", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="401010888-H", PartName="RC-XS1 connector (male)", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="726010528-H", PartName="Belt-lock for Pressure plate belt", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="726010332-H", PartName="RC base plate", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011518-H", PartName="RC pick roller assebmly", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011529-H", PartName="RC connector assembly", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301010133-H", PartName="Recycle Cassette Control Board", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="726010465-H", PartName="adjustor connector (right)", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="726010464-H", PartName="adjustor connector (left)", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="726010463-H", PartName="cassette adjust shim", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502014949070-H", PartName="CRM9250NRC", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010214007-H", PartName="CRM9250N  Recycling  Cassettes", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010214-H", PartName="CRM9250N Recycling Cassettes(EN)", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="50201494070", PartName="CRM9250N RECYCLING CASSETTE", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502014949002-H", PartName="CRM9250N RC cassette (English, CNY100)", CategoryId=C("Cassette"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="728022100", PartName="Hydraulic rod (450N)-快", CategoryId=C("Power Supply"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="726010376003-H", PartName="LCD Panel", CategoryId=C("Screen Monitor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010792", PartName="solenoid(CRM9250 NT/AC assy/with cable)", CategoryId=C("CT"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010793", PartName="CRM9250 CT solenoid of AC1 assy.(with cable)", CategoryId=C("CT"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010794", PartName="CRM9250 CT solenoid of RC1 assy.(with cable)", CategoryId=C("CT"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010795", PartName="CRM9250 CT solenoid of RC2 assy.(with cable)", CategoryId=C("CT"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010796", PartName="CRM9250 CT solenoid of RC3 assy.(with cable)", CategoryId=C("CT"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011424-H", PartName="transport-AC Lower plate B", CategoryId=C("CT"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011426-H", PartName="transport-RC Lower plate", CategoryId=C("CT"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011423-H", PartName="transport-AC Lower plate A", CategoryId=C("CT"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502014045-H", PartName="diverter (AC-CT) Generic Component", CategoryId=C("CT"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="711090500-H", PartName="Soleniod SMTR-3616LR16AA", CategoryId=C("CT"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010223002", PartName="Cash Transportation (Front service)", CategoryId=C("CT"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010223", PartName="CT MODULE (CRM9250/RS/ENGLISH) (Front service)", CategoryId=C("CT"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010223001", PartName="CT MODULE (CRM9250/RS/ENGLISH) (Front service)", CategoryId=C("CT"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010223001-H", PartName="CT MODULE (CRM9250/RS/ENGLISH) (Front service)", CategoryId=C("CT"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010222002", PartName="Cash Transportation (Rear service)", CategoryId=C("CT"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010222001", PartName="Cash Transportation (Rear service)", CategoryId=C("CT"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010223002-H", PartName="CRM9250 front access cassette channel assy", CategoryId=C("CT"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="203020184-H", PartName="Doc Scanner&Drop Box HSM-003", CategoryId=C("Doc Scanner"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="720110140-H", PartName="KEY LOCK (MS864-4)(8301)", CategoryId=C("Cabinet Parts"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="720110141-H", PartName="KEY LOCK (MS864-4)(8302)", CategoryId=C("Cabinet Parts"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="720110146-H", PartName="KEY LOCK (MS864-4)(8307)", CategoryId=C("Cabinet Parts"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="720110147-H", PartName="KEY LOCK (MS864-4)(8308)", CategoryId=C("Cabinet Parts"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="720110148-H", PartName="KEY LOCK (MS864-4)(8309)", CategoryId=C("Cabinet Parts"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="720110145-H", PartName="Cabinet door lock (MS864-4) (8306)", CategoryId=C("Cabinet Parts"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="206010182018", PartName="EPP-004 Keyboard - English and Thai", CategoryId=C("EPP"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="206010182020-H", PartName="EPP004 Keypad", CategoryId=C("EPP"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="206010007016-H", PartName="EPP003 Keypad", CategoryId=C("EPP"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="206010182-H", PartName="EPP-004 KEYPAD (CHINESE/ENGLISH VERSION)", CategoryId=C("EPP"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="206010182018-H", PartName="EPP-004 Keyboard - English and Thai", CategoryId=C("EPP"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010115", PartName="ADDA BIG FAN ASSEMBLY (WITH CABLE)", CategoryId=C("Power Supply"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="725041951", PartName="FAN SUPPORT 1", CategoryId=C("Power Supply"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="401027882", PartName="H68NL-829 LOWER FAN CABLE", CategoryId=C("Power Supply"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="CER111-5PCS", PartName="CE report (5PCS./PACK)/SVOA", CategoryId=C("FE Report"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="CER112-5PCS", PartName="CE report (5PCS./PACK)/D1", CategoryId=C("FE Report"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="202030464", PartName="Finger Printer/FPR622(HID)", CategoryId=C("Fingerprint reader"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="202030012001-H", PartName="Fingerprint reader (FPR-622/V501A)", CategoryId=C("Fingerprint reader"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="725040564", PartName="PANEL METAL HINGE", CategoryId=C("Cabinet Parts"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301010337001", PartName="IO EXPANDING BOARD (12 PORTS)", CategoryId=C("PCB"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301010337001-H", PartName="IO EXPANDING BOARD (12 PORTS)", CategoryId=C("PCB"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301010830003-H", PartName="IO SIGNAL CONTROL BOARD", CategoryId=C("PCB"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214010191002", PartName="IPC-014(i3/3.3G/4GM/2* 1T HD)", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214040067", PartName="IPC MAIN BOARD(GDYT-IMB-H61 2B)", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214040067-H", PartName="IPC MAIN BOARD(GDYT-IMB-H61 2B)", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214050013", PartName="CPU（Core i3 3220 3.3 ntel)", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214050013-H", PartName="CPU（Core i3 3220 3.3 ntel)", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214030022", PartName="RAM(4GB,DDR3,1333,Kingstone)", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214030022-H", PartName="RAM(4GB,DDR3,1333,Kingstone)", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214100012", PartName="DVD ROM (iHAS324)", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="208010187", PartName="POWER SUPPLY (FSP300-70PFL)", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="208010187-H", PartName="POWER SUPPLY (FSP300-70PFL)", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="711100506", PartName="CPU Fan", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="711100033", PartName="IPC-011 Case Fan(RDH1225B)", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214020210", PartName="Seagate ST1000DM010", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214020210-H", PartName="Seagate ST1000DM010", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214010191-H", PartName="IPC-014（I3 3.3G 4GM/500G*2HD）", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214010190-H", PartName="IPC-013 (i3/4GM/500G*2HD)", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214010190018-H", PartName="IPC-013(i3-3220 4GM 1THD)", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214010191022-H", PartName="IPC-014(I5-3550 4GM/1T HD/IEI)", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214020209-H", PartName="hard disk/ST500DM009/seagate", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214020005014-H", PartName="Harddisk (Seagate/ST3250312AS/500G)", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214050012-H", PartName="CPU(Core i5 3550 3.3G Intel)", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214100011-H", PartName="DVDROM(iHAS124)", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214030182-H", PartName="Kingston 4GB DDR3 1600MHz", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214090182-H", PartName="Graphic Card", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214040185-H", PartName="IEI embedded PC board KINO-CV-D25501", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214040185002-H", PartName="KINO-CV-D25501(MB+500G HDD+4GM+cable)", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214010191001-H", PartName="IPC-014 (i5/4GM/500G*2HD)", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214010191002-H", PartName="IPC-014(I5-3550 4GM 1T HD/IEI)", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214010016019-H", PartName="IPC-GRGB (Computer) CPU:E5300 RAM2G HDD250G", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214040198-H", PartName="IPC Mainboard", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214010191012", PartName="IPC-014 (I5-3550/4GM/2-1THD/IEI)", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214010191001", PartName="IPC-014(i3/3.3G/4GM/2* 1T HD)", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214010194-H", PartName="IPC-014 (I5/4GM/500GHD)", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214011217", PartName="IPC-014(i3/3.3G/4GM/ no HDD)", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214110364-H", PartName="IPC-014 (i5/ 4GM/ no HDD)", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214040210-H", PartName="GRG-S7018(4GM/128 G/mSATA/with cable)", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214040209-H", PartName="PC(GRG-S7015-02(4GM/128GSSD/VGA))", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214110364", PartName="IPC-014 (i5/ 4GM/ no HDD)", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214110365-H", PartName="IPC-013 (i3/ 4GM/ no HDD)", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214010191020", PartName="IPC-014(I3-3220 3.3G 8GM)", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="720110168", PartName="ABA lock,9439(with different keys)", CategoryId=C("Key"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="720110171", PartName="ABA lock 9249(common key)", CategoryId=C("Key"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="720110171-H", PartName="ABA lock 9249(common key)", CategoryId=C("Key"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214060187", PartName="Keyboard DS-2120K", CategoryId=C("Keyboard"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010240", PartName="Link transportation(Rear service)", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010240-H", PartName="Link transportation(Rear service)", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010210", PartName="Link transportation（front service)", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010210-H", PartName="Link transportation（front service)", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011459-H", PartName="LT foreside assembly", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010227002", PartName="CRM9250(FS/EN/OD S/4 NRC/UNV/WITH LOCK) Lower unit", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301010117", PartName="CRM9250 Main Board", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301010117-H", PartName="CRM9250 Main Board", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301010339", PartName="NV power board", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301010339-H", PartName="NV power board", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="402020038", PartName="MF08(motor with Cable)", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="402020038-H", PartName="MF08(motor with Cable)", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010742", PartName="Step Motor Assy(CRM9250/RC2)", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010743", PartName="Step Motor Assy(CRM9250/RC3)", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010744", PartName="Step Motor Assy(CRM9250/RC4)", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010810", PartName="SMHP step motor cable", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="711060008", PartName="steo motor/STP-60D3007GAN", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011091-H", PartName="RC4DC motor assy-RC4", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011455-H", PartName="Channel main motor assy.", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011088-H", PartName="RC1 Base board DC motor assy", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011089-H", PartName="CRM9250 motor assemblyRC2", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011090-H", PartName="RC3DC motor assy-RC3", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="401010890-H", PartName="AC connector (female)", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011093-H", PartName="CRM9250 cassette motor assembly RC3", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="401010892-H", PartName="RC1 connector (female)", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="401010894-H", PartName="RC2 connector (female)", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301010145-H", PartName="ODS control board", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="401010898-H", PartName="RC4 connector (female)", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="711060005-H", PartName="Step Motor STP-59D5055GAN", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011457-H", PartName="MC06 (Motor with Cable and Gear)", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011649-H", PartName="MC01(Motor with Cable and Gear)", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011450-H", PartName="MC07", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011092-H", PartName="RC2 motor assy_CRM9250", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011454-H", PartName="MC02(with Cable and Gear)", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011094-H", PartName="CRM9250 cassette motor assembly RC4", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="401010896-H", PartName="RC3 connector (female)", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="401010890", PartName="AC/XS11", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="401010892", PartName="RC1/XS10", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="401010894", PartName="RC2/XS12", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="401010896", PartName="RC3/XS14", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="401010898", PartName="RC4/XS16", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011091", PartName="RC4DC motor assy-RC4", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011090", PartName="RC3DC motor assy-RC3", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011089", PartName="CRM9250 motor assemblyRC2", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011088", PartName="RC1 Base board DC motor assy", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="208010056-H", PartName="Power Box(AD901M36-4M1A)", CategoryId=C("Power Supply"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="208010182-H", PartName="Power Supply(GPAD132M36-4A)", CategoryId=C("Power Supply"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="208010056", PartName="Power Supply (AD901M36-4M1A)", CategoryId=C("Power Supply"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="208010182", PartName="Power Supply(GPAD132M36-4A)", CategoryId=C("Power Supply"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="DD2FWB4AB5XFFB2X-H", PartName="MDM i9000S", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="NBC9000S-H", PartName="Battery Charger for i9000S", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="HBL9000S-H", PartName="Battery for I9000s", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010218003", PartName="CR9250, NE temporary storage assembly", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301010116", PartName="CRM9250 Upper Board", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301010116-H", PartName="CRM9250 Upper Board", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010791", PartName="LPS TOMOR PE02", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010791-H", PartName="LPS TOMOR PE02", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010798", PartName="MPS TOMOR PE01", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010798-H", PartName="MPS TOMOR PE01", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010799", PartName="LPS MOTOR PE03", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010799-H", PartName="LPS MOTOR PE03", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010807", PartName="ME01 stepper motor own cable", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010807-H", PartName="ME01 stepper motor own cable", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010808", PartName="ME02 stepping motor", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010809", PartName="ME03 stepper motor own cable", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010809-H", PartName="ME03 stepper motor own cable", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010218001-H", PartName="Note Escrow", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011553-H", PartName="ME02(with Cable and Gear)", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011552-H", PartName="ME01 and MF03(with Cable and Gear)", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011036-H", PartName="NE Entrance Guide-plate Component", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010233-H", PartName="NE tri-section module", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011546-H", PartName="NE rotary Solenoid assembly", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="715010165-H", PartName="tape (with black end)", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="715010166-H", PartName="tape (no black end)", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010218002-H", PartName="Note Escrow", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301010396-H", PartName="UI control board", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="708030034", PartName="MOD_SENSOR_U_SG2 48", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="708030012-H", PartName="U-type Sensor/OJ-551-A5/ALEPH", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011340-H", PartName="MF05 (NF main Motor with Cable and Gear)", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010806-H", PartName="MF05 stepper motor own cable", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="402020009-H", PartName="NF cable assembly", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="708030048", PartName="SENSOR (G310/ EMITTER)", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="708030048-H", PartName="SENSOR (G310/ EMITTER)", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="708030049", PartName="SENSOR (DIG310D/ RECEIVER)", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="708030049-H", PartName="SENSOR (DIG310D/ RECEIVER)", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="708030524", PartName="Photoelectric position sensor /SIS-PT16/", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="402020022-H", PartName="Motor cable", CategoryId=C("NF/NE Sensor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010217", PartName="Note feeder Lower", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010217-H", PartName="Note feeder Lower", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="708030016-H", PartName="Coder (OSS-05-2C)", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010242-H", PartName="Note-Reject Channel(CRM9250 NF)", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011368-H", PartName="rotary Solenoid", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011337-H", PartName="NF rotary Solenoid assembly", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010790-H", PartName="MOTOR PF02", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010216001", PartName="CRM9250 recycling shaft assembly", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010284", PartName="Note Feeder Upper Module(CRM9250)", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010284-H", PartName="Note Feeder Upper Module(CRM9250)", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010745", PartName="MF06 (shutter motor with Cable)", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010745-H", PartName="MF06 (shutter motor with Cable)", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010789", PartName="Solenoid_DLS-PF01(Z M)", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010802", PartName="MF01 stepper motor own cable", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010803", PartName="MF02 stepper motor own cable", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010803-H", PartName="MF02 stepper motor own cable", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010804", PartName="CRM9250 MF03 Assy.(with cable)", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010805", PartName="MF04 stepper motor own cable", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010805-H", PartName="MF04 stepper motor own cable", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010811", PartName="CRM9250 MF07 motor Assy", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="711060003", PartName="Step Motor(STH-39D2011GAN)", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="714070042", PartName="NOTE FEEDER RUBBER", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="714070042-H", PartName="NOTE FEEDER RUBBER", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011344-H", PartName="MF07 (pusher plate motor with Cable)", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011379-H", PartName="MF02 (Motor with Cable and Gear)", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011387-H", PartName="MF04 (Motor with Cable and Gear)", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010211-H", PartName="Note Feeder Slot", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010221-H", PartName="Note Feeder Transport", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010216-H", PartName="Slot Shutter (CRM9250-ss-001)", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010216", PartName="SHUTTER ASSY (CRM9250)", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="501010506-H", PartName="CM400 NV ASSY", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301010176-H", PartName="THICKNESS SENSOR BOARD", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010292002-H", PartName="Note Validator Module(2nd generation)", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="801060527-H", PartName="Stylus", CategoryId=C("Pen"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="213050182-H", PartName="Coil Winding Controller (Phone Cable)", CategoryId=C("Phone"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="208010276", PartName="POWER SUPPLY (PSU-3203)", CategoryId=C("Power Supply"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="208010238", PartName="POWER SUPPLY (GW-EP500WV36AA V02)", CategoryId=C("Power Supply"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="208010063-H", PartName="Power Supply GPAD431M36-1E", CategoryId=C("Power Supply"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="208010006-H", PartName="Power Supply(AD321M36-4M1320W)", CategoryId=C("Power Supply"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="208010065-H", PartName="Power Supply(GPAD311M36-4B)", CategoryId=C("Power Supply"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="207040197", PartName="THERMAL RECEIPT PRINTER(TRP-006R)", CategoryId=C("Printer"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="716010197", PartName="OD24, PRINTER PAPER SPINDLE", CategoryId=C("Printer"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="716010197-H", PartName="OD24, PRINTER PAPER SPINDLE", CategoryId=C("Printer"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301010904", PartName="TRP-006R RECEIPT PRINTER CONTROL BOARD", CategoryId=C("Printer"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502015969", PartName="TRP-006R printer sensor and motor cable", CategoryId=C("Printer"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="711010513", PartName="Thermal printer head/PT72AS-A/Pratt", CategoryId=C("Printer"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301010144001-H", PartName="TRP-005 control board_package material", CategoryId=C("Printer"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="711010021-H", PartName="TRP-003 print headBT-T080", CategoryId=C("Printer"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301010131001-H", PartName="USB TRP Control Board", CategoryId=C("Printer"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="711010018-H", PartName="TRP-005 Print headLTP2242", CategoryId=C("Printer"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="207040007005-H", PartName="TRP-003R", CategoryId=C("Printer"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="207040005-H", PartName="USB Thermal Receipt Printer/TRP-005", CategoryId=C("Printer"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301010232001-H", PartName="Paper Low Level Sensor 401", CategoryId=C("Printer"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="801070044", PartName="Thermal Receipt Printer(Slip paper)", CategoryId=C("Printer"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="207040198-H", PartName="TRP-006A SHORT THERMAL RECEIPT PRINTER", CategoryId=C("Printer"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="801070044-1", PartName="Thermal Receipt Printer(Slip paper) (9Roll/Box)", CategoryId=C("Printer"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="801070044-2", PartName="Thermal Receipt Printer(Slip paper) (3Roll/Box)", CategoryId=C("Printer"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="201011003-H", PartName="STAMP MACHINE (SM-002)", CategoryId=C("Printer"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="718010020", PartName="LIGHT RAIL (3507-524)", CategoryId=C("Cabinet Parts"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="718010066", PartName="36-inch EXTENDED 4-inch RAIL(C3551-109)", CategoryId=C("Cabinet Parts"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="718010059", PartName="34-inch EXTENDED 6-inch HEAVY DUTY RAIL", CategoryId=C("Cabinet Parts"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="718010040", PartName="28-inch LIGHT LEFT RAIL (3732-207-L)", CategoryId=C("Cabinet Parts"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="718010041", PartName="28-inch LIGHT RIGHT RAIL (3732-207-R)", CategoryId=C("Cabinet Parts"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="215030222", PartName="10-inch TOUCH SCREEN (MON-1001)", CategoryId=C("Screen Monitor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="202020228", PartName="RF-2G Creator", CategoryId=C("Card Reader"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="401045275", PartName="RF reader signal cable", CategoryId=C("Card Reader"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="216010006", PartName="Mechanical lock (LAGARD/3390+1777)", CategoryId=C("Safe Lock"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="216010006001-H", PartName="LAGARD LOCK 3390 and 1777", CategoryId=C("Safe Lock"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="216020190", PartName="TAIZHOU LOCK(8K-2)", CategoryId=C("Safe Lock"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="216010006001", PartName="LAGARD PASSWORD LOCK (3390+1777)", CategoryId=C("Safe Lock"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="216020185-H", PartName="KABA KEY LOCK 71111", CategoryId=C("Safe Lock"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="215030194-H", PartName="10.4 inch touch LCD HL1002", CategoryId=C("Screen Monitor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="215040005-H", PartName="15\" Touch Screen(TSC-004TL6B15D2WS)", CategoryId=C("Screen Monitor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="215030199001-H", PartName="15-inch touch disply/ X-AU15T2525LE02", CategoryId=C("Screen Monitor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="215050247", PartName="Touch screen/MON-1 501(NCB03)", CategoryId=C("Screen Monitor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="401013175", PartName="VGA cable", CategoryId=C("Screen Monitor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="215030300", PartName="TOUCH SCREEN (MON-1501/NCB01)", CategoryId=C("Screen Monitor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="215050185-H", PartName="LCD HL1513N(AUO LED)", CategoryId=C("Screen Monitor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="721030420-H", PartName="Privacy Film", CategoryId=C("Screen Monitor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="215030189-H", PartName="21.5'' anti-peep touch screen", CategoryId=C("Screen Monitor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="215020194-H", PartName="LCD Module/HL1558-NO1", CategoryId=C("Screen Monitor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="215030300-H", PartName="TOUCH SCREEN MODELL:MON-1501", CategoryId=C("Screen Monitor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="215020006-H", PartName="21.5\" LCD(Bigtide/HL2102)", CategoryId=C("Screen Monitor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="215030315-H", PartName="21.5 inch Touch Screen HL2111B", CategoryId=C("Screen Monitor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="215030237-H", PartName="TOUCH SCREEN#OTL193-RPC03(F)-UCD-1194", CategoryId=C("Screen Monitor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301011074", PartName="AD BOARD(ATM_DVI/VGA2281)", CategoryId=C("Screen Monitor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="711110046", PartName="DPDT switch 2DM409(Honeywell)", CategoryId=C("Switch"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="711110046-H", PartName="DPDT switch 2DM409(Honeywell)", CategoryId=C("Switch"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="711110045-H", PartName="DPDT switch 1DM401(Honeywell)", CategoryId=C("Switch"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502021657", PartName="UNV MODULE", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502017581", PartName="9250-UNV(MDT+HEC)", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010237004", PartName="CRM9250(FS/EN/OD S/4 NRC/UNV/WITH LOCK) Upper unit", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="725030904002", PartName="CRM9250 upper unit lock cover (including 725030903 lock bolt and 720110171 key lock)", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010237002", PartName="CRM9250 (FS/EN/OD S/4 NRC/UNV/WITH LOCK) Upper unit", CategoryId=C("MDM"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="401027879", PartName="H68NL-829 UPS INPUT EXTENSION CABLE", CategoryId=C("Power Supply"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="401027880", PartName="H68NL-829 UPS OUTPUT EXTENSION CABLE", CategoryId=C("Power Supply"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="401044864", PartName="H68NL-829 UPS SERIAL EXTENSION CABLE", CategoryId=C("Power Supply"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="9103-53862XG1-00", PartName="UPS", CategoryId=C("Power Supply"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="501010005-H", PartName="USB HUB(MOXA,407)", CategoryId=C("USB HUB"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="210010005-H", PartName="Audio Amplifier AAP-002", CategoryId=C("Voice Amplifier"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301010348001-H", PartName="VTM Voice Amplifier Board", CategoryId=C("Voice Amplifier"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="725041955", PartName="H68NL-829 MONITOR LENS PRESS BOARD", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="210010183-H", PartName="Speakers M18", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="202030001001-H", PartName="Proximity sensor", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="202030436", PartName="QR CODE SCANNING MODULE (N5680SR-BR0-215)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="711080012", PartName="Rotary Solenoid", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011340", PartName="NF MAIN MOTOR (CRM9250/MF05)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="208010022-H", PartName="Power Supply(Detla/DPS-60PBAA00)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="711060049-H", PartName="Step motor(42_STP-45D0009)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="724080029-H", PartName="Non-solid pin 2.5*20", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="501010945-H", PartName="Sound Pick-up(MIC-001)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="721030421-H", PartName="60° Privacy Filter /B-HDPV400AG", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="211030190-H", PartName="HUMAN APPROACH SENSOR TAD-9128", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011086-H", PartName="motor assy.", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010243-H", PartName="LT pusher plate (left)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="725017325-H", PartName="Connector guide-plate", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010244-H", PartName="LT pusher plate (right)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="715030021-H", PartName="Belt 141*6,S3M", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="715010055-H", PartName="Flat Belt, SE-N-SMV1,10*429*0.65", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="724080028-H", PartName="Non-solid pin 2.5X14(thichness=0.5)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="401014790-H", PartName="H22NL core USB signal cable", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="708040006-H", PartName="Sensor FI-406GB11", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="714020040-H", PartName="Bevel Gear (0818-m1.5)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="202010012-H", PartName="ANTI LEBANESE HOOK DEVICE", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="721010055-H", PartName="Prism D34", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="715010067-H", PartName="Belt 372 X 12 X 0.65", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="803030019-H", PartName="Calibration Note", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="724050011-10PCS", PartName="COMBINATION SCREW M4X8 (10PCS./PACK)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="724010102-10PCS", PartName="ROUND HEAD CROSS SCREW M3X16 (10PCS./PACK)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="724010134-10PCS", PartName="M3*6 COMBINATION SCREW (10PCS./PACK)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="724010615-10PCS", PartName="SCREW M4X45 (10PCS./PACK)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="724040110-10PCS", PartName="M6 WASHER (10PCS./PACK)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="501010945", PartName="Sound Pick-up(MIC-001)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="602011701-H", PartName="LOCK CATCH 2 (for BCA)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="602020306-H", PartName="LOCK  CATCH  1 (for BCA)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="728021631-H", PartName="LOCK SHAFT 1 (FOR BAC)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="728021632-H", PartName="LOCK SHAFT 2 (FOR BCA)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="012345678-H", PartName="เสา", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="123456789-H", PartName="ฐาน", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="725012339-H", PartName="LCD PANEL SIDE FIXING PLATE 1", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="725012340-H", PartName="LCD panel up fixing plate", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="801080004-H", PartName="Rubber bar", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="801080012-H", PartName="Waterproof Ring(4MM*9MM)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="721010104", PartName="Human Proximity Sensor Glass", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="211030190", PartName="#Human Approach Sensor TAD-9128", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214040210", PartName="GRG-S7018 (4GM/128G/mSATA/with cable", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="715010067", PartName="Belt 372 X 12 X 0.65", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="401045259", PartName="QR CODE SCANNER USB CABLE", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301011647", PartName="TRP-006R RECEIPT PRINTER CONTROL BOARD", CategoryId=C("Printer"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214020393", PartName="SATA (ST1000DM014/3.5 inch/7200R/1T/256M)", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010741", PartName="Step Motor Assy(CRM9250/RC1)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010284005", PartName="CRM9250 NF UPPER MODULE(ENGLISH)", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="US-5015LZ", PartName="CAT 5E RJ45 - RJ45 PATCH CORD, LSZH 5 M.", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="US-5101", PartName="CAT 6 UTP PATCH CORD 5 M.", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301011075", PartName="TOUCH CONTROL BOARD(YTG- G18O06)", CategoryId=C("Screen Monitor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="714110033", PartName="(Set)RC IMPELLER 1+2 w BUCKLE", CategoryId=C("NV Module"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214020669", PartName="SSD(V310 1024G-4fLC20/2.5)#", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214030189", PartName="Kingston memory(KVR16N11S 8/4/)", CategoryId=C("IPC"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="215050240", PartName="15-inch DISPLAY(MON-1501/ NLT/NCB01)", CategoryId=C("Screen Monitor"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301011993", PartName="CRM9250 UPPER CONTROL BOARD(GD32)", CategoryId=C("PCB"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301011994", PartName="CRM9250 MAIN CONTROL BOARD(GD32)", CategoryId=C("PCB"), MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301010399-POC", PartName="Headphone Jack", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="724010200-POC", PartName="M4*12 tapping cross screw", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="724050011-POC", PartName="COMBINATION SCREW 3 (M4x8)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="725020014-POC", PartName="AUDIO ADAPTER FIXING PLATE 2", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="3101173TE-1K", PartName="UPS Syndome", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="206010182020-IC", PartName="EPP004 Key Pad (English Version)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502018034-IC", PartName="ANTI-FISHING HOOK(Sankyo3H/ICT0H0-0301)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301010583-IC", PartName="CDM8240 main board(6NC)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011774-IC", PartName="Note Transporation", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011941-IC", PartName="NOTE STACKING MODULE", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011776-IC", PartName="Note presenter/ NP(short)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="715010054-IC", PartName="FLAT BELT (SE-N-SMV1,10X304X0.65)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="715010050-IC", PartName="FLAT BELT (SE-N-SMV1/10X184X0.65)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="715010048-IC", PartName="FLAT BELT (SE-N-SMV1/10X214X0.65)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="715010047-IC", PartName="FLAT BELT (SE-N-SMV1/10X367X0.65)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="715010046-IC", PartName="FLAT BELT (SE-N-SMV1/10X548X0.65)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="715010049-IC", PartName="T_TRANSPORT_OUTSIDE_2ND_BELT", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011875-IC", PartName="CDM THICKNESS SENSOR ASSY", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="714110041-IC", PartName="IMPELLER 0870", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="726020094-IC", PartName="PLASTIC SLIDER", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="724010021-IC", PartName="SCREW M4X5", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="715010035-IC", PartName="FLAT BELT (SE-N-SMV1/10X282X0.65)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="715010034-IC", PartName="FLAT BELT (SE-N-SMV1/10X235X0.65)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="714110044-IC", PartName="EPDM CASH PICK-UP GUM WHEEL", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011811-IC", PartName="NF MOTOR ASSY (WITH CABLE)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="602010886-IC", PartName="NS LOWER SHRAPNEL WELDING ASSY (CDM8240)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011945-IC", PartName="MOTOR_TG_05IA_SR_28_CN089_ASSY", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="726010127-IC", PartName="NC FRAME NOTE CASSETTE GUIDE BAR", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011775-IC", PartName="Note presenter/ NP(long)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="202011056-IC", PartName="CARD READER (SANKYO/ ICT3Q8-3H0180-S)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="206010182018-GA", PartName="EPP-004 KEYPAD (ENGLISH/THAI VERSION)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214040210-GA", PartName="GRG-S7018(4GM/128G/mSATA/with cable)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="215050240-GA", PartName="15-inch DISPLAY(MON-1501/NLT/NCB01)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011771-GA", PartName="CDM Note Feeder", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502011775-GA", PartName="Note presenter/ NP(long)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214010946-GA", PartName="IPC-102-03DX(i5-6500/8/256GSDMS）", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="215050235-GA", PartName="15-INCH SCREEN/MON-1501(NLT,CB01)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="202011014-GA", PartName="CARD READER (CRT-350-NJ10)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="205011004001-GA", PartName="WITHDRAWAL  SHUTTER (WST-004)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="207040197002-GA", PartName="THERMAL RECEIPT PRINTER(TRP-006R)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="208010276-GA", PartName="POWER SUPPLY (PSU-3203)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="211010408-GA", PartName="BINOCULAR CAMERA(GDZS-DC004-EJVL)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="211010662-GA", PartName="USB Camera DV-U3303WP205AD", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="216010006-GA", PartName="LAGARD PASSWORD LOCK (3390+1777)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="216020190002-GA", PartName="TAIZHOU LOCK(FDS-B-8K-2)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502014572-GA", PartName="MAIN BOARD (CDM8240N)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502014469001-GA", PartName="CDM8240N TRANSPORT ASSY (FS)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502010240-GA", PartName="Link transportation (Rear Service)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="210020232-GA", PartName="SPEAKER(LF-501S)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="202011056-GA", PartName="CARD READER (SANKYO/ ICT3Q8-3H0180-S)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502018034-GA", PartName="ANTI-FISHING HOOK(Sankyo3H/ICT0H0-0301)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="725046621-GA", PartName="IPC-102-03YH REAR FIXED PLATE", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="208010270-GA", PartName="POWER SUPPLY(GW-FLX1800SSA/180W)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301011640-GA", PartName="IO SIGNAL CONTROL BOARD", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502014463-GA", PartName="NOTE FEEDER(CDM8240N)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502014499-GA", PartName="Short presenters of note delivery module", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502014501-GA", PartName="Long presenters of note delivery module", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="202020376-GA", PartName="CARD READER(CRT-603-P200-001)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214020527-GA", PartName="HARD DISK(AirDisk/AMF10/3.0 INCH/256G)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="215030222-GA", PartName="10-inch TOUCH SCREEN (MON-1001)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="725039953-GA", PartName="SPEAKER BRACKET", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214040316-GA", PartName="MIANBOARD/GRG-M1063/H110/10×USB/10×COM", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="401042853-GA", PartName="SPEAKER SIGNAL EXTENSION CABLE", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="202060389-GA", PartName="Barcode Scanner/NT0810H", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502015962001-GA", PartName="CDM8240N TRANSPORT STRUCTURE MODULE", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="726012883005-GA", PartName="H22V CASH-OUT SLOT PANEL", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="213040191003-GA", PartName="SECURED CARD SLOT MODULE (SCM-001A/GD）", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="211010408001-GA", PartName="BINOCULAR CAMERA(GDZS-DC004-EJVL)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="726012890013-GA", PartName="DISPLAY PANEL", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502014487-GA", PartName="Reject Vault", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502014465-GA", PartName="CDM8240N Note Cassette", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="401026898-GA", PartName="SPEAKERPOWER CABLE", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="SG1KT-XP PLUS", PartName="UPS ESSCO", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="201020185046-GA", PartName="CDM8240N(FS,SHORT,4NC)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="201020186034-GA", PartName="CDM8240N(RS, Long,no NV,4 NC )", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214030332-GA", PartName="RAM(HSEU408G2666C19B1/DDR4/8G) C19B1/DDR4/8G)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214050201-GA", PartName="CPU(i5-6500/3.2GHz/INTEL)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214020503-GA", PartName="HDD(WD10EZEX/7200RPM/1TB)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214011490-GA", PartName="IPC-102-03DX(i5-6500/16/2X1TSD)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214020564-GA", PartName="SSD(XS300S001T/1T/SATA3 2.5)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214011665-BA", PartName="IPC-102-04DX i5-9500 16G 2*1TSSD", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301010729-GA", PartName="MAIN BOARD (STANDARD/CDM8240N)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214030330-BA", PartName="RAM(KTFGU4NEF/DDR4/UDIMM/16G)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301011988-GA", PartName="CDM8240N MAIN CONTROL BOARD(GD)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214020560-BA", PartName="V310-1024G-SSDV04dBB40A1024A00", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="215030536-GA", PartName="TOUCH SCREEN/MON-1501/6CB01/8BOE", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="401016075-GA", PartName="CDM8240N SINGLE DIVERTER CABLE XS6", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="401016243-GA", PartName="CDM8240N BUNDLE DEVERTER CABLE XS7", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502014575-GA", PartName="CIS rear transport light plate ass", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="726012377003-GA", PartName="CDM8240N STACKER MECHANISM", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="GLOBA20241004", PartName="Battery 12V-7.2AH GLOBA Power (2 Pcs/1 Pack)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="215030222002-GA", PartName="10 Inch TOUCH SCREEN(MON-1001-1CA02)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="202011161003-GA", PartName="CARD READER CRT-350-NJ10-GS1", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502014576-GA", PartName="EDDY CURRENT THICKNESS ASSY", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="201020185103-GA", PartName="CDM8240N(FS/SHORT NP/2 NC/NEW CHANNEL)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301010252-GA", PartName="SENSOR BOARD (CASSETTE LOW DETECT/FOR CDM MODULE)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="401016259-GA", PartName="8240N CASSETTE LOWER LEVE AND ID CABLE", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="213040191002-GA", PartName="SECURED CARD SLOT MODULE (SCM-001A）", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="214100011-GA", PartName="DVD ROM(LITEON/IHAS124)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="725050389-GA", PartName="QR CODE BRACKET", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301010367-GA", PartName="CDM CASSETTE LOWER LEVE AND ID BOARD", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301010399-GA", PartName="Headphone Jack", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="301010902-GA", PartName="MTS CONTROL BOARD", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="726012433-GA", PartName="CDM8240N Buckle Handle", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="725051110-GA", PartName="Microswitch bracket", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="725033891-GA", PartName="DOOR SWITCH DOME", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="205011004002-GA", PartName="WITHDRAWAL  SHUTTER (WST-004)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="722022383-GA", PartName="CASH WITHDRAWAL LABEL(ENGLISH/H22V)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="716014179-GA", PartName="386 BEARING (FOR CRT-350/5x8/METAL)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="603010310001-GA", PartName="STACKING GUIDE ROLLER FIXING FRAME ASSY", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="502019975", PartName="CONTACT ASSY(CRT-350N)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
                new Part { PartNo="715030475-GA", PartName="CRT-350N BELT(160TN15)", CategoryId=null, MinStock=2, MaxStock=20, ReorderPoint=5, CostPerUnit=0, IsActive=true },
            });
            context.SaveChanges();
            Console.WriteLine("✅ Parts seeded (522).");
        }


        // ── Locations ──
        if (!context.Locations.Any())
        {
            context.Locations.AddRange(
                new Location { Name = "DHL Center Bangkok",      Code = "DHL-BKK",  LocationType = "DHL_CENTER" },
                new Location { Name = "Ratchaburana Warehouse",  Code = "WH-RAT",   LocationType = "RATCHABURANA" },
                new Location { Name = "GRG Bangkok Hub",         Code = "GRG-BKK",  LocationType = "GRG" },
                new Location { Name = "Suvarnabhumi Airport",    Code = "APT-BKK",  LocationType = "AIRPORT" },
                new Location { Name = "Scrap / Disposal Yard",   Code = "SCRAP-01", LocationType = "SCRAP" },
                new Location { Name = "On-hand Technician Stock", Code = "OL-TECH", LocationType = "OL_TECHNICIAN" },
                new Location { Name = "In-Transit",              Code = "TRANSIT-01", LocationType = "IN_TRANSIT" }
            );
            context.SaveChanges();
            Console.WriteLine("✅ Locations seeded.");
        }

        // ── Vendors ──
        if (!context.Vendors.Any())
        {
            context.Vendors.AddRange(
                new Vendor { Name = "GRG Banking Equipment (Thailand)", Code = "GRG",  VendorType = "GRG",   ContactInfo = "grg-th@grg.com" },
                new Vendor { Name = "ATM Parts Co., Ltd.",              Code = "APC",  VendorType = "LOCAL", ContactInfo = "purchase@atmparts.co.th" },
                new Vendor { Name = "Siam Electronic Supply",           Code = "SES",  VendorType = "LOCAL", ContactInfo = "order@siamelectronic.com" }
            );
            context.SaveChanges();
            Console.WriteLine("✅ Vendors seeded.");
        }

        // ── SystemSettings (single row, controls freeze for annual stock count) ──
        if (!context.SystemSettings.Any())
        {
            context.SystemSettings.Add(new SystemSettings { Id = 1, IsFrozen = false });
            context.SaveChanges();
            Console.WriteLine("✅ SystemSettings seeded.");
        }

        // ── PartStock (one zero-quantity row per part at the main warehouse) ──
        // Parts seed with no stock; on-hand quantity is built up through Goods Receipt.
        // Seeding a zero row gives every part a known warehouse location to receive into.
        if (!context.PartStocks.Any())
        {
            var mainWh = context.Locations.First(l => l.Code == "WH-RAT");
            foreach (var part in context.Parts)
            {
                context.PartStocks.Add(new PartStock
                {
                    PartId = part.Id,
                    LocationId = mainWh.Id,
                    GoodQty = 0,
                    DefectiveQty = 0
                });
            }
            context.SaveChanges();
            Console.WriteLine("✅ PartStock seeded (zero opening balance).");
        }

        // ── ATM Models (GRG Banking Equipment) ──
        if (!context.AtmModels.Any())
        {
            context.AtmModels.AddRange(
                new AtmModel { ModelCode = "GRG-H22N",  ModelName = "GRG H22N",   Manufacturer = "GRG", Description = "ตู้ ATM ล็อบบี้ รุ่นมาตรฐาน" },
                new AtmModel { ModelCode = "GRG-H22NL", ModelName = "GRG H22NL",  Manufacturer = "GRG", Description = "ตู้ ATM ล็อบบี้ รุ่น Low-profile" },
                new AtmModel { ModelCode = "GRG-H68N",  ModelName = "GRG H68N",   Manufacturer = "GRG", Description = "ตู้ ATM ความจุสูง High-capacity" },
                new AtmModel { ModelCode = "GRG-C6",    ModelName = "GRG C6",     Manufacturer = "GRG", Description = "ตู้ ATM คอมแพค Through-the-wall" },
                new AtmModel { ModelCode = "GRG-S90N",  ModelName = "GRG S90N",   Manufacturer = "GRG", Description = "ตู้รับฝากเงิน Cash Deposit (CDM)" }
            );
            context.SaveChanges();

            var models  = context.AtmModels.ToDictionary(m => m.ModelCode);
            var partNos = context.Parts.Select(p => p.PartNo).ToHashSet();

            void AddPart(string modelCode, string partNo)
            {
                if (models.ContainsKey(modelCode) && partNos.Contains(partNo))
                    context.AtmModelParts.Add(new AtmModelPart { AtmModelId = models[modelCode].Id, PartNo = partNo });
            }

            // GRG H22N — ล็อบบี้มาตรฐาน ใช้อะไหล่ครบทุกชิ้น
            AddPart("GRG-H22N", "ATM-001"); // Display Screen
            AddPart("GRG-H22N", "ATM-002"); // Banknote Acceptor
            AddPart("GRG-H22N", "ATM-003"); // Banknote Dispenser
            AddPart("GRG-H22N", "ATM-004"); // Card Reader
            AddPart("GRG-H22N", "ATM-005"); // UPS Battery
            AddPart("GRG-H22N", "ATM-006"); // Thermal Printer
            AddPart("GRG-H22N", "ATM-007"); // Journal Roll
            AddPart("GRG-H22N", "ATM-008"); // Power Supply

            // GRG H22NL — เหมือน H22N แต่ Low-profile
            AddPart("GRG-H22NL", "ATM-001");
            AddPart("GRG-H22NL", "ATM-002");
            AddPart("GRG-H22NL", "ATM-003");
            AddPart("GRG-H22NL", "ATM-004");
            AddPart("GRG-H22NL", "ATM-005");
            AddPart("GRG-H22NL", "ATM-006");
            AddPart("GRG-H22NL", "ATM-007");
            AddPart("GRG-H22NL", "ATM-008");

            // GRG H68N — High-capacity ใช้อะไหล่เดียวกับ H22N
            AddPart("GRG-H68N", "ATM-001");
            AddPart("GRG-H68N", "ATM-002");
            AddPart("GRG-H68N", "ATM-003");
            AddPart("GRG-H68N", "ATM-004");
            AddPart("GRG-H68N", "ATM-005");
            AddPart("GRG-H68N", "ATM-006");
            AddPart("GRG-H68N", "ATM-007");
            AddPart("GRG-H68N", "ATM-008");

            // GRG C6 — Through-the-wall ไม่มี Display และ Thermal Printer
            AddPart("GRG-C6", "ATM-002"); // Banknote Acceptor
            AddPart("GRG-C6", "ATM-003"); // Banknote Dispenser
            AddPart("GRG-C6", "ATM-004"); // Card Reader
            AddPart("GRG-C6", "ATM-005"); // UPS Battery
            AddPart("GRG-C6", "ATM-007"); // Journal Roll
            AddPart("GRG-C6", "ATM-008"); // Power Supply

            // GRG S90N — Cash Deposit เน้นรับเงิน ไม่มี Dispenser
            AddPart("GRG-S90N", "ATM-001"); // Display
            AddPart("GRG-S90N", "ATM-002"); // Banknote Acceptor
            AddPart("GRG-S90N", "ATM-004"); // Card Reader
            AddPart("GRG-S90N", "ATM-005"); // UPS Battery
            AddPart("GRG-S90N", "ATM-006"); // Thermal Printer
            AddPart("GRG-S90N", "ATM-007"); // Journal Roll
            AddPart("GRG-S90N", "ATM-008"); // Power Supply

            context.SaveChanges();
            Console.WriteLine("✅ ATM Models (GRG) seeded.");
        }

        // ── Tickets (rich demo data) ──
        if (!context.Tickets.Any())
        {
            context.Tickets.AddRange(

                // ✅ Overdue — received but past SLA (admin history)
                new Ticket {
                    TechEmail="tech@atm.com", TechName="Somchai Saichill",
                    TechDept="North Zone", TechPhone="081-111-1111",
                    RequestedPartNo="ATM-001", ApprovedPartNo="ATM-001",
                    Status="Received",
                    CreatedAt=DateTime.Now.AddDays(-12),
                    ReceivedAt=DateTime.Now.AddDays(-10),
                    DueDate=DateTime.Now.AddDays(-5)      // overdue by 5 days
                },

                // ✅ Received on time (admin history)
                new Ticket {
                    TechEmail="tech@atm.com", TechName="Somying Wingwai",
                    TechDept="Central Zone", TechPhone="082-222-2222",
                    RequestedPartNo="ATM-004", ApprovedPartNo="ATM-004",
                    Status="Received",
                    CreatedAt=DateTime.Now.AddDays(-5),
                    ReceivedAt=DateTime.Now.AddDays(-4),
                    DueDate=DateTime.Now.AddDays(1)       // still within SLA
                },

                // ✅ Approved — awaiting pickup (tech sees this to confirm receive)
                new Ticket {
                    TechEmail="tech@atm.com", TechName="Somkiat Suchivit",
                    TechDept="South Zone", TechPhone="083-333-3333",
                    RequestedPartNo="ATM-006", ApprovedPartNo="ATM-006",
                    Status="Approved",
                    CreatedAt=DateTime.Now.AddDays(-2)
                },

                // 🟡 Pending — waiting for admin approval (admin sees approve/reject)
                new Ticket {
                    TechEmail="tech@atm.com", TechName="Wanchai Deeprom",
                    TechDept="East Zone", TechPhone="084-444-4444",
                    RequestedPartNo="ATM-002",
                    Status="Pending",
                    CreatedAt=DateTime.Now.AddDays(-1)
                },

                // 🟡 Pending — second request
                new Ticket {
                    TechEmail="tech@atm.com", TechName="Nattapong Ruanrit",
                    TechDept="West Zone", TechPhone="085-555-5555",
                    RequestedPartNo="ATM-008",
                    Status="Pending",
                    CreatedAt=DateTime.Now.AddHours(-3)
                },

                // ❌ Rejected (admin history)
                new Ticket {
                    TechEmail="tech@atm.com", TechName="Somkiat Suchivit",
                    TechDept="South Zone", TechPhone="083-333-3333",
                    RequestedPartNo="ATM-003",
                    Status="Rejected",
                    CreatedAt=DateTime.Now.AddDays(-3)
                }
            );
            context.SaveChanges();
            Console.WriteLine("✅ Tickets seeded.");
        }

        // ── Demo parts referenced by the serial-tracking demo (create-if-missing, ungated) ──
        // These must exist as real Parts so StockMovement.PartId (FK) resolves on every DB.
        var demoParts = new[] {
            ("ATM-001", "Cash Dispenser Module (demo)"),
            ("ATM-002", "Bunch Note Acceptor (demo)"),
            ("ATM-003", "Encrypting PIN Pad (demo)"),
            ("ATM-004", "Card Reader (demo)"),
            ("ATM-006", "Receipt Printer (demo)"),
            ("ATM-008", "Journal Printer (demo)"),
        };
        foreach (var (pn, name) in demoParts)
        {
            if (!context.Parts.Any(p => p.PartNo == pn))
                context.Parts.Add(new Part { PartNo = pn, PartName = name, Unit = "pcs", MinStock = 1, MaxStock = 10, ReorderPoint = 2, IsActive = true });
        }
        context.SaveChanges();
        int DemoPartId(string pn) => context.Parts.First(p => p.PartNo == pn).Id;

        // ── Serial Tracking demo data ──────────────────────────────────────────
        // Seeds GoodsReceipts + StockMovements with SerialNo so the tracking
        // timeline page has real data to display out of the box.
        if (!context.GoodsReceipts.Any())
        {
            var wh    = context.Locations.First(l => l.Code == "WH-RAT");
            var grg   = context.Locations.First(l => l.Code == "GRG-BKK");
            var scrap = context.Locations.First(l => l.Code == "SCRAP-01");
            var vendor = context.Vendors.First();

            // ── GR-001: 3 parts received with serial numbers ──
            var gr1 = new GoodsReceipt
            {
                ReceiptNo    = "GR-2025-001",
                Source       = "GRG",
                VendorId     = vendor.Id,
                RefDocument  = "PO-2025-001",
                LocationId   = wh.Id,
                ReceivedBy   = "Admin",
                ReceivedAt   = DateTime.Now.AddDays(-30),
                HandlingCost = 500m
            };
            context.GoodsReceipts.Add(gr1);
            context.SaveChanges();

            context.GoodsReceiptLines.AddRange(
                new GoodsReceiptLine { GoodsReceiptId = gr1.Id, PartId = DemoPartId("ATM-001"), PartNo = "ATM-001", Qty = 1, Condition = "Good",      SerialNo = "SN-DISP-001" },
                new GoodsReceiptLine { GoodsReceiptId = gr1.Id, PartId = DemoPartId("ATM-002"), PartNo = "ATM-002", Qty = 1, Condition = "Good",      SerialNo = "SN-BNA-001"  },
                new GoodsReceiptLine { GoodsReceiptId = gr1.Id, PartId = DemoPartId("ATM-004"), PartNo = "ATM-004", Qty = 1, Condition = "Good",      SerialNo = "SN-CR-001"   },
                new GoodsReceiptLine { GoodsReceiptId = gr1.Id, PartId = DemoPartId("ATM-006"), PartNo = "ATM-006", Qty = 1, Condition = "Defective", SerialNo = "SN-PRT-001"  }
            );

            // GR movements in StockMovement ledger (with SerialNo)
            context.StockMovements.AddRange(
                new StockMovement { MovementType="GR", PartId=DemoPartId("ATM-001"), PartNo="ATM-001", ToLocationId=wh.Id, Qty=1, Condition="Good",      SerialNo="SN-DISP-001", RefType="GoodsReceipt", RefId=gr1.Id.ToString(), UserName="Admin", Remarks="Received from GRG", Timestamp=DateTime.Now.AddDays(-30) },
                new StockMovement { MovementType="GR", PartId=DemoPartId("ATM-002"), PartNo="ATM-002", ToLocationId=wh.Id, Qty=1, Condition="Good",      SerialNo="SN-BNA-001",  RefType="GoodsReceipt", RefId=gr1.Id.ToString(), UserName="Admin", Remarks="Received from GRG", Timestamp=DateTime.Now.AddDays(-30) },
                new StockMovement { MovementType="GR", PartId=DemoPartId("ATM-004"), PartNo="ATM-004", ToLocationId=wh.Id, Qty=1, Condition="Good",      SerialNo="SN-CR-001",   RefType="GoodsReceipt", RefId=gr1.Id.ToString(), UserName="Admin", Remarks="Received from GRG", Timestamp=DateTime.Now.AddDays(-30) },
                new StockMovement { MovementType="GR", PartId=DemoPartId("ATM-006"), PartNo="ATM-006", ToLocationId=wh.Id, Qty=1, Condition="Defective", SerialNo="SN-PRT-001",  RefType="GoodsReceipt", RefId=gr1.Id.ToString(), UserName="Admin", Remarks="Received defective from GRG", Timestamp=DateTime.Now.AddDays(-30) }
            );
            context.SaveChanges();

            // ── SN-DISP-001: Issued → Received by tech → Returned ──
            context.StockMovements.AddRange(
                new StockMovement { MovementType="Issue",  PartId=DemoPartId("ATM-001"), PartNo="ATM-001", FromLocationId=wh.Id,    Qty=1, Condition="Good", SerialNo="SN-DISP-001", RefType="Ticket", RefId="1", UserName="Somchai Saichill",  Remarks="Issued for TK-0001", Timestamp=DateTime.Now.AddDays(-28) },
                new StockMovement { MovementType="Return", PartId=DemoPartId("ATM-001"), PartNo="ATM-001", ToLocationId=wh.Id,      Qty=1, Condition="Good", SerialNo="SN-DISP-001", RefType="Ticket", RefId="1", UserName="Somchai Saichill",  Remarks="Returned after repair",  Timestamp=DateTime.Now.AddDays(-20) }
            );

            // ── SN-BNA-001: Issued → Still with technician (not returned) ──
            context.StockMovements.Add(
                new StockMovement { MovementType="Issue", PartId=DemoPartId("ATM-002"), PartNo="ATM-002", FromLocationId=wh.Id, Qty=1, Condition="Good", SerialNo="SN-BNA-001", RefType="Ticket", RefId="2", UserName="Somying Wingwai", Remarks="Issued for TK-0002", Timestamp=DateTime.Now.AddDays(-10) }
            );

            // ── SN-CR-001: Transferred to another location ──
            context.StockMovements.Add(
                new StockMovement { MovementType="Transfer", PartId=DemoPartId("ATM-004"), PartNo="ATM-004", FromLocationId=wh.Id, ToLocationId=grg.Id, Qty=1, Condition="Good", SerialNo="SN-CR-001", RefType="StockTransfer", RefId="T-001", UserName="Admin", Remarks="Transferred to GRG Bangkok Hub", Timestamp=DateTime.Now.AddDays(-15) }
            );

            // ── SN-PRT-001: Defective → Disposal ──
            context.StockMovements.Add(
                new StockMovement { MovementType="Disposal", PartId=DemoPartId("ATM-006"), PartNo="ATM-006", FromLocationId=wh.Id, ToLocationId=scrap.Id, Qty=1, Condition="Defective", SerialNo="SN-PRT-001", RefType="Disposal", RefId="D-001", UserName="Admin", Remarks="Unrepairable — sent to scrap", Timestamp=DateTime.Now.AddDays(-25) }
            );

            context.SaveChanges();

            // PartStock for demo parts so on-hand reconciles with the demo ledger above:
            //  ATM-001 received→issued→returned = 1 in warehouse · ATM-004 transferred = 1 at GRG · ATM-006 disposed = 1 defective at Scrap
            context.PartStocks.AddRange(
                new PartStock { PartId = DemoPartId("ATM-001"), LocationId = wh.Id,    GoodQty = 1, DefectiveQty = 0 },
                new PartStock { PartId = DemoPartId("ATM-004"), LocationId = grg.Id,   GoodQty = 1, DefectiveQty = 0 },
                new PartStock { PartId = DemoPartId("ATM-006"), LocationId = scrap.Id, GoodQty = 0, DefectiveQty = 1 }
            );
            context.SaveChanges();
            Console.WriteLine("✅ Serial tracking demo data seeded. Try: SN-DISP-001, SN-BNA-001, SN-CR-001, SN-PRT-001");
        }

        // ── Backfill StockMovement.PartId from PartNo (existing rows where PartId=0) ──
        // Idempotent: only touches unlinked rows whose PartNo matches an existing Part.
        try
        {
            var linked = context.Database.ExecuteSqlRaw(@"
                UPDATE StockMovements
                SET PartId = (SELECT p.Id FROM Parts p WHERE p.PartNo = StockMovements.PartNo)
                WHERE PartId = 0
                  AND EXISTS (SELECT 1 FROM Parts p WHERE p.PartNo = StockMovements.PartNo);");
            if (linked > 0) Console.WriteLine($"✅ Backfilled PartId on {linked} StockMovement(s)");

            var unlinked = context.StockMovements.Count(m => m.PartId == 0);
            if (unlinked > 0)
                Console.WriteLine($"⚠ {unlinked} StockMovement(s) still have no matching Part (PartNo not found) — review needed");
        }
        catch (Exception bex) { Console.WriteLine($"⚠ PartId backfill skipped: {bex.Message}"); }

        // ── Backfill PartId on transaction tables from their PartNo snapshot (idempotent) ──
        foreach (var tbl in new[] { "GoodsReceiptLines", "ReturnRequests", "StockTransfers",
                                    "StockCountLines", "DisposalRequests", "AtmModelParts", "EquivalentGroupMembers" })
        {
            try
            {
                var n = context.Database.ExecuteSqlRaw($@"
                    UPDATE {tbl}
                    SET PartId = (SELECT p.Id FROM Parts p WHERE p.PartNo = {tbl}.PartNo)
                    WHERE PartId = 0
                      AND EXISTS (SELECT 1 FROM Parts p WHERE p.PartNo = {tbl}.PartNo);");
                if (n > 0) Console.WriteLine($"✅ Backfilled PartId on {n} {tbl} row(s)");
            }
            catch (Exception bex) { Console.WriteLine($"⚠ {tbl} PartId backfill skipped: {bex.Message}"); }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Seed error: {ex.Message}");
    }
}

app.UseCors("AllowAll");

// Convert optimistic-concurrency conflicts into a clean 409 response instead of a 500.
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
    {
        ctx.Response.StatusCode = StatusCodes.Status409Conflict;
        await ctx.Response.WriteAsJsonAsync(new { message = "This record was changed by someone else. Please reload and try again." });
    }
});

app.UseAuthentication();

// Serve part images from external asset folder (configurable via AssetPath in appsettings.json)
// Default: D:\ATMAssets — kept outside the repo so images don't bloat git
var assetPath = builder.Configuration["AssetPath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
Directory.CreateDirectory(assetPath);
app.UseStaticFiles(new StaticFileOptions {
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(assetPath),
    RequestPath  = "/assets"
});

app.UseAuthorization();
app.MapControllers();

app.Run();
