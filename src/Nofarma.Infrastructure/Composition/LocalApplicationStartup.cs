using Microsoft.EntityFrameworkCore;

namespace Nofarma.Infrastructure.Composition;

public sealed class LocalApplicationStartup(
    DbContextOptions<Persistence.NofarmaDbContext> options)
{
    public async Task<ApplicationStartDestination> InitializeAsync(
        CancellationToken cancellationToken)
    {
        await using var context = new Persistence.NofarmaDbContext(options);
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        bool configured = await context.Installations
            .AsNoTracking()
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);
        return configured
            ? ApplicationStartDestination.Login
            : ApplicationStartDestination.Setup;
    }
}
