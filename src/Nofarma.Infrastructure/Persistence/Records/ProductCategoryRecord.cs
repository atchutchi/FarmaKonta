namespace Nofarma.Infrastructure.Persistence.Records;

public sealed class ProductCategoryRecord
{
    public Guid Id { get; set; }
    public Guid PharmacyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
