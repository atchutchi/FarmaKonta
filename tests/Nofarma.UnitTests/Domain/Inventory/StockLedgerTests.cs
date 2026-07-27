using Nofarma.Domain.Common;
using Nofarma.Domain.Inventory;

namespace Nofarma.UnitTests.Domain.Inventory;

public sealed class StockLedgerTests
{
    [Theory]
    [InlineData(StockMovementType.OpeningInventory)]
    [InlineData(StockMovementType.PurchaseReceipt)]
    [InlineData(StockMovementType.QuickEntry)]
    [InlineData(StockMovementType.PositiveAdjustment)]
    public void IncomingMovementTypesRequirePositiveQuantity(StockMovementType type)
    {
        StockMovement movement = StockLedger.CreateMovement(
            CreateOperation(type, quantityBase: 5, ReasonFor(type)),
            currentLotBalance: 0);

        Assert.Equal(5, movement.QuantityBase);
        Assert.Equal(type, movement.Type);
    }

    [Theory]
    [InlineData(StockMovementType.NegativeAdjustment)]
    [InlineData(StockMovementType.Loss)]
    [InlineData(StockMovementType.Damage)]
    [InlineData(StockMovementType.Expiration)]
    [InlineData(StockMovementType.SupplierReturn)]
    public void OutgoingMovementTypesRequireNegativeQuantity(StockMovementType type)
    {
        StockMovement movement = StockLedger.CreateMovement(
            CreateOperation(type, quantityBase: -5, ReasonFor(type)),
            currentLotBalance: 10);

        Assert.Equal(-5, movement.QuantityBase);
        Assert.Equal(5, movement.ResultingLotBalance);
    }

    [Fact]
    public void MovementRejectsZeroQuantity()
    {
        Assert.Throws<InventoryValidationException>(() => StockLedger.CreateMovement(
            CreateOperation(StockMovementType.OpeningInventory, 0, reason: null),
            currentLotBalance: 0));
    }

    [Fact]
    public void IncomingMovementRejectsNegativeQuantity()
    {
        Assert.Throws<InventoryValidationException>(() => StockLedger.CreateMovement(
            CreateOperation(StockMovementType.PurchaseReceipt, -1, reason: null),
            currentLotBalance: 10));
    }

    [Fact]
    public void OutgoingMovementRejectsPositiveQuantity()
    {
        Assert.Throws<InventoryValidationException>(() => StockLedger.CreateMovement(
            CreateOperation(StockMovementType.Loss, 1, "Perda confirmada"),
            currentLotBalance: 10));
    }

    [Fact]
    public void MovementRejectsBlankIdempotencyKey()
    {
        Assert.Throws<InventoryValidationException>(() => StockLedger.CreateMovement(
            CreateOperation(
                StockMovementType.OpeningInventory,
                1,
                reason: null,
                idempotencyKey: " "),
            currentLotBalance: 0));
    }

    [Fact]
    public void CompensationCannotBeCreatedWithoutOriginalMovement()
    {
        Assert.Throws<InventoryValidationException>(() => StockLedger.CreateMovement(
            CreateOperation(
                StockMovementType.Compensation,
                -1,
                "Correcção do movimento original"),
            currentLotBalance: 10));
    }

    [Theory]
    [InlineData(StockMovementType.QuickEntry)]
    [InlineData(StockMovementType.PositiveAdjustment)]
    [InlineData(StockMovementType.NegativeAdjustment)]
    [InlineData(StockMovementType.Loss)]
    [InlineData(StockMovementType.Damage)]
    [InlineData(StockMovementType.Expiration)]
    [InlineData(StockMovementType.SupplierReturn)]
    public void ManualAndExceptionalMovementsRequireReason(StockMovementType type)
    {
        long quantity = type is StockMovementType.QuickEntry or StockMovementType.PositiveAdjustment
            ? 1
            : -1;

        Assert.Throws<InventoryValidationException>(() => StockLedger.CreateMovement(
            CreateOperation(type, quantity, reason: " "),
            currentLotBalance: 10));
    }

    [Fact]
    public void OutgoingMovementCannotMakeBalanceNegative()
    {
        Assert.Throws<InsufficientStockException>(() => StockLedger.CreateMovement(
            CreateOperation(StockMovementType.Loss, -11, "Quebra durante transporte"),
            currentLotBalance: 10));
    }

    [Fact]
    public void MovementPreservesOriginUserAndIdempotencyKey()
    {
        EntityId userId = EntityId.New();
        EntityId sourceId = EntityId.New();
        StockOperation operation = CreateOperation(
            StockMovementType.PurchaseReceipt,
            10,
            reason: null,
            userId,
            sourceId,
            "receipt:2026:42");

        StockMovement movement = StockLedger.CreateMovement(operation, currentLotBalance: 3);

        Assert.Equal(userId, movement.UserId);
        Assert.Equal(sourceId, movement.SourceDocumentId);
        Assert.Equal("receipt:2026:42", movement.IdempotencyKey);
        Assert.Equal(13, movement.ResultingLotBalance);
    }

    [Fact]
    public void CompensationCreatesOppositeMovementWithoutEditingOriginal()
    {
        StockMovement original = StockLedger.CreateMovement(
            CreateOperation(
                StockMovementType.PositiveAdjustment,
                7,
                "Contagem inicial corrigida"),
            currentLotBalance: 3);

        StockMovement compensation = StockLedger.Compensate(
            original,
            EntityId.New(),
            EntityId.New(),
            "Ajuste introduzido no produto errado",
            "compensation:42",
            AtUtc(2026, 7, 28),
            currentLotBalance: 10);

        Assert.Equal(7, original.QuantityBase);
        Assert.Equal(-7, compensation.QuantityBase);
        Assert.Equal(StockMovementType.Compensation, compensation.Type);
        Assert.Equal(original.Id, compensation.CompensatesMovementId);
        Assert.Equal(3, compensation.ResultingLotBalance);
    }

    private static StockOperation CreateOperation(
        StockMovementType type,
        long quantityBase,
        string? reason,
        EntityId? userId = null,
        EntityId? sourceDocumentId = null,
        string idempotencyKey = "operation:42") => new(
            EntityId.New(),
            EntityId.New(),
            EntityId.New(),
            EntityId.New(),
            quantityBase,
            type,
            reason,
            sourceDocumentId,
            userId ?? EntityId.New(),
            AtUtc(2026, 7, 27),
            idempotencyKey);

    private static string? ReasonFor(StockMovementType type) => type switch
    {
        StockMovementType.QuickEntry => "Entrada autorizada",
        StockMovementType.PositiveAdjustment => "Correcção de contagem",
        StockMovementType.NegativeAdjustment => "Correcção de contagem",
        StockMovementType.Loss => "Perda confirmada",
        StockMovementType.Damage => "Produto danificado",
        StockMovementType.Expiration => "Validade terminada",
        StockMovementType.SupplierReturn => "Devolução aceite",
        _ => null
    };

    private static UtcInstant AtUtc(int year, int month, int day) => UtcInstant.From(
        new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero));
}
