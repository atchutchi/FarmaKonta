using Nofarma.Application.Licensing;

namespace Nofarma.Application.Abstractions;

public interface ILicenseStatusProvider
{
    Task<LicenseStatus> GetStatusAsync(CancellationToken cancellationToken);
}
