using Nofarma.Application.Abstractions;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Application.Identity.Authorization;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;
using Nofarma.Domain.Inventory;

namespace Nofarma.Application.Inventory;

public sealed class InventoryQueryService(
    IInventoryStore store,
    AuthorizationService authorization,
    IUtcClock clock)
{
    public async Task<ProductStockDetails> GetProductAsync(
        LocalSession actor,
        EntityId productId,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.ViewStock);
        InventoryActorContext context = await store.GetContextAsync(
            actor.UserId,
            cancellationToken).ConfigureAwait(false)
            ?? throw new AuthorizationException("A sessão actual deixou de ser válida.");
        DateOnly businessDate = DateOnly.FromDateTime(clock.GetCurrentInstant().Value.UtcDateTime);
        return await store.GetProductStockAsync(
            context.PharmacyId,
            productId,
            businessDate,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("O produto indicado não existe.");
    }

    public async Task<IReadOnlyList<StockAllocation>> AllocateFefoAsync(
        LocalSession actor,
        EntityId productId,
        long requiredQuantityBase,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.ViewStock);
        InventoryActorContext context = await store.GetContextAsync(
            actor.UserId,
            cancellationToken).ConfigureAwait(false)
            ?? throw new AuthorizationException("A sessão actual deixou de ser válida.");
        DateOnly businessDate = DateOnly.FromDateTime(clock.GetCurrentInstant().Value.UtcDateTime);
        return await store.AllocateFefoAsync(
            context.PharmacyId,
            productId,
            requiredQuantityBase,
            businessDate,
            cancellationToken).ConfigureAwait(false);
    }
}
