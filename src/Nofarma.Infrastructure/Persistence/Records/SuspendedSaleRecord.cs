namespace Nofarma.Infrastructure.Persistence.Records;

public sealed class SuspendedSaleRecord
{
    public Guid Id { get; set; }
    public Guid PharmacyId { get; set; }
    public Guid DeviceId { get; set; }
    public Guid UserId { get; set; }
    public string? Name { get; set; }
    public DateTimeOffset SuspendedAtUtc { get; set; }
    public long RowVersion { get; set; }
}
