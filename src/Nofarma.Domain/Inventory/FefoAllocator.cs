using Nofarma.Domain.Common;

namespace Nofarma.Domain.Inventory;

public sealed record StockLotAvailability(StockLot Lot, long AvailableQuantityBase);

public sealed record StockAllocation(EntityId LotId, long QuantityBase);

public static class FefoAllocator
{
    public static IReadOnlyList<StockAllocation> Allocate(
        long requiredQuantityBase,
        IReadOnlyCollection<StockLotAvailability> availability,
        DateOnly businessDate)
    {
        ArgumentNullException.ThrowIfNull(availability);
        if (requiredQuantityBase <= 0)
        {
            throw new InventoryValidationException("A quantidade pedida deve ser positiva.");
        }

        if (availability.Any(item => item.AvailableQuantityBase < 0))
        {
            throw new InventoryValidationException("A disponibilidade de um lote não pode ser negativa.");
        }

        EntityId[] productIds = availability
            .Select(item => item.Lot.ProductId)
            .Distinct()
            .Take(2)
            .ToArray();
        if (productIds.Length > 1)
        {
            throw new InventoryValidationException(
                "A alocação FEFO só pode conter lotes do mesmo produto.");
        }

        StockLotAvailability[] eligible = availability
            .Where(item => item.AvailableQuantityBase > 0)
            .Where(item => !item.Lot.IsBlockedAt(businessDate))
            .OrderBy(item => item.Lot.Expiry?.BlockingDate ?? DateOnly.MaxValue)
            .ThenBy(item => item.Lot.FirstEntryUtc.Value)
            .ThenBy(item => item.Lot.Id.Value)
            .ToArray();

        long totalAvailable = eligible.Aggregate(
            0L,
            (total, item) => checked(total + item.AvailableQuantityBase));
        if (totalAvailable < requiredQuantityBase)
        {
            throw new InsufficientStockException(
                "O stock válido disponível não cobre a quantidade pedida.");
        }

        var allocations = new List<StockAllocation>();
        long remaining = requiredQuantityBase;
        foreach (StockLotAvailability item in eligible)
        {
            if (remaining == 0)
            {
                break;
            }

            long allocated = Math.Min(remaining, item.AvailableQuantityBase);
            allocations.Add(new StockAllocation(item.Lot.Id, allocated));
            remaining -= allocated;
        }

        return allocations.AsReadOnly();
    }
}
