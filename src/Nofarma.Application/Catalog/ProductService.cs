using Nofarma.Application.Abstractions;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Application.Identity.Authorization;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;

namespace Nofarma.Application.Catalog;

public sealed class ProductService(
    ICatalogStore store,
    AuthorizationService authorization,
    IUtcClock clock)
{
    public async Task<IReadOnlyList<ProductSummary>> SearchAsync(
        LocalSession actor,
        string? query,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.ViewProducts);
        CatalogActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        return await store.SearchAsync(
            context.PharmacyId,
            query?.Trim() ?? string.Empty,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProductDetails> GetDetailsAsync(
        LocalSession actor,
        EntityId productId,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.ViewPurchasePrices);
        CatalogActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        return await store.GetDetailsAsync(context.PharmacyId, productId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("O produto indicado não existe.");
    }

    public async Task<ProductDetails> CreateAsync(
        LocalSession actor,
        CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.ManageProducts);
        ArgumentNullException.ThrowIfNull(request);
        CatalogActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        EntityId productId = EntityId.New();
        AuditEvent audit = CreateAudit(
            context,
            actor.UserId,
            productId,
            "product.created",
            clock.GetCurrentInstant());
        return await store.CreateAsync(
            context,
            productId,
            EntityId.New(),
            request,
            audit,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task DeactivateAsync(
        LocalSession actor,
        EntityId productId,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.ManageProducts);
        CatalogActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        AuditEvent audit = CreateAudit(
            context,
            actor.UserId,
            productId,
            "product.deactivated",
            clock.GetCurrentInstant());
        await store.DeactivateAsync(context, productId, audit, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ProductPackageDetails> AddPackageAsync(
        LocalSession actor,
        EntityId productId,
        AddProductPackageRequest request,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.ManageProducts);
        ArgumentNullException.ThrowIfNull(request);
        CatalogActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        EntityId packageId = EntityId.New();
        AuditEvent audit = CreateAudit(
            context,
            actor.UserId,
            productId,
            "product.package_added",
            clock.GetCurrentInstant());
        return await store.AddPackageAsync(
            context,
            productId,
            packageId,
            string.IsNullOrWhiteSpace(request.Barcode) ? null : EntityId.New(),
            request,
            audit,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProductDetails> UpdateSettingsAsync(
        LocalSession actor,
        EntityId productId,
        UpdateProductSettingsRequest request,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.ManageProducts);
        ArgumentNullException.ThrowIfNull(request);
        CatalogActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        AuditEvent audit = CreateAudit(
            context,
            actor.UserId,
            productId,
            "product.settings_changed",
            clock.GetCurrentInstant());
        return await store.UpdateSettingsAsync(
            context,
            productId,
            request,
            audit,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProductCategorySummary>> ListCategoriesAsync(
        LocalSession actor,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.ViewProducts);
        CatalogActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        return await store.ListCategoriesAsync(context.PharmacyId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ProductCategorySummary> CreateCategoryAsync(
        LocalSession actor,
        string name,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.ManageProducts);
        CatalogActorContext context = await GetContextAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        EntityId categoryId = EntityId.New();
        AuditEvent audit = CreateAudit(
            context,
            actor.UserId,
            categoryId,
            "product_category.created",
            clock.GetCurrentInstant(),
            "ProductCategory");
        return await store.CreateCategoryAsync(
            context,
            categoryId,
            name,
            audit,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<CatalogActorContext> GetContextAsync(
        LocalSession actor,
        CancellationToken cancellationToken) =>
        await store.GetContextAsync(actor.UserId, cancellationToken).ConfigureAwait(false)
            ?? throw new AuthorizationException("A sessão actual deixou de ser válida.");

    private static AuditEvent CreateAudit(
        CatalogActorContext context,
        EntityId actorUserId,
        EntityId productId,
        string action,
        UtcInstant occurredAtUtc,
        string objectType = "Product") => new(
            EntityId.New(),
            context.PharmacyId,
            context.DeviceId,
            actorUserId,
            action,
            objectType,
            productId.Value.ToString("D"),
            occurredAtUtc,
            AuditOutcome.Success,
            null,
            "{}");
}
