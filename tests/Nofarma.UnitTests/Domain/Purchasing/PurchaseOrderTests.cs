using Nofarma.Domain.Common;
using Nofarma.Domain.Purchasing;

namespace Nofarma.UnitTests.Domain.Purchasing;

public sealed class PurchaseOrderTests
{
    [Fact]
    public void NewOrderIsDraftAndCalculatesExactXofTotal()
    {
        PurchaseOrder order = CreateOrder();

        PurchaseOrderLine line = order.AddLine(
            EntityId.New(),
            EntityId.New(),
            EntityId.New(),
            orderedPackageQuantity: 3,
            factorToBaseUnit: 12,
            unitCostXof: 1_001,
            discountXof: 3,
            notes: null);

        Assert.Equal(PurchaseOrderStatus.Draft, order.Status);
        Assert.Equal(3_000, line.TotalXof);
        Assert.Equal(3_000, order.TotalXof);
        Assert.Equal(12, line.FactorToBaseUnit);
    }

    [Fact]
    public void PartialThenCompleteReceiptUpdatesStatusAndQuantities()
    {
        PurchaseOrder order = CreateOrder(documentNumber: "FT-2026-001");
        PurchaseOrderLine line = AddLine(order, orderedQuantity: 10);

        order.Receive(line.Id, packageQuantity: 4);

        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, order.Status);
        Assert.Equal(4, line.ReceivedPackageQuantity);
        Assert.Equal(6, line.RemainingPackageQuantity);

        order.Receive(line.Id, packageQuantity: 6);

        Assert.Equal(PurchaseOrderStatus.Received, order.Status);
        Assert.Equal(10, line.ReceivedPackageQuantity);
    }

    [Fact]
    public void ReceiptCannotExceedOrderedQuantity()
    {
        PurchaseOrder order = CreateOrder(documentNumber: "FT-2026-001");
        PurchaseOrderLine line = AddLine(order, orderedQuantity: 10);

        Assert.Throws<PurchaseValidationException>(() => order.Receive(line.Id, 11));
    }

    [Fact]
    public void ReceiptRequiresDocumentNumber()
    {
        PurchaseOrder order = CreateOrder();
        PurchaseOrderLine line = AddLine(order, orderedQuantity: 10);

        Assert.Throws<PurchaseValidationException>(() => order.Receive(line.Id, 1));
    }

    [Fact]
    public void DraftCanBeCancelledButReceivedOrderCannotBeCancelled()
    {
        PurchaseOrder draft = CreateOrder();
        AddLine(draft, orderedQuantity: 1);
        draft.Cancel();
        Assert.Equal(PurchaseOrderStatus.Cancelled, draft.Status);

        PurchaseOrder received = CreateOrder(documentNumber: "FT-2026-001");
        PurchaseOrderLine line = AddLine(received, orderedQuantity: 1);
        received.Receive(line.Id, 1);

        Assert.Throws<PurchaseValidationException>(received.Cancel);
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(1, 0, 0)]
    [InlineData(1, 1, -1)]
    [InlineData(1, 10, 11)]
    public void InvalidLineValuesAreRejected(
        long quantity,
        long unitCostXof,
        long discountXof)
    {
        PurchaseOrder order = CreateOrder();

        Assert.Throws<PurchaseValidationException>(() => order.AddLine(
            EntityId.New(),
            EntityId.New(),
            EntityId.New(),
            quantity,
            factorToBaseUnit: 1,
            unitCostXof,
            discountXof,
            notes: null));
    }

    [Fact]
    public void DraftLinesCanBeEditedAndRemoved()
    {
        PurchaseOrder order = CreateOrder();
        PurchaseOrderLine line = AddLine(order, orderedQuantity: 2);

        order.UpdateLine(
            line.Id,
            orderedPackageQuantity: 3,
            factorToBaseUnit: 24,
            unitCostXof: 900,
            discountXof: 200,
            notes: "Caixa promocional");

        Assert.Equal(2_500, line.TotalXof);
        Assert.Equal(24, line.FactorToBaseUnit);
        order.RemoveLine(line.Id);
        Assert.Empty(order.Lines);
    }

    private static PurchaseOrder CreateOrder(string? documentNumber = null) => PurchaseOrder.Create(
        EntityId.New(),
        EntityId.New(),
        EntityId.New(),
        EntityId.New(),
        documentNumber,
        documentDate: null,
        notes: null,
        UtcInstant.From(new DateTimeOffset(2026, 7, 28, 10, 0, 0, TimeSpan.Zero)));

    private static PurchaseOrderLine AddLine(PurchaseOrder order, long orderedQuantity) =>
        order.AddLine(
            EntityId.New(),
            EntityId.New(),
            EntityId.New(),
            orderedQuantity,
            factorToBaseUnit: 12,
            unitCostXof: 1_000,
            discountXof: 0,
            notes: null);
}
