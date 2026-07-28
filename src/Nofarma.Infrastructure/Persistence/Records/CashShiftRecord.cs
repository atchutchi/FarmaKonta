namespace Nofarma.Infrastructure.Persistence.Records;

public sealed class CashShiftRecord
{
    public Guid Id { get; set; }

    public Guid PharmacyId { get; set; }

    public Guid DeviceId { get; set; }

    public Guid UserId { get; set; }

    public int Status { get; set; }

    public long OpeningCashXof { get; set; }

    public long ExpectedCashXof { get; set; }

    public long? CountedCashXof { get; set; }

    public long? DifferenceXof { get; set; }

    public DateTimeOffset OpenedAtUtc { get; set; }

    public DateTimeOffset? ClosedAtUtc { get; set; }

    public long RowVersion { get; set; }
}
