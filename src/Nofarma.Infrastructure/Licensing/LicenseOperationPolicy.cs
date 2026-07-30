using Nofarma.Application.Abstractions;
using Nofarma.Application.Licensing;

namespace Nofarma.Infrastructure.Licensing;

public sealed class LicenseOperationPolicy(ILicenseStatusProvider statusProvider)
    : ILicensedOperationPolicy
{
    public async Task<LicensedOperationPolicyResult> CanCreateAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            LicenseStatus status = await statusProvider.GetStatusAsync(cancellationToken)
                .ConfigureAwait(false);
            return LicenseOperationPolicyRules.For(status.State);
        }
        catch (LicenseContextUnavailableException)
        {
            return new LicensedOperationPolicyResult(false, "INSTALLATION_REQUIRED");
        }
    }
}
