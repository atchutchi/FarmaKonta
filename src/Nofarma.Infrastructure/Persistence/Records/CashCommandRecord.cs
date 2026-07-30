namespace Nofarma.Infrastructure.Persistence.Records;

public sealed class CashCommandRecord
{
    public Guid Id { get; set; }

    public Guid PharmacyId { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    public int OperationType { get; set; }

    public string RequestFingerprint { get; set; } = string.Empty;

    public Guid CashShiftId { get; set; }

    public string ResultJson { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
}
