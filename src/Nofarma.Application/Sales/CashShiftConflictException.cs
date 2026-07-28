namespace Nofarma.Application.Sales;

public sealed class CashShiftConflictException()
    : InvalidOperationException(
        "A chave idempotente já foi usada por uma operação de caixa diferente.");
