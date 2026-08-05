namespace Nofarma.Application.Sales;

public enum SaleConcurrencyReason
{
    Sequence = 1,
    Stock = 2,
    CashShift = 3
}

public sealed class SaleConcurrencyException(SaleConcurrencyReason reason)
    : InvalidOperationException(MessageFor(reason))
{
    public SaleConcurrencyReason Reason { get; } = reason;

    private static string MessageFor(SaleConcurrencyReason reason) => reason switch
    {
        SaleConcurrencyReason.Sequence => "O número da venda foi usado por outra operação.",
        SaleConcurrencyReason.Stock => "O stock foi alterado antes da confirmação da venda.",
        SaleConcurrencyReason.CashShift => "O turno de caixa foi alterado antes da confirmação da venda.",
        _ => "A venda entrou em conflito com outra operação."
    };
}
