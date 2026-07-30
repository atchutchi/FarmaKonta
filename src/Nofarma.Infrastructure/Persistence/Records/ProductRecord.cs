namespace Nofarma.Infrastructure.Persistence.Records;

public sealed class ProductRecord
{
    public Guid Id { get; set; }
    public Guid PharmacyId { get; set; }
    public Guid CategoryId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NormalizedCode { get; set; } = string.Empty;
    public long? GeneratedSequence { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ActiveIngredient { get; set; }
    public string? Dosage { get; set; }
    public string? PharmaceuticalForm { get; set; }
    public string? Manufacturer { get; set; }
    public int Type { get; set; }
    public bool RequiresPrescription { get; set; }
    public bool RequiresLot { get; set; }
    public bool RequiresExpiry { get; set; }
    public Guid BasePackageId { get; set; }
    public string BaseUnit { get; set; } = string.Empty;
    public long SalePriceXof { get; set; }
    public long IndicativePurchasePriceXof { get; set; }
    public long? MinimumStockBase { get; set; }
    public int? TaxRateBasisPoints { get; set; }
    public string? TaxExemptionReason { get; set; }
    public bool IsActive { get; set; }
    public bool HasMovements { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
