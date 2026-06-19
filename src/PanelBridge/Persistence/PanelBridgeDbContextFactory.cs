using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PanelBridge.Persistence;

/// <summary>
/// Used only at design time by `dotnet ef` so it can build a DbContext
/// without spinning up the Functions host. Pick up the connection string
/// from env var OMNI_MIGRATIONS_CONNECTION, falling back to a LocalDB default.
/// </summary>
internal sealed class PanelBridgeDbContextFactory : IDesignTimeDbContextFactory<PanelBridgeDbContext>
{
    public PanelBridgeDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("OMNI_MIGRATIONS_CONNECTION")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=PanelBridge;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<PanelBridgeDbContext>()
            .UseSqlServer(conn)
            .Options;

        return new PanelBridgeDbContext(options);
    }
}
