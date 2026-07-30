using Nofarma.Application.Supply;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;

namespace Nofarma.Application.Abstractions;

public interface ISupplierStore
{
    Task<SupplierActorContext?> GetContextAsync(
        EntityId actorUserId,
        CancellationToken cancellationToken);

    Task<SupplierDetails> CreateAsync(
        SupplierActorContext context,
        EntityId supplierId,
        CreateSupplierRequest request,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SupplierSummary>> SearchAsync(
        EntityId pharmacyId,
        string query,
        CancellationToken cancellationToken);

    Task DeactivateAsync(
        SupplierActorContext context,
        EntityId supplierId,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);

    Task<SupplierDetails> UpdateAsync(
        SupplierActorContext context,
        EntityId supplierId,
        UpdateSupplierRequest request,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);
}
