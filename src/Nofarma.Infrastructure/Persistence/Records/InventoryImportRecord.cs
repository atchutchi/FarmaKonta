namespace Nofarma.Infrastructure.Persistence.Records;

public sealed class InventoryImportRecord
{
    public Guid Id { get; set; }
    public Guid PharmacyId { get; set; }
    public string FileHash { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? SheetName { get; set; }
    public int Status { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ConfirmedAtUtc { get; set; }
    public string? ConfirmationIdempotencyKey { get; set; }
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int ErrorRows { get; set; }
}
