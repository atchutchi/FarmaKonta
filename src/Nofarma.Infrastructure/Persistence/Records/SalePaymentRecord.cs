namespace Nofarma.Infrastructure.Persistence.Records;

public sealed class SalePaymentRecord
{
    public Guid Id { get; set; }
    public Guid SaleId { get; set; }
    public int Sequence { get; set; }
    public int Method { get; set; }
    public long AmountXof { get; set; }
    public string? Reference { get; set; }
}
