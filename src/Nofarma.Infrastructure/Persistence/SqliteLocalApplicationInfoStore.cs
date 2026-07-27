using Microsoft.EntityFrameworkCore;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Configuration;
using Nofarma.Domain.Identity;

namespace Nofarma.Infrastructure.Persistence;

public sealed class SqliteLocalApplicationInfoStore(
    DbContextOptions<NofarmaDbContext> options) : ILocalApplicationInfoStore
{
    public async Task<LocalApplicationInfo?> GetAsync(CancellationToken cancellationToken)
    {
        await using var context = new NofarmaDbContext(options);
        return await context.Installations
            .AsNoTracking()
            .Select(record => new LocalApplicationInfo(
                record.Pharmacy.Name,
                record.Pharmacy.TaxIdentifier,
                record.Pharmacy.Address,
                record.Pharmacy.Contact,
                (InstallationStatus)record.Status))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
