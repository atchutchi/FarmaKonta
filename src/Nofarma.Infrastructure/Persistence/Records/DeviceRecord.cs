namespace Nofarma.Infrastructure.Persistence.Records;

public sealed class DeviceRecord
{
    public Guid Id { get; set; }

    public Guid PharmacyId { get; set; }

    public string Name { get; set; } = string.Empty;
}
