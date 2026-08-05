namespace Nofarma.Application.Sales;

public sealed class SaleOperationBlockedException(string code)
    : InvalidOperationException("A licença não permite concluir uma nova venda.")
{
    public string Code { get; } = code;
}
