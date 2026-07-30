namespace Nofarma.Infrastructure.Persistence.Records;

public sealed class CashMovementRecord
{
    public Guid Id { get; set; }

    public Guid PharmacyId { get; set; }

    public Guid CashShiftId { get; set; }

    public long Sequence { get; set; }

    public int Type { get; set; }

    public long AmountXof { get; set; }

    public Guid? SourceSaleId { get; set; }

    public string Reason { get; set; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; set; }
}
