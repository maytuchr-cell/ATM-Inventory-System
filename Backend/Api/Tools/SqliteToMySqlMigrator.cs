using System.Globalization;
using Microsoft.Data.Sqlite;
using MySql.Data.MySqlClient;

namespace Api.Tools;

// One-off data migration: copies every row from the old SQLite database file into the MySQL
// database the app is now configured to use. Run once via `dotnet run -- migrate-sqlite-to-mysql
// <path-to-AtmInventory.db>` — the destination schema must already exist (start the app once
// against the empty MySQL database first; Program.cs's EnsureCreated() builds it from the model).
// Generic table copy (not tied to specific entity types) so it doesn't need updating when the
// model changes: reads every user table from sqlite_master, skips any that don't exist on the
// MySQL side, and converts values using the DESTINATION column's declared type (so SQLite's
// TEXT-stored dates land back in real MySQL DATETIME columns instead of raw strings).
public static class SqliteToMySqlMigrator
{
    public static void Run(string sqlitePath, string mysqlConnectionString)
    {
        if (!File.Exists(sqlitePath))
            throw new FileNotFoundException($"SQLite source file not found: {sqlitePath}");

        Console.WriteLine($"Migrating '{sqlitePath}' -> MySQL...");

        using var src = new SqliteConnection($"Data Source={sqlitePath}");
        src.Open();
        using var dst = new MySqlConnection(mysqlConnectionString);
        dst.Open();

        var tables = new List<string>();
        using (var cmd = src.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
            using var r = cmd.ExecuteReader();
            while (r.Read()) tables.Add(r.GetString(0));
        }

        using (var fkOff = dst.CreateCommand()) { fkOff.CommandText = "SET FOREIGN_KEY_CHECKS=0;"; fkOff.ExecuteNonQuery(); }

        var totalRows = 0;
        foreach (var table in tables)
        {
            var colTypes = GetMySqlColumnTypes(dst, table);
            if (colTypes.Count == 0)
            {
                Console.WriteLine($"  skip {table} (no matching table in MySQL schema)");
                continue;
            }

            using var selectCmd = src.CreateCommand();
            selectCmd.CommandText = $"SELECT * FROM \"{table}\";";
            using var reader = selectCmd.ExecuteReader();
            var colCount = reader.FieldCount;
            var colNames = Enumerable.Range(0, colCount).Select(reader.GetName).ToArray();

            var insertSql = $"INSERT INTO `{table}` (" + string.Join(",", colNames.Select(c => $"`{c}`"))
                + ") VALUES (" + string.Join(",", colNames.Select((_, i) => $"@p{i}")) + ");";

            var rowCount = 0;
            using var tx = dst.BeginTransaction();
            while (reader.Read())
            {
                using var insertCmd = dst.CreateCommand();
                insertCmd.Transaction = tx;
                insertCmd.CommandText = insertSql;
                for (int i = 0; i < colCount; i++)
                {
                    var raw = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    var value = ConvertForMySql(raw, colTypes.GetValueOrDefault(colNames[i]));
                    insertCmd.Parameters.AddWithValue($"@p{i}", value ?? DBNull.Value);
                }
                insertCmd.ExecuteNonQuery();
                rowCount++;
            }
            tx.Commit();
            Console.WriteLine($"  {table}: {rowCount} rows migrated");
            totalRows += rowCount;
        }

        using (var fkOn = dst.CreateCommand()) { fkOn.CommandText = "SET FOREIGN_KEY_CHECKS=1;"; fkOn.ExecuteNonQuery(); }
        Console.WriteLine($"Migration complete — {totalRows} rows total across {tables.Count} tables.");
    }

    // MySQL column name -> data_type (lowercase, e.g. "datetime", "tinyint", "varchar").
    private static Dictionary<string, string> GetMySqlColumnTypes(MySqlConnection dst, string table)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = dst.CreateCommand();
        cmd.CommandText = "SELECT column_name, data_type FROM information_schema.columns " +
                           "WHERE table_schema = DATABASE() AND table_name = @t;";
        cmd.Parameters.AddWithValue("@t", table);
        using var r = cmd.ExecuteReader();
        while (r.Read()) result[r.GetString(0)] = r.GetString(1);
        return result;
    }

    private static object? ConvertForMySql(object? value, string? mysqlType)
    {
        if (value is null) return null;
        if (mysqlType is null) return value;

        // SQLite stores DateTime as TEXT (ISO-ish string) — parse it back into a real DateTime so
        // the MySQL connector writes an actual DATETIME/DATE value instead of a literal string.
        if (value is string s && (mysqlType is "datetime" or "timestamp" or "date"))
        {
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt;
        }
        return value;
    }
}
