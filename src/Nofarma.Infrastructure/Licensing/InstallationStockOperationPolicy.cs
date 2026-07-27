using Nofarma.Application.Abstractions;
using Nofarma.Application.Configuration;
using Nofarma.Application.Inventory;
using Nofarma.Domain.Identity;

namespace Nofarma.Infrastructure.Licensing;

public sealed class InstallationStockOperationPolicy(
    ILocalApplicationInfoStore applicationInfoStore) : IStockOperationPolicy
{
    public async Task<StockOperationPolicyResult> CanConfirmAsync(
        CancellationToken cancellationToken)
    {
        LocalApplicationInfo? info = await applicationInfoStore
            .GetAsync(cancellationToken)
            .ConfigureAwait(false);
        if (info is null)
        {
            return new StockOperationPolicyResult(false, "INSTALLATION_REQUIRED");
        }

        return info.InstallationStatus == InstallationStatus.Active
            ? new StockOperationPolicyResult(true, null)
            : new StockOperationPolicyResult(false, "LICENSE_REQUIRED");
    }
}
