using Nofarma.Application.Licensing;
using Nofarma.Domain.Auditing;

namespace Nofarma.Application.Abstractions;

public interface ILicenseStore
{
    Task<StoredLicense?> GetAsync(CancellationToken cancellationToken);

    Task<LicenseStoreReplaceResult> ReplaceAsync(
        VerifiedLicense license,
        AuditEvent audit,
        LicenseStorePrecondition precondition,
        CancellationToken cancellationToken);
}
