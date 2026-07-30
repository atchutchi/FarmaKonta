using Nofarma.Domain.Common;
using Nofarma.Domain.Purchasing;

namespace Nofarma.Application.Purchasing;

public sealed record PurchaseActorContext(
    EntityId PharmacyId,
    EntityId DeviceId,
    EntityId ActorUserId);

public sealed record PurchaseSummary(
    EntityId Id,
    EntityId SupplierId,
    string SupplierName,
    PurchaseOrderStatus Status,
    string? DocumentNumber,
    DateOnly? DocumentDate,
    int LineCount);

public sealed record PurchaseLineDetails(
    EntityId Id,
    EntityId ProductId,
    EntityId PackageId,
    string ProductName,
    string PackageName,
    long OrderedPackageQuantity,
    long ReceivedPackageQuantity,
    long FactorToBaseUnit,
    long? UnitCostXof,
    long? DiscountXof,
    long? TotalXof);

public sealed record PurchaseDetails(
    EntityId Id,
    EntityId SupplierId,
    string SupplierName,
    PurchaseOrderStatus Status,
    string? DocumentNumber,
    DateOnly? DocumentDate,
    string? Notes,
    long? TotalXof,
    IReadOnlyList<PurchaseLineDetails> Lines,
    IReadOnlyList<PurchaseReceiptSummary> Receipts);

public sealed record PurchaseReceiptSummary(
    EntityId Id,
    string DocumentNumber,
    UtcInstant ReceivedAtUtc,
    long TotalReceivedPackageQuantity);

public sealed record PurchaseReceiptDetails(
    EntityId Id,
    EntityId PurchaseId,
    string DocumentNumber,
    UtcInstant ReceivedAtUtc,
    long TotalReceivedPackageQuantity,
    PurchaseOrderStatus PurchaseStatus);
