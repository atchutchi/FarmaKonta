using Nofarma.Domain.Common;
using Nofarma.Domain.Inventory;
using Nofarma.Domain.Sales;

namespace Nofarma.Application.Sales;

public sealed record SaleActorContext(
    EntityId PharmacyId,
    string PharmacyName,
    string TimeZoneId,
    EntityId DeviceId,
    EntityId ActorUserId,
    string ActorDisplayName);

public sealed record SaleProductResult(
    EntityId ProductId,
    EntityId PackageId,
    string Code,
    string Name,
    string PackageName,
    long PackageFactor,
    long AvailableQuantityBase,
    long SalePriceXof,
    string? EarliestLotNumber,
    DateOnly? EarliestExpiry,
    bool RequiresPrescription);

public sealed record SaleLotSnapshot(
    StockLot Lot,
    long AvailableQuantityBase,
    long Version);

public sealed record SaleProductSnapshot(
    EntityId ProductId,
    EntityId PackageId,
    string Code,
    string Name,
    string PackageName,
    long PackageFactor,
    long SalePriceXof,
    bool RequiresPrescription,
    IReadOnlyList<SaleLotSnapshot> Lots);

public sealed record SaleSummary(
    EntityId Id,
    string Number,
    long TotalXof,
    long PaidXof,
    long ChangeXof,
    UtcInstant CompletedAtUtc,
    EntityId ReceiptId);

public sealed record SaleCommandEnvelope(
    string IdempotencyKey,
    string RequestFingerprint);

public sealed record SaleCommandResult(
    string RequestFingerprint,
    SaleSummary Result);

public sealed record SuspendedSaleSummary(
    EntityId Id,
    string? Name,
    int LineCount,
    long EstimatedTotalXof,
    UtcInstant SuspendedAtUtc);

public sealed record SuspendedSaleLineDetails(
    EntityId ProductId,
    EntityId PackageId,
    long QuantityPackages,
    long DiscountXof);

public sealed record SuspendedSaleDetails(
    EntityId Id,
    string? Name,
    IReadOnlyList<SuspendedSaleLineDetails> Lines,
    UtcInstant SuspendedAtUtc);

public sealed record ResumedSaleLineDetails(
    EntityId ProductId,
    EntityId PackageId,
    string? Code,
    string? Name,
    string? PackageName,
    long? PackageFactor,
    long QuantityPackages,
    long DiscountXof,
    long? SalePriceXof,
    long AvailableQuantityBase,
    bool RequiresReview);

public sealed record ResumedSaleDetails(
    EntityId Id,
    string? Name,
    IReadOnlyList<ResumedSaleLineDetails> Lines,
    UtcInstant SuspendedAtUtc);

public sealed record ReceiptLineDetails(
    string Description,
    string UnitName,
    long QuantityPackages,
    long UnitPriceXof,
    long DiscountXof,
    long TotalXof);

public sealed record ReceiptPaymentDetails(
    PaymentMethod Method,
    long AmountXof,
    string? Reference);

public sealed record ReceiptDetails(
    EntityId Id,
    EntityId SaleId,
    string Number,
    string PharmacyName,
    string OperatorName,
    UtcInstant CreatedAtUtc,
    IReadOnlyList<ReceiptLineDetails> Lines,
    IReadOnlyList<ReceiptPaymentDetails> Payments,
    long TotalXof,
    long ChangeXof,
    string DocumentLabel);
