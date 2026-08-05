namespace Nofarma.Application.Sales;

public sealed class SaleConflictException()
    : InvalidOperationException(
        "A chave idempotente já foi usada por uma venda diferente.");
