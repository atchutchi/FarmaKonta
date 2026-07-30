namespace Nofarma.Infrastructure.Persistence.Records;

public sealed class AuditEventRecord
{
    public Guid Id { get; set; }

    public Guid PharmacyId { get; set; }

    public Guid DeviceId { get; set; }

    public Guid? UserId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string ObjectType { get; set; } = string.Empty;

    public string ObjectId { get; set; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; set; }

    public int Outcome { get; set; }

    public string? DiagnosticCode { get; set; }

    public string DetailsJson { get; set; } = "{}";
}
