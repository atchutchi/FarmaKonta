using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Nofarma.Infrastructure.Persistence;

public sealed class NofarmaDbContextFactory : IDesignTimeDbContextFactory<NofarmaDbContext>
{
    public NofarmaDbContext CreateDbContext(string[] args)
    {
        string path = Environment.GetEnvironmentVariable("NOFARMA_DESIGN_DB_PATH") ??
            Path.Combine(Path.GetTempPath(), "nofarma-design.db");
        var options = new DbContextOptionsBuilder<NofarmaDbContext>()
            .UseSqlite(LocalDatabasePath.BuildConnectionString(path))
            .Options;

        return new NofarmaDbContext(options);
    }
}
