using Nofarma.Application.Sales;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Sales;

namespace Nofarma.Application.Abstractions;

public interface ISaleStore
{
    Task<SaleActorContext?> GetActorContextAsync(
        EntityId userId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SaleProductResult>> SearchProductsAsync(
        EntityId pharmacyId,
        string query,
        DateOnly businessDate,
        int limit,
        CancellationToken cancellationToken);

    Task<StoredCashShift?> GetOpenShiftAsync(
        EntityId pharmacyId,
        EntityId deviceId,
        CancellationToken cancellationToken);

    Task<SaleProductSnapshot?> GetProductSnapshotAsync(
        EntityId pharmacyId,
        EntityId productId,
        EntityId packageId,
        DateOnly businessDate,
        CancellationToken cancellationToken);

    Task<SaleCommandResult?> GetCommandResultAsync(
        EntityId pharmacyId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<long> GetNextSaleSequenceAsync(
        EntityId pharmacyId,
        DateOnly businessDate,
        CancellationToken cancellationToken);

    Task<SaleSummary> CompleteAsync(
        SaleCompletion completion,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SuspendedSaleSummary>> GetSuspendedAsync(
        EntityId pharmacyId,
        EntityId deviceId,
        CancellationToken cancellationToken);

    Task<SuspendedSaleDetails?> GetSuspendedDetailsAsync(
        EntityId pharmacyId,
        EntityId deviceId,
        EntityId suspendedSaleId,
        CancellationToken cancellationToken);

    Task<SuspendedSaleSummary> SaveSuspendedAsync(
        SuspendedSale sale,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);

    Task<bool> DeleteSuspendedAsync(
        EntityId pharmacyId,
        EntityId deviceId,
        EntityId suspendedSaleId,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);

    Task<ReceiptDetails?> GetReceiptAsync(
        EntityId pharmacyId,
        EntityId receiptId,
        CancellationToken cancellationToken);
}
