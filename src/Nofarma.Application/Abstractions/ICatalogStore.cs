using Nofarma.Application.Catalog;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;

namespace Nofarma.Application.Abstractions;

public interface ICatalogStore
{
    Task<CatalogActorContext?> GetContextAsync(
        EntityId actorUserId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ProductSummary>> SearchAsync(
        EntityId pharmacyId,
        string query,
        CancellationToken cancellationToken);

    Task<ProductDetails?> GetDetailsAsync(
        EntityId pharmacyId,
        EntityId productId,
        CancellationToken cancellationToken);

    Task<ProductDetails> CreateAsync(
        CatalogActorContext context,
        EntityId productId,
        EntityId basePackageId,
        CreateProductRequest request,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);

    Task DeactivateAsync(
        CatalogActorContext context,
        EntityId productId,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);

    Task<ProductPackageDetails> AddPackageAsync(
        CatalogActorContext context,
        EntityId productId,
        EntityId packageId,
        EntityId? barcodeId,
        AddProductPackageRequest request,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);

    Task<ProductDetails> UpdateSettingsAsync(
        CatalogActorContext context,
        EntityId productId,
        UpdateProductSettingsRequest request,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ProductCategorySummary>> ListCategoriesAsync(
        EntityId pharmacyId,
        CancellationToken cancellationToken);

    Task<ProductCategorySummary> CreateCategoryAsync(
        CatalogActorContext context,
        EntityId categoryId,
        string name,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);
}
