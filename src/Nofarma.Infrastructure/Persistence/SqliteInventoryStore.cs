using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Inventory;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Catalog;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;
using Nofarma.Domain.Inventory;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.Infrastructure.Persistence;

public sealed class SqliteInventoryStore(
    DbContextOptions<NofarmaDbContext> options) : IInventoryStore
{
    public async Task<InventoryActorContext?> GetContextAsync(
        EntityId actorUserId,
        CancellationToken cancellationToken)
    {
        await using var context = new NofarmaDbContext(options);
        LocalUserRecord? actor = await context.LocalUsers.AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.Id == actorUserId.Value &&
                    record.Status == (int)UserStatus.Active,
                cancellationToken).ConfigureAwait(false);
        if (actor is null)
        {
            return null;
        }

        Guid deviceId = await context.Installations.AsNoTracking()
            .Where(record => record.PharmacyId == actor.PharmacyId)
            .Select(record => record.DeviceId)
            .SingleAsync(cancellationToken).ConfigureAwait(false);
        return new InventoryActorContext(
            new EntityId(actor.PharmacyId),
            new EntityId(deviceId));
    }

    public async Task<StockProductRules?> GetProductRulesAsync(
        EntityId pharmacyId,
        EntityId productId,
        CancellationToken cancellationToken)
    {
        await using var context = new NofarmaDbContext(options);
        return await context.Products.AsNoTracking()
            .Where(record => record.PharmacyId == pharmacyId.Value &&
                record.Id == productId.Value)
            .Select(record => new StockProductRules(
                (ProductType)record.Type,
                record.RequiresLot,
                record.RequiresExpiry,
                record.IsActive))
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<StockConfirmationResult> ConfirmAsync(
        InventoryActorContext context,
        InventoryConfirmation confirmation,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(confirmation);
        ValidateContext(context, confirmation, auditEvent);
        await using var dbContext = new NofarmaDbContext(options);
        await dbContext.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var connection = (SqliteConnection)dbContext.Database.GetDbConnection();
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        dbContext.Database.UseTransaction(transaction);

        StockMovementRecord? existing = await dbContext.StockMovements.AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.PharmacyId == context.PharmacyId.Value &&
                    record.IdempotencyKey == confirmation.Operation.IdempotencyKey.Trim(),
                cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return MapResult(existing);
        }

        ProductRecord product = await dbContext.Products.SingleOrDefaultAsync(
            record => record.PharmacyId == context.PharmacyId.Value &&
                record.Id == confirmation.Operation.ProductId.Value &&
                record.IsActive,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InventoryValidationException("O produto indicado não está activo.");
        StockLotRecord lot = await ResolveLotAsync(
            dbContext,
            context,
            confirmation,
            cancellationToken).ConfigureAwait(false);
        ValidateLot(product, lot, confirmation.Operation);
        StockOperation operation = confirmation.Operation with { LotId = new EntityId(lot.Id) };
        StockMovement movement = StockLedger.CreateMovement(
            operation,
            lot.AvailableQuantityBase);
        if (dbContext.Entry(lot).State == EntityState.Added)
        {
            lot.AvailableQuantityBase = movement.ResultingLotBalance;
            lot.QuantityReceivedBase = movement.QuantityBase;
            lot.RowVersion = 1;
        }
        else
        {
            await UpdateExistingLotBalanceAsync(
                dbContext,
                lot,
                movement,
                cancellationToken).ConfigureAwait(false);
        }

        product.HasMovements = true;
        product.UpdatedAtUtc = operation.OccurredUtc.Value;
        dbContext.StockMovements.Add(MapMovement(movement));
        dbContext.AuditEvents.Add(InventoryPersistenceMapper.MapAudit(auditEvent));
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return MapResult(movement);
    }

    public async Task<StockConfirmationResult> CompensateAsync(
        InventoryActorContext context,
        StockCompensationCommand command,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        if (auditEvent.PharmacyId != context.PharmacyId ||
            auditEvent.DeviceId != context.DeviceId ||
            auditEvent.UserId != command.UserId ||
            auditEvent.ObjectId != command.MovementId.Value.ToString("D"))
        {
            throw new InventoryValidationException("O contexto da compensação não é válido.");
        }

        await using var dbContext = new NofarmaDbContext(options);
        await dbContext.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var connection = (SqliteConnection)dbContext.Database.GetDbConnection();
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        dbContext.Database.UseTransaction(transaction);
        StockMovementRecord? repeated = await dbContext.StockMovements.AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.PharmacyId == context.PharmacyId.Value &&
                    record.IdempotencyKey == command.IdempotencyKey,
                cancellationToken).ConfigureAwait(false);
        if (repeated is not null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return MapResult(repeated);
        }

        StockMovementRecord original = await dbContext.StockMovements.AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.PharmacyId == context.PharmacyId.Value &&
                    record.Id == command.OriginalMovementId.Value,
                cancellationToken).ConfigureAwait(false)
            ?? throw new InventoryValidationException("O movimento original não existe.");
        if (original.Type == (int)StockMovementType.Compensation)
        {
            throw new InventoryValidationException("Uma compensação não pode compensar outra.");
        }

        if (original.StockLotId is null)
        {
            throw new InventoryValidationException("O movimento original não tem lote.");
        }

        StockLotRecord lot = await dbContext.StockLots.SingleAsync(
            record => record.Id == original.StockLotId.Value,
            cancellationToken).ConfigureAwait(false);
        var originalOperation = new StockOperation(
            new EntityId(original.Id),
            new EntityId(original.PharmacyId),
            new EntityId(original.ProductId),
            new EntityId(original.StockLotId.Value),
            original.QuantityBase,
            (StockMovementType)original.Type,
            original.Reason,
            original.SourceDocumentId is { } sourceId ? new EntityId(sourceId) : null,
            new EntityId(original.UserId),
            UtcInstant.From(original.OccurredAtUtc),
            original.IdempotencyKey);
        StockMovement compensation = StockLedger.Compensate(
            originalOperation,
            original.ResultingLotBalance,
            command.MovementId,
            command.UserId,
            command.Reason,
            command.IdempotencyKey,
            command.OccurredUtc,
            lot.AvailableQuantityBase);
        await UpdateExistingLotBalanceAsync(
            dbContext,
            lot,
            compensation,
            cancellationToken).ConfigureAwait(false);
        dbContext.StockMovements.Add(MapMovement(compensation));
        dbContext.AuditEvents.Add(InventoryPersistenceMapper.MapAudit(auditEvent));
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return MapResult(compensation);
    }

    public async Task<ProductStockDetails?> GetProductStockAsync(
        EntityId pharmacyId,
        EntityId productId,
        DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        await using var dbContext = new NofarmaDbContext(options);
        ProductRecord? product = await dbContext.Products.AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.PharmacyId == pharmacyId.Value &&
                    record.Id == productId.Value,
                cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return null;
        }

        StockLotRecord[] lots = await dbContext.StockLots.AsNoTracking()
            .Where(record => record.PharmacyId == pharmacyId.Value &&
                record.ProductId == productId.Value)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        lots = lots
            .OrderBy(record => record.ExpiryYear)
            .ThenBy(record => record.ExpiryMonth)
            .ThenBy(record => record.ExpiryDay)
            .ThenBy(record => record.FirstEntryAtUtc)
            .ToArray();
        StockMovementRecord[] movements = await dbContext.StockMovements.AsNoTracking()
            .Where(record => record.PharmacyId == pharmacyId.Value &&
                record.ProductId == productId.Value)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        movements = movements
            .OrderByDescending(record => record.OccurredAtUtc)
            .ThenByDescending(record => record.Id)
            .ToArray();
        long total = lots.Sum(record => record.AvailableQuantityBase);
        StockAlert stockAlert = StockAlertCalculator.CalculateStock(
            total,
            product.MinimumStockBase);
        StockLotSummary[] lotDetails = lots.Select(record =>
        {
            ExpiryDate? expiry = GetExpiry(record);
            StockAlert expiryAlert = StockAlertCalculator.CalculateExpiry(expiry, businessDate);
            return new StockLotSummary(
                new EntityId(record.Id),
                record.Number,
                record.AvailableQuantityBase,
                expiry?.BlockingDate,
                expiryAlert.Level);
        }).ToArray();
        StockMovementSummary[] movementDetails = movements.Select(record =>
            new StockMovementSummary(
                new EntityId(record.Id),
                record.StockLotId is { } lotId ? new EntityId(lotId) : null,
                record.QuantityBase,
                (StockMovementType)record.Type,
                record.Reason,
                UtcInstant.From(record.OccurredAtUtc),
                record.CompensatesMovementId is { } compensatedId
                    ? new EntityId(compensatedId)
                    : null)).ToArray();
        return new ProductStockDetails(
            new EntityId(product.Id),
            product.Code,
            product.Name,
            total,
            stockAlert.Threshold ?? StockAlertCalculator.GlobalMinimumStock,
            stockAlert.Level,
            lotDetails,
            movementDetails);
    }

    public async Task<StockOverview> SearchStockAsync(
        EntityId pharmacyId,
        string query,
        DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        await using var dbContext = new NofarmaDbContext(options);
        string normalizedQuery = query.Trim().ToUpperInvariant();
        var records = await (
            from product in dbContext.Products.AsNoTracking()
            where product.PharmacyId == pharmacyId.Value
                && product.IsActive
                && (normalizedQuery == string.Empty
                    || product.NormalizedCode.Contains(normalizedQuery)
                    || EF.Functions.Like(product.Name, $"%{query.Trim()}%"))
            join lotRecord in dbContext.StockLots.AsNoTracking()
                on product.Id equals lotRecord.ProductId into productLots
            from lot in productLots.DefaultIfEmpty()
            join supplierRecord in dbContext.Suppliers.AsNoTracking()
                on lot.SupplierId equals supplierRecord.Id into lotSuppliers
            from supplier in lotSuppliers.DefaultIfEmpty()
            select new
            {
                product.Id,
                product.Code,
                product.Name,
                product.BaseUnit,
                product.MinimumStockBase,
                LotId = lot == null ? (Guid?)null : lot.Id,
                LotNumber = lot == null ? null : lot.Number,
                Quantity = lot == null ? 0 : lot.AvailableQuantityBase,
                ExpiryYear = lot == null ? null : lot.ExpiryYear,
                ExpiryMonth = lot == null ? null : lot.ExpiryMonth,
                ExpiryDay = lot == null ? null : lot.ExpiryDay,
                SupplierName = supplier == null ? null : supplier.Name,
                FirstEntry = lot == null ? (DateTimeOffset?)null : lot.FirstEntryAtUtc
            }).ToArrayAsync(cancellationToken);

        var productAlerts = records.GroupBy(record => record.Id).ToDictionary(
            group => group.Key,
            group => StockAlertCalculator.CalculateStock(
                group.Sum(record => record.Quantity),
                group.First().MinimumStockBase));
        StockOverviewItem[] items = records.Select(record =>
        {
            ExpiryDate? expiry = record.ExpiryYear is int year && record.ExpiryMonth is int month
                ? record.ExpiryDay is int day
                    ? ExpiryDate.ForDay(year, month, day)
                    : ExpiryDate.ForMonth(year, month)
                : null;
            StockAlertLevel expiryLevel = StockAlertCalculator.CalculateExpiry(expiry, businessDate).Level;
            return new StockOverviewItem(
                new EntityId(record.Id),
                record.Code,
                record.Name,
                record.LotId is Guid lotId ? new EntityId(lotId) : null,
                record.LotNumber ?? "Sem lote",
                record.Quantity,
                record.BaseUnit,
                expiry?.BlockingDate,
                record.SupplierName,
                productAlerts[record.Id].Level,
                expiryLevel);
        }).OrderByDescending(item => item.ExpiryAlertLevel)
            .ThenBy(item => item.ExpiryDate)
            .ThenBy(item => item.ProductName)
            .ToArray();

        return new StockOverview(
            items,
            productAlerts.Count(pair => pair.Value.Level == StockAlertLevel.LowStock),
            productAlerts.Count(pair => pair.Value.Level == StockAlertLevel.OutOfStock),
            items.Count(item => item.ExpiryAlertLevel >= StockAlertLevel.ExpiryAttention));
    }

    public async Task<IReadOnlyList<StockAllocation>> AllocateFefoAsync(
        EntityId pharmacyId,
        EntityId productId,
        long requiredQuantityBase,
        DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        await using var dbContext = new NofarmaDbContext(options);
        StockLotRecord[] records = await dbContext.StockLots.AsNoTracking()
            .Where(record => record.PharmacyId == pharmacyId.Value &&
                record.ProductId == productId.Value &&
                record.AvailableQuantityBase > 0)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        StockLotAvailability[] availability = records.Select(record =>
            new StockLotAvailability(
                StockLot.Create(
                    new EntityId(record.Id),
                    new EntityId(record.ProductId),
                    record.Number,
                    GetExpiry(record),
                    record.SupplierId is { } supplierId ? new EntityId(supplierId) : null,
                    Money.Xof(record.OriginCostXof),
                    UtcInstant.From(record.FirstEntryAtUtc)),
                record.AvailableQuantityBase)).ToArray();
        return FefoAllocator.Allocate(requiredQuantityBase, availability, businessDate);
    }

    private static async Task<StockLotRecord> ResolveLotAsync(
        NofarmaDbContext dbContext,
        InventoryActorContext context,
        InventoryConfirmation confirmation,
        CancellationToken cancellationToken)
    {
        StockOperation operation = confirmation.Operation;
        if (confirmation.Lot is null)
        {
            if (operation.LotId is null)
            {
                throw new InventoryValidationException("O lote do movimento é obrigatório.");
            }

            return await dbContext.StockLots.SingleOrDefaultAsync(
                record => record.Id == operation.LotId.Value.Value &&
                    record.ProductId == operation.ProductId.Value &&
                    record.PharmacyId == context.PharmacyId.Value,
                cancellationToken).ConfigureAwait(false)
                ?? throw new InventoryValidationException("O lote indicado não existe.");
        }

        StockLotDefinition definition = confirmation.Lot;
        string normalizedNumber = definition.Number.Trim().ToUpperInvariant();
        StockLotRecord? existing = await dbContext.StockLots.SingleOrDefaultAsync(
            record => record.ProductId == operation.ProductId.Value &&
                record.NormalizedNumber == normalizedNumber,
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.ExpiryYear != definition.Expiry?.Year ||
                existing.ExpiryMonth != definition.Expiry?.Month ||
                existing.ExpiryDay != definition.Expiry?.Day)
            {
                throw new InventoryValidationException(
                    "O número do lote já existe com uma validade diferente.");
            }

            return existing;
        }

        StockLot lot = StockLot.Create(
            definition.ProposedId,
            operation.ProductId,
            definition.Number,
            definition.Expiry,
            definition.SupplierId,
            Money.Xof(definition.OriginCostXof),
            operation.OccurredUtc);
        var record = new StockLotRecord
        {
            Id = lot.Id.Value,
            PharmacyId = context.PharmacyId.Value,
            ProductId = lot.ProductId.Value,
            Number = lot.Number,
            NormalizedNumber = lot.Number,
            ExpiryYear = lot.Expiry?.Year,
            ExpiryMonth = lot.Expiry?.Month,
            ExpiryDay = lot.Expiry?.Day,
            SupplierId = lot.SupplierId?.Value,
            OriginCostXof = lot.OriginCost.Amount,
            FirstEntryAtUtc = lot.FirstEntryUtc.Value,
            RowVersion = 0
        };
        dbContext.StockLots.Add(record);
        return record;
    }

    private static void ValidateLot(
        ProductRecord product,
        StockLotRecord lot,
        StockOperation operation)
    {
        if (product.RequiresLot && lot.Number == "SEM-LOTE")
        {
            throw new InventoryValidationException("O número do lote é obrigatório.");
        }

        if (product.RequiresExpiry && lot.ExpiryYear is null)
        {
            throw new InventoryValidationException("A validade do lote é obrigatória.");
        }

        if (operation.QuantityBase > 0 && GetExpiry(lot) is { } expiry)
        {
            DateOnly businessDate = DateOnly.FromDateTime(operation.OccurredUtc.Value.UtcDateTime);
            if (businessDate >= expiry.BlockingDate)
            {
                throw new InventoryValidationException("Não é permitido receber um lote expirado.");
            }
        }
    }

    private static async Task UpdateExistingLotBalanceAsync(
        NofarmaDbContext dbContext,
        StockLotRecord lot,
        StockMovement movement,
        CancellationToken cancellationToken)
    {
        long expectedBalance = lot.AvailableQuantityBase;
        long quantity = movement.QuantityBase;
        int affected;
        if (quantity > 0)
        {
            affected = await dbContext.StockLots
                .Where(record => record.Id == lot.Id &&
                    record.AvailableQuantityBase == expectedBalance &&
                    record.AvailableQuantityBase + quantity >= 0)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            record => record.AvailableQuantityBase,
                            record => record.AvailableQuantityBase + quantity)
                        .SetProperty(
                            record => record.QuantityReceivedBase,
                            record => record.QuantityReceivedBase + quantity)
                        .SetProperty(record => record.RowVersion, record => record.RowVersion + 1),
                    cancellationToken).ConfigureAwait(false);
        }
        else
        {
            affected = await dbContext.StockLots
                .Where(record => record.Id == lot.Id &&
                    record.AvailableQuantityBase == expectedBalance &&
                    record.AvailableQuantityBase + quantity >= 0)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            record => record.AvailableQuantityBase,
                            record => record.AvailableQuantityBase + quantity)
                        .SetProperty(record => record.RowVersion, record => record.RowVersion + 1),
                    cancellationToken).ConfigureAwait(false);
        }

        if (affected != 1)
        {
            throw new InsufficientStockException(
                "O saldo do lote mudou ou deixou de cobrir a operação.");
        }
    }

    private static ExpiryDate? GetExpiry(StockLotRecord lot)
    {
        if (lot.ExpiryYear is null || lot.ExpiryMonth is null)
        {
            return null;
        }

        return lot.ExpiryDay is { } day
            ? ExpiryDate.ForDay(lot.ExpiryYear.Value, lot.ExpiryMonth.Value, day)
            : ExpiryDate.ForMonth(lot.ExpiryYear.Value, lot.ExpiryMonth.Value);
    }

    private static void ValidateContext(
        InventoryActorContext context,
        InventoryConfirmation confirmation,
        AuditEvent auditEvent)
    {
        if (confirmation.Operation.PharmacyId != context.PharmacyId ||
            auditEvent.PharmacyId != context.PharmacyId ||
            auditEvent.DeviceId != context.DeviceId ||
            auditEvent.UserId != confirmation.Operation.UserId ||
            auditEvent.ObjectId != confirmation.Operation.MovementId.Value.ToString("D"))
        {
            throw new InventoryValidationException("O contexto da confirmação não é válido.");
        }
    }

    private static StockMovementRecord MapMovement(StockMovement movement) => new()
    {
        Id = movement.Id.Value,
        PharmacyId = movement.PharmacyId.Value,
        ProductId = movement.ProductId.Value,
        StockLotId = movement.LotId?.Value,
        QuantityBase = movement.QuantityBase,
        Type = (int)movement.Type,
        Reason = movement.Reason,
        SourceDocumentId = movement.SourceDocumentId?.Value,
        UserId = movement.UserId.Value,
        OccurredAtUtc = movement.OccurredUtc.Value,
        IdempotencyKey = movement.IdempotencyKey,
        CompensatesMovementId = movement.CompensatesMovementId?.Value,
        ResultingLotBalance = movement.ResultingLotBalance
    };

    private static StockConfirmationResult MapResult(StockMovement movement) => new(
        movement.Id,
        movement.ProductId,
        movement.LotId,
        movement.QuantityBase,
        movement.ResultingLotBalance,
        movement.Type,
        movement.OccurredUtc);

    private static StockConfirmationResult MapResult(StockMovementRecord movement) => new(
        new EntityId(movement.Id),
        new EntityId(movement.ProductId),
        movement.StockLotId is { } lotId ? new EntityId(lotId) : null,
        movement.QuantityBase,
        movement.ResultingLotBalance,
        (StockMovementType)movement.Type,
        UtcInstant.From(movement.OccurredAtUtc));
}
