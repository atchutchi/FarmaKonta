using Nofarma.Application.Licensing;

namespace Nofarma.Application.Abstractions;

public interface ILicensedOperationPolicy
{
    Task<LicensedOperationPolicyResult> CanCreateAsync(CancellationToken cancellationToken);
}
