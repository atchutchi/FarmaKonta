namespace Nofarma.Infrastructure.Persistence.Records;

public sealed class OutboxEventRecord
{
    public Guid Id { get; set; }

    public Guid PharmacyId { get; set; }

    public Guid DeviceId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public Guid AggregateId { get; set; }

    public string PayloadJson { get; set; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; set; }
}
