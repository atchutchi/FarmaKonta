using Microsoft.EntityFrameworkCore;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Licensing;
using Nofarma.Domain.Common;
using Nofarma.Infrastructure.Persistence;

namespace Nofarma.Infrastructure.Licensing;

public sealed class SqliteLicenseContextStore(
    DbContextOptions<NofarmaDbContext> options) : ILicenseContextStore
{
    public async Task<LicenseContext?> GetAsync(CancellationToken cancellationToken)
    {
        await using var db = new NofarmaDbContext(options);
        var installations = await db.Installations.AsNoTracking()
            .Select(record => new { record.PharmacyId, record.DeviceId })
            .Take(2)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return installations.Length switch
        {
            0 => null,
            1 => new LicenseContext(
                new EntityId(installations[0].PharmacyId),
                new EntityId(installations[0].PharmacyId),
                new EntityId(installations[0].DeviceId)),
            _ => throw new InvalidOperationException(
                "A instalação local contém mais do que um contexto de licenciamento.")
        };
    }
}
