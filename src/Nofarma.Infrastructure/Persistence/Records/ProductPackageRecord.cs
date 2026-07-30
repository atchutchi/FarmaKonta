namespace Nofarma.Infrastructure.Persistence.Records;

public sealed class ProductPackageRecord
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public long FactorToBaseUnit { get; set; }
    public bool IsBaseUnit { get; set; }
    public bool IsActive { get; set; }
}
