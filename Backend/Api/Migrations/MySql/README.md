# MySQL Migration

Files here were generated with `dotnet ef migrations add InitialCreate --context AppDbContext -o Migrations/MySql`
against the `MySql.EntityFrameworkCore` (Oracle) provider, using the design-time factory in
`Backend/Api/AppDbContextFactory.cs`.

## UAT server (already provisioned)

- Host: `172.22.100.22:3306` (RHEL 9, `UAT-ATM-db-im`) — remote access only works with the
  `workbench_user` account (`root` is localhost-only via SSH).
- Database: `Sparepart_DB` — already created, `InitialCreate` migration already applied
  (verified: 22 tables + `__EFMigrationsHistory`), and seeded with the same baseline data as
  SQLite (522 parts, categories, users, etc.).
- The real connection string is **not** committed to this repo. It's stored locally via
  .NET User Secrets (`dotnet user-secrets list` from `Backend/Api` to view/reconfigure on a
  given machine). Ask a teammate who's set it up, or re-run:
  ```bash
  cd Backend/Api
  dotnet user-secrets set "ConnectionStrings:MySqlConnection" "server=172.22.100.22;port=3306;database=Sparepart_DB;user=workbench_user;password=...;connectiontimeout=60;"
  ```
  (`connectiontimeout=60` matters — the network path to this server needs longer than the 15s
  default or the Oracle MySQL client times out during the initial handshake even though the TCP
  port is reachable.)
- To actually point the running app at it, set `Database:Provider=MySql` for that run (env var
  `Database__Provider=MySql`, or in `appsettings.Development.json` locally — don't commit that
  as the repo default, since most devs should keep using local SQLite).

## Switching the app to MySQL (fresh server / new environment)

1. Create the database and a user in MySQL (or MariaDB) yourself, e.g.:
   ```sql
   CREATE DATABASE atm_inventory CHARACTER SET utf8mb4;
   ```
2. In `Backend/Api/appsettings.json` (or `appsettings.Development.json`), set:
   ```json
   {
     "Database": { "Provider": "MySql" },
     "ConnectionStrings": {
       "MySqlConnection": "server=localhost;port=3306;database=atm_inventory;user=root;password=YOUR_PASSWORD"
     }
   }
   ```
3. Run the app once (`dotnet run` from `Backend/Api`, or F5 in Visual Studio). On startup it calls
   `context.Database.Migrate()`, which applies `InitialCreate` and creates all tables in MySQL.
   The built-in seed logic (admin/tech users, categories, 522 GRG parts, ATM models) then runs the
   same way it does for SQLite, since it only checks `if (!context.Users.Any())` etc.

## Applying the migration manually instead (no app run)

```bash
cd Backend/Api
dotnet tool install --global dotnet-ef   # once, if not already installed
$env:EF_PROVIDER="MySql"                 # PowerShell; use `export EF_PROVIDER=MySql` in bash
dotnet ef database update --context AppDbContext --connection "server=localhost;port=3306;database=atm_inventory;user=root;password=YOUR_PASSWORD"
```

Or apply the plain SQL script directly with any MySQL client / `mysql` CLI:

```bash
mysql -u root -p atm_inventory < Backend/Api/Migrations/MySql/InitialCreate.sql
```

## Regenerating migrations after a model change

```bash
cd Backend/Api
$env:EF_PROVIDER="MySql"
dotnet ef migrations add <Name> --context AppDbContext -o Migrations/MySql
dotnet ef migrations script --context AppDbContext -o Migrations/MySql/InitialCreate.sql --idempotent
```

## Notes

- The SQLite path (`Database:Provider` = `Sqlite`, the default) is untouched — it still uses
  `EnsureCreated()` plus the small `ALTER TABLE` shim in `Program.cs` for legacy DBs missing the
  `MainUnit`/`Remark`/`ImagePath` columns. That shim is skipped entirely under MySQL.
- `database/AtmInventory.db` in the repo root is a **stale snapshot from an older/different branch**
  of this app — it has extra tables (`PartImages`, `PartUnits`, `TicketAttachments`, `TicketPartLines`)
  and extra `Parts` columns not present in the current C# models, and it is missing `Parts.StockQuantity`
  which the current model requires. Do not point the app at it directly; it will throw on the first
  query that touches a mismatched column. Let the app create/seed a fresh database instead.
