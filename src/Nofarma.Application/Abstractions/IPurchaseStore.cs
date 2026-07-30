using Nofarma.Application.Purchasing;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;

namespace Nofarma.Application.Abstractions;

public interface IPurchaseStore
{
    Task<PurchaseActorContext?> GetContextAsync(
        EntityId actorUserId,
        CancellationToken cancellationToken);

    Task<PurchaseDetails> CreateAsync(
        PurchaseActorContext context,
        EntityId purchaseId,
        IReadOnlyList<EntityId> lineIds,
        EntityId actorUserId,
        CreatePurchaseRequest request,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);

    Task<PurchaseDetails?> GetAsync(
        EntityId pharmacyId,
        EntityId purchaseId,
        bool includeCosts,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PurchaseSummary>> SearchAsync(
        EntityId pharmacyId,
        string query,
        CancellationToken cancellationToken);

    Task<PurchaseDetails> UpdateAsync(
        PurchaseActorContext context,
        EntityId purchaseId,
        UpdatePurchaseRequest request,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);

    Task CancelAsync(
        PurchaseActorContext context,
        EntityId purchaseId,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);

    Task<PurchaseReceiptDetails?> GetReceiptResultAsync(
        EntityId pharmacyId,
        string idempotencyKey,
        string requestFingerprint,
        CancellationToken cancellationToken);

    Task<PurchaseReceiptDetails> ConfirmReceiptAsync(
        PurchaseActorContext context,
        ConfirmPurchaseReceiptCommand command,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);
}
