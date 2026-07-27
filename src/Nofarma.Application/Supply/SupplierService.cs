using Nofarma.Application.Abstractions;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Application.Identity.Authorization;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;

namespace Nofarma.Application.Supply;

public sealed class SupplierService(
    ISupplierStore store,
    AuthorizationService authorization,
    IUtcClock clock)
{
    public async Task<SupplierDetails> CreateAsync(
        LocalSession actor,
        CreateSupplierRequest request,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.ManageSuppliers);
        ArgumentNullException.ThrowIfNull(request);
        SupplierActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        EntityId supplierId = EntityId.New();
        AuditEvent audit = CreateAudit(
            context,
            actor.UserId,
            supplierId,
            "supplier.created",
            clock.GetCurrentInstant());
        return await store.CreateAsync(
            context,
            supplierId,
            request,
            audit,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SupplierSummary>> SearchAsync(
        LocalSession actor,
        string? query,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.ViewSuppliers);
        SupplierActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        return await store.SearchAsync(
            context.PharmacyId,
            query?.Trim() ?? string.Empty,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task DeactivateAsync(
        LocalSession actor,
        EntityId supplierId,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.ManageSuppliers);
        SupplierActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        AuditEvent audit = CreateAudit(
            context,
            actor.UserId,
            supplierId,
            "supplier.deactivated",
            clock.GetCurrentInstant());
        await store.DeactivateAsync(context, supplierId, audit, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<SupplierDetails> UpdateAsync(
        LocalSession actor,
        EntityId supplierId,
        UpdateSupplierRequest request,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.ManageSuppliers);
        ArgumentNullException.ThrowIfNull(request);
        SupplierActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        AuditEvent audit = CreateAudit(
            context,
            actor.UserId,
            supplierId,
            "supplier.updated",
            clock.GetCurrentInstant());
        return await store.UpdateAsync(
            context,
            supplierId,
            request,
            audit,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<SupplierActorContext> GetContextAsync(
        LocalSession actor,
        CancellationToken cancellationToken) =>
        await store.GetContextAsync(actor.UserId, cancellationToken).ConfigureAwait(false)
            ?? throw new AuthorizationException("A sessão actual deixou de ser válida.");

    private static AuditEvent CreateAudit(
        SupplierActorContext context,
        EntityId actorUserId,
        EntityId supplierId,
        string action,
        UtcInstant occurredAtUtc) => new(
            EntityId.New(),
            context.PharmacyId,
            context.DeviceId,
            actorUserId,
            action,
            "Supplier",
            supplierId.Value.ToString("D"),
            occurredAtUtc,
            AuditOutcome.Success,
            null,
            "{}");
}
