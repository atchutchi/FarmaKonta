using Nofarma.Domain.Common;
using Nofarma.Domain.Inventory;

namespace Nofarma.Application.Purchasing;

public sealed record CreatePurchaseLineRequest(
    EntityId ProductId,
    EntityId PackageId,
    long OrderedPackageQuantity,
    long FactorToBaseUnit,
    long UnitCostXof,
    long DiscountXof,
    string? Notes);

public sealed record CreatePurchaseRequest(
    EntityId SupplierId,
    string? DocumentNumber,
    DateOnly? DocumentDate,
    string? Notes,
    IReadOnlyList<CreatePurchaseLineRequest> Lines);

public sealed record UpdatePurchaseLineRequest(
    EntityId LineId,
    EntityId ProductId,
    EntityId PackageId,
    long OrderedPackageQuantity,
    long FactorToBaseUnit,
    long UnitCostXof,
    long DiscountXof,
    string? Notes);

public sealed record UpdatePurchaseRequest(
    string? DocumentNumber,
    DateOnly? DocumentDate,
    string? Notes,
    IReadOnlyList<UpdatePurchaseLineRequest> Lines);

public sealed record ConfirmPurchaseReceiptLineRequest(
    EntityId PurchaseOrderLineId,
    long PackageQuantity,
    long UnitCostXof,
    string? LotNumber,
    ExpiryDate? Expiry);

public sealed record ConfirmPurchaseReceiptRequest(
    string? DocumentNumber,
    DateOnly? DocumentDate,
    string? Notes,
    string IdempotencyKey,
    IReadOnlyList<ConfirmPurchaseReceiptLineRequest> Lines);

public sealed record ConfirmPurchaseReceiptLineCommand(
    EntityId ReceiptLineId,
    EntityId PurchaseOrderLineId,
    EntityId ProposedLotId,
    EntityId MovementId,
    long PackageQuantity,
    long UnitCostXof,
    string? LotNumber,
    ExpiryDate? Expiry,
    string MovementIdempotencyKey);

public sealed record ConfirmPurchaseReceiptCommand(
    EntityId ReceiptId,
    EntityId PurchaseId,
    EntityId ActorUserId,
    string DocumentNumber,
    DateOnly? DocumentDate,
    string? Notes,
    string IdempotencyKey,
    UtcInstant OccurredUtc,
    IReadOnlyList<ConfirmPurchaseReceiptLineCommand> Lines,
    string RequestFingerprint);
