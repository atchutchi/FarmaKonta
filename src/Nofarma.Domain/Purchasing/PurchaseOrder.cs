using Nofarma.Domain.Common;

namespace Nofarma.Domain.Purchasing;

public sealed class PurchaseOrder
{
    private readonly List<PurchaseOrderLine> _lines = [];

    private PurchaseOrder(
        EntityId id,
        EntityId pharmacyId,
        EntityId supplierId,
        EntityId createdByUserId,
        UtcInstant createdAtUtc)
    {
        Id = id;
        PharmacyId = pharmacyId;
        SupplierId = supplierId;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
        Status = PurchaseOrderStatus.Draft;
    }

    public EntityId Id { get; }

    public EntityId PharmacyId { get; }

    public EntityId SupplierId { get; }

    public EntityId CreatedByUserId { get; }

    public UtcInstant CreatedAtUtc { get; }

    public PurchaseOrderStatus Status { get; private set; }

    public string? DocumentNumber { get; private set; }

    public DateOnly? DocumentDate { get; private set; }

    public string? Notes { get; private set; }

    public IReadOnlyList<PurchaseOrderLine> Lines => _lines.AsReadOnly();

    public long TotalXof => _lines.Aggregate(
        0L,
        (total, line) => checked(total + line.TotalXof));

    public static PurchaseOrder Create(
        EntityId id,
        EntityId pharmacyId,
        EntityId supplierId,
        EntityId createdByUserId,
        string? documentNumber,
        DateOnly? documentDate,
        string? notes,
        UtcInstant createdAtUtc)
    {
        EnsureIdentifier(id, "A compra deve ter um identificador.");
        EnsureIdentifier(pharmacyId, "A farmácia da compra é obrigatória.");
        EnsureIdentifier(supplierId, "O fornecedor da compra é obrigatório.");
        EnsureIdentifier(createdByUserId, "O utilizador da compra é obrigatório.");
        var order = new PurchaseOrder(
            id,
            pharmacyId,
            supplierId,
            createdByUserId,
            createdAtUtc);
        order.UpdateDocument(documentNumber, documentDate, notes);
        return order;
    }

    public PurchaseOrderLine AddLine(
        EntityId lineId,
        EntityId productId,
        EntityId packageId,
        long orderedPackageQuantity,
        long factorToBaseUnit,
        long unitCostXof,
        long discountXof,
        string? notes)
    {
        EnsureDraft();
        if (_lines.Any(line => line.Id == lineId))
        {
            throw new PurchaseValidationException("A linha já existe na compra.");
        }

        var line = new PurchaseOrderLine(
            lineId,
            productId,
            packageId,
            orderedPackageQuantity,
            receivedPackageQuantity: 0,
            factorToBaseUnit,
            unitCostXof,
            discountXof,
            notes);
        _lines.Add(line);
        return line;
    }

    public void UpdateDocument(
        string? documentNumber,
        DateOnly? documentDate,
        string? notes)
    {
        EnsureDraft();
        DocumentNumber = NormalizeOptional(documentNumber);
        DocumentDate = documentDate;
        Notes = NormalizeOptional(notes);
    }

    public void UpdateLine(
        EntityId lineId,
        long orderedPackageQuantity,
        long factorToBaseUnit,
        long unitCostXof,
        long discountXof,
        string? notes)
    {
        EnsureDraft();
        FindLine(lineId).Update(
            orderedPackageQuantity,
            factorToBaseUnit,
            unitCostXof,
            discountXof,
            notes);
    }

    public void RemoveLine(EntityId lineId)
    {
        EnsureDraft();
        _lines.Remove(FindLine(lineId));
    }

    public void Receive(EntityId lineId, long packageQuantity)
    {
        if (Status is PurchaseOrderStatus.Cancelled or PurchaseOrderStatus.Received)
        {
            throw new PurchaseValidationException("O estado da compra não permite recepções.");
        }

        if (DocumentNumber is null)
        {
            throw new PurchaseValidationException(
                "O número do documento é obrigatório antes da recepção.");
        }

        PurchaseOrderLine line = FindLine(lineId);
        line.Receive(packageQuantity);
        Status = _lines.All(candidate => candidate.RemainingPackageQuantity == 0)
            ? PurchaseOrderStatus.Received
            : PurchaseOrderStatus.PartiallyReceived;
    }

    public void Cancel()
    {
        EnsureDraft();
        Status = PurchaseOrderStatus.Cancelled;
    }

    private void EnsureDraft()
    {
        if (Status != PurchaseOrderStatus.Draft)
        {
            throw new PurchaseValidationException("Apenas uma compra em rascunho pode ser alterada.");
        }
    }

    private PurchaseOrderLine FindLine(EntityId lineId) =>
        _lines.SingleOrDefault(candidate => candidate.Id == lineId)
            ?? throw new PurchaseValidationException("A linha indicada não pertence à compra.");

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void EnsureIdentifier(EntityId id, string message)
    {
        if (id.Value == Guid.Empty)
        {
            throw new PurchaseValidationException(message);
        }
    }
}
