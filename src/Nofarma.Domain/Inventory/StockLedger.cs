using Nofarma.Domain.Common;

namespace Nofarma.Domain.Inventory;

public static class StockLedger
{
    private static readonly HashSet<StockMovementType> IncomingTypes =
    [
        StockMovementType.OpeningInventory,
        StockMovementType.PurchaseReceipt,
        StockMovementType.QuickEntry,
        StockMovementType.PositiveAdjustment
    ];

    private static readonly HashSet<StockMovementType> OutgoingTypes =
    [
        StockMovementType.NegativeAdjustment,
        StockMovementType.Loss,
        StockMovementType.Damage,
        StockMovementType.Expiration,
        StockMovementType.SupplierReturn
    ];

    private static readonly HashSet<StockMovementType> ReasonRequiredTypes =
    [
        StockMovementType.QuickEntry,
        StockMovementType.PositiveAdjustment,
        StockMovementType.NegativeAdjustment,
        StockMovementType.Loss,
        StockMovementType.Damage,
        StockMovementType.Expiration,
        StockMovementType.SupplierReturn,
        StockMovementType.Compensation
    ];

    public static StockMovement CreateMovement(
        StockOperation operation,
        long currentLotBalance)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ValidateOperation(operation, currentLotBalance, allowCompensation: false);
        long resultingBalance = checked(currentLotBalance + operation.QuantityBase);
        if (resultingBalance < 0)
        {
            throw new InsufficientStockException(
                "A operação excede a quantidade disponível no lote.");
        }

        return new StockMovement(operation, resultingBalance);
    }

    public static StockMovement Compensate(
        StockMovement original,
        EntityId movementId,
        EntityId userId,
        string reason,
        string idempotencyKey,
        UtcInstant occurredUtc,
        long currentLotBalance)
    {
        ArgumentNullException.ThrowIfNull(original);
        var operation = new StockOperation(
            movementId,
            original.PharmacyId,
            original.ProductId,
            original.LotId,
            checked(-original.QuantityBase),
            StockMovementType.Compensation,
            reason,
            original.SourceDocumentId,
            userId,
            occurredUtc,
            idempotencyKey);
        ValidateOperation(operation, currentLotBalance, allowCompensation: true);
        long resultingBalance = checked(currentLotBalance + operation.QuantityBase);
        if (resultingBalance < 0)
        {
            throw new InsufficientStockException(
                "A compensação excede a quantidade disponível no lote.");
        }

        return new StockMovement(operation, resultingBalance, original.Id);
    }

    public static StockMovement Compensate(
        StockOperation originalOperation,
        long originalResultingLotBalance,
        EntityId movementId,
        EntityId userId,
        string reason,
        string idempotencyKey,
        UtcInstant occurredUtc,
        long currentLotBalance)
    {
        ArgumentNullException.ThrowIfNull(originalOperation);
        var original = new StockMovement(originalOperation, originalResultingLotBalance);
        return Compensate(
            original,
            movementId,
            userId,
            reason,
            idempotencyKey,
            occurredUtc,
            currentLotBalance);
    }

    private static void ValidateOperation(
        StockOperation operation,
        long currentLotBalance,
        bool allowCompensation)
    {
        if (currentLotBalance < 0)
        {
            throw new InventoryValidationException("O saldo actual do lote não pode ser negativo.");
        }

        EnsureIdentifier(operation.MovementId, "O movimento deve ter um identificador.");
        EnsureIdentifier(operation.PharmacyId, "A farmácia do movimento é obrigatória.");
        EnsureIdentifier(operation.ProductId, "O produto do movimento é obrigatório.");
        EnsureIdentifier(operation.UserId, "O utilizador do movimento é obrigatório.");
        if (operation.LotId is { Value: var lotValue } && lotValue == Guid.Empty)
        {
            throw new InventoryValidationException("O lote do movimento não é válido.");
        }

        if (operation.QuantityBase == 0)
        {
            throw new InventoryValidationException("A quantidade do movimento não pode ser zero.");
        }

        if (IncomingTypes.Contains(operation.Type) && operation.QuantityBase < 0)
        {
            throw new InventoryValidationException("Um movimento de entrada exige quantidade positiva.");
        }

        if (OutgoingTypes.Contains(operation.Type) && operation.QuantityBase > 0)
        {
            throw new InventoryValidationException("Um movimento de saída exige quantidade negativa.");
        }

        if (operation.Type == StockMovementType.Compensation && !allowCompensation)
        {
            throw new InventoryValidationException(
                "Uma compensação deve ser criada a partir do movimento original.");
        }

        if (!IncomingTypes.Contains(operation.Type) &&
            !OutgoingTypes.Contains(operation.Type) &&
            operation.Type != StockMovementType.Compensation)
        {
            throw new InventoryValidationException("O tipo de movimento não é suportado.");
        }

        if (ReasonRequiredTypes.Contains(operation.Type) &&
            string.IsNullOrWhiteSpace(operation.Reason))
        {
            throw new InventoryValidationException("O motivo do movimento é obrigatório.");
        }

        if (string.IsNullOrWhiteSpace(operation.IdempotencyKey))
        {
            throw new InventoryValidationException("A chave idempotente é obrigatória.");
        }
    }

    private static void EnsureIdentifier(EntityId id, string message)
    {
        if (id.Value == Guid.Empty)
        {
            throw new InventoryValidationException(message);
        }
    }
}
