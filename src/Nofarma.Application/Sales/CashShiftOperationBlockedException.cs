namespace Nofarma.Application.Sales;

public sealed class CashShiftOperationBlockedException(string code)
    : InvalidOperationException("A operação de caixa está bloqueada.")
{
    public string Code { get; } = code;
}
