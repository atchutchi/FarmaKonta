using Nofarma.Application.Inventory;

namespace Nofarma.Application.Abstractions;

public interface IStockOperationPolicy
{
    Task<StockOperationPolicyResult> CanConfirmAsync(
        CancellationToken cancellationToken);
}
