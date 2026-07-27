namespace Nofarma.Infrastructure.Persistence.Records;

public sealed class PharmacyRecord
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string TaxIdentifier { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public string Contact { get; set; } = string.Empty;

    public string TimeZoneId { get; set; } = string.Empty;
}
