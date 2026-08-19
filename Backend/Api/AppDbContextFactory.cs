using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Api.Models;

// Used only by `dotnet ef` design-time tooling to generate migrations.
// Select provider with EF_PROVIDER=MySql|Sqlite (defaults to Sqlite).
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        var provider = Environment.GetEnvironmentVariable("EF_PROVIDER") ?? "Sqlite";

        if (string.Equals(provider, "MySql", StringComparison.OrdinalIgnoreCase))
        {
            var conn = Environment.GetEnvironmentVariable("EF_MYSQL_CONNECTION")
                ?? "server=localhost;port=3306;database=atm_inventory;user=root;password=root";
            optionsBuilder.UseMySQL(conn);
        }
        else
        {
            optionsBuilder.UseSqlite("Data Source=AtmInventory.db");
        }

        return new AppDbContext(optionsBuilder.Options);
    }
}
