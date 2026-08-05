using Nofarma.Domain.Common;
using Nofarma.Domain.Sales;

namespace Nofarma.Application.Sales;

public sealed record CompleteSaleRequest(
    IReadOnlyCollection<CompleteSaleLineRequest> Lines,
    long TotalDiscountXof,
    IReadOnlyCollection<SalePaymentRequest> Payments,
    string IdempotencyKey);

public sealed record CompleteSaleLineRequest(
    EntityId ProductId,
    EntityId PackageId,
    long QuantityPackages,
    long DiscountXof);

public sealed record SalePaymentRequest(
    PaymentMethod Method,
    long AmountXof,
    string? Reference);

public sealed record SuspendSaleRequest(
    EntityId? Id,
    string? Name,
    IReadOnlyCollection<CompleteSaleLineRequest> Lines);
