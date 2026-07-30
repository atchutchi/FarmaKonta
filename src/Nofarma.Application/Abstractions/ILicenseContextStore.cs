using Nofarma.Application.Licensing;

namespace Nofarma.Application.Abstractions;

public interface ILicenseContextStore
{
    Task<LicenseContext?> GetAsync(CancellationToken cancellationToken);
}
