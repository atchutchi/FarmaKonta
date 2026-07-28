using Nofarma.Domain.Sales;

namespace Nofarma.Application.Sales;

public sealed record OpenCashShiftRequest(
    long OpeningCashXof,
    string IdempotencyKey);

public sealed record ManualCashMovementRequest(
    CashMovementType Type,
    long AmountXof,
    string? Reason,
    string IdempotencyKey);

public sealed record CloseCashShiftRequest(
    long CountedCashXof,
    string IdempotencyKey);
