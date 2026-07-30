using Nofarma.Domain.Catalog;
using Nofarma.Domain.Common;

namespace Nofarma.Application.Catalog;

public sealed record ProductSummary(
    EntityId Id,
    string Code,
    string Name,
    ProductType Type,
    string BaseUnit,
    long SalePriceXof,
    bool RequiresLot,
    bool RequiresExpiry,
    bool IsActive,
    long? MinimumStockBase);

public sealed record ProductPackageDetails(
    EntityId Id,
    string Name,
    long FactorToBaseUnit,
    bool IsBaseUnit,
    IReadOnlyList<string> Barcodes);

public sealed record ProductCategorySummary(
    EntityId Id,
    string Name,
    bool IsActive);

public sealed record ProductDetails(
    EntityId Id,
    string Code,
    string Name,
    ProductType Type,
    string BaseUnit,
    long SalePriceXof,
    long IndicativePurchasePriceXof,
    long? MinimumStockBase,
    bool RequiresPrescription,
    bool RequiresLot,
    bool RequiresExpiry,
    bool IsActive,
    IReadOnlyList<ProductPackageDetails> Packages,
    string? ActiveIngredient = null,
    string? Dosage = null,
    string? PharmaceuticalForm = null,
    string? Manufacturer = null);
