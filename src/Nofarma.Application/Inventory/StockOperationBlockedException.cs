namespace Nofarma.Application.Inventory;

public sealed class StockOperationBlockedException(string code)
    : InvalidOperationException("A confirmação de stock está bloqueada.")
{
    public string Code { get; } = code;
}
