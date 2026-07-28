namespace Nofarma.Application.Sales;

public sealed class CashShiftConcurrencyException()
    : InvalidOperationException("O turno de caixa foi alterado por outra operação.");
