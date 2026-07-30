using Nofarma.Domain.Catalog;
using Nofarma.Domain.Common;

namespace Nofarma.Application.Catalog;

public sealed record CreateProductRequest(
    string? Code,
    string Name,
    EntityId CategoryId,
    string BaseUnit,
    ProductType Type,
    long SalePriceXof,
    long IndicativePurchasePriceXof,
    long? MinimumStockBase,
    bool RequiresPrescription,
    bool RequiresLot,
    bool RequiresExpiry,
    string? ActiveIngredient = null,
    string? Dosage = null,
    string? PharmaceuticalForm = null,
    string? Manufacturer = null);

public sealed record CatalogActorContext(EntityId PharmacyId, EntityId DeviceId);

public sealed record AddProductPackageRequest(
    string Name,
    long FactorToBaseUnit,
    string? Barcode);

public sealed record UpdateProductSettingsRequest(
    long SalePriceXof,
    long IndicativePurchasePriceXof,
    long? MinimumStockBase,
    bool RequiresLot,
    bool RequiresExpiry);
