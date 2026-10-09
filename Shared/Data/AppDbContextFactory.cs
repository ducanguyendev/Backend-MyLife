using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MyLife.Shared.Data;

// Scaffolding must never start the application or run its startup migrations.
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("MYLIFE_DESIGN_DATABASE")
            ?? throw new InvalidOperationException("Set MYLIFE_DESIGN_DATABASE explicitly for EF tooling; no application configuration is loaded.");
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
    }
}
