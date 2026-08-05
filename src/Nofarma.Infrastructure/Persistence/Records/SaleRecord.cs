namespace Nofarma.Infrastructure.Persistence.Records;

public sealed class SaleRecord
{
    public Guid Id { get; set; }
    public Guid PharmacyId { get; set; }
    public Guid DeviceId { get; set; }
    public Guid UserId { get; set; }
    public Guid CashShiftId { get; set; }
    public string Number { get; set; } = string.Empty;
    public DateOnly BusinessDate { get; set; }
    public long DailySequence { get; set; }
    public int Status { get; set; }
    public long GrossSubtotalXof { get; set; }
    public long LineDiscountXof { get; set; }
    public long TotalDiscountXof { get; set; }
    public Guid? TotalDiscountAuthorizedByUserId { get; set; }
    public long TotalXof { get; set; }
    public long PaidXof { get; set; }
    public long ChangeXof { get; set; }
    public DateTimeOffset CompletedAtUtc { get; set; }
}
