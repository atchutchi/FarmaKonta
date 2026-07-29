using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Purchasing;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;
using Nofarma.Domain.Inventory;
using Nofarma.Domain.Purchasing;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.Infrastructure.Persistence;

public sealed class SqlitePurchaseStore(
    DbContextOptions<NofarmaDbContext> options) : IPurchaseStore
{
    public async Task<PurchaseActorContext?> GetContextAsync(
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
        return new PurchaseActorContext(
            new EntityId(actor.PharmacyId),
            new EntityId(deviceId),
            actorUserId);
    }

    public async Task<PurchaseDetails> CreateAsync(
        PurchaseActorContext context,
        EntityId purchaseId,
        IReadOnlyList<EntityId> lineIds,
        EntityId actorUserId,
        CreatePurchaseRequest request,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        ValidateAudit(context, actorUserId, purchaseId, auditEvent);
        if (lineIds.Count != request.Lines.Count)
        {
            throw new PurchaseValidationException("Cada linha deve ter um identificador.");
        }

        await using var dbContext = new NofarmaDbContext(options);
        bool supplierExists = await dbContext.Suppliers.AnyAsync(
            record => record.Id == request.SupplierId.Value &&
                record.PharmacyId == context.PharmacyId.Value &&
                record.IsActive,
            cancellationToken).ConfigureAwait(false);
        if (!supplierExists)
        {
            throw new PurchaseValidationException("O fornecedor indicado não está activo.");
        }

        PurchaseOrder order = PurchaseOrder.Create(
            purchaseId,
            context.PharmacyId,
            request.SupplierId,
            actorUserId,
            request.DocumentNumber,
            request.DocumentDate,
            request.Notes,
            auditEvent.OccurredAtUtc);
        for (int index = 0; index < request.Lines.Count; index++)
        {
            CreatePurchaseLineRequest lineRequest = request.Lines[index];
            await ValidateCatalogLineAsync(
                dbContext,
                context.PharmacyId,
                lineRequest.ProductId,
                lineRequest.PackageId,
                lineRequest.FactorToBaseUnit,
                cancellationToken).ConfigureAwait(false);
            order.AddLine(
                lineIds[index],
                lineRequest.ProductId,
                lineRequest.PackageId,
                lineRequest.OrderedPackageQuantity,
                lineRequest.FactorToBaseUnit,
                lineRequest.UnitCostXof,
                lineRequest.DiscountXof,
                lineRequest.Notes);
        }

        DateTimeOffset now = auditEvent.OccurredAtUtc.Value;
        dbContext.PurchaseOrders.Add(new PurchaseOrderRecord
        {
            Id = order.Id.Value,
            PharmacyId = order.PharmacyId.Value,
            SupplierId = order.SupplierId.Value,
            Status = (int)order.Status,
            DocumentNumber = order.DocumentNumber,
            DocumentDate = order.DocumentDate,
            Notes = order.Notes,
            CreatedByUserId = order.CreatedByUserId.Value,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        dbContext.PurchaseOrderLines.AddRange(
            order.Lines.Select(line => MapLine(line, order.Id)));
        dbContext.AuditEvents.Add(InventoryPersistenceMapper.MapAudit(auditEvent));
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await MapDetailsAsync(
            dbContext,
            order.Id,
            includeCosts: true,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("A compra criada não foi encontrada.");
    }

    public async Task<PurchaseDetails?> GetAsync(
        EntityId pharmacyId,
        EntityId purchaseId,
        bool includeCosts,
        CancellationToken cancellationToken)
    {
        await using var context = new NofarmaDbContext(options);
        bool belongsToPharmacy = await context.PurchaseOrders.AsNoTracking().AnyAsync(
            record => record.Id == purchaseId.Value &&
                record.PharmacyId == pharmacyId.Value,
            cancellationToken).ConfigureAwait(false);
        return belongsToPharmacy
            ? await MapDetailsAsync(context, purchaseId, includeCosts, cancellationToken)
                .ConfigureAwait(false)
            : null;
    }

    public async Task<IReadOnlyList<PurchaseSummary>> SearchAsync(
        EntityId pharmacyId,
        string query,
        CancellationToken cancellationToken)
    {
        await using var context = new NofarmaDbContext(options);
        string normalized = query.Trim().ToUpperInvariant();
        var source =
            from purchase in context.PurchaseOrders.AsNoTracking()
            join supplier in context.Suppliers.AsNoTracking()
                on purchase.SupplierId equals supplier.Id
            where purchase.PharmacyId == pharmacyId.Value
            select new { Purchase = purchase, Supplier = supplier };
        if (normalized.Length > 0)
        {
            string pattern = $"%{normalized}%";
            source = source.Where(item =>
                EF.Functions.Like(item.Supplier.NormalizedName, pattern) ||
                (item.Purchase.DocumentNumber != null &&
                    EF.Functions.Like(item.Purchase.DocumentNumber, pattern)));
        }

        var records = await source
            .Select(item => new
            {
                item.Purchase.UpdatedAtUtc,
                Summary = new PurchaseSummary(
                    new EntityId(item.Purchase.Id),
                    new EntityId(item.Supplier.Id),
                    item.Supplier.Name,
                    (PurchaseOrderStatus)item.Purchase.Status,
                    item.Purchase.DocumentNumber,
                    item.Purchase.DocumentDate,
                    context.PurchaseOrderLines.Count(
                        line => line.PurchaseOrderId == item.Purchase.Id))
            })
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return records
            .OrderByDescending(record => record.UpdatedAtUtc)
            .Select(record => record.Summary)
            .ToArray();
    }

    public async Task<PurchaseDetails> UpdateAsync(
        PurchaseActorContext context,
        EntityId purchaseId,
        UpdatePurchaseRequest request,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        ValidateAudit(context, auditEvent.UserId, purchaseId, auditEvent);
        await using var dbContext = new NofarmaDbContext(options);
        PurchaseOrderRecord record = await GetDraftAsync(
            dbContext,
            context.PharmacyId,
            purchaseId,
            cancellationToken).ConfigureAwait(false);
        var validated = PurchaseOrder.Create(
            purchaseId,
            context.PharmacyId,
            new EntityId(record.SupplierId),
            new EntityId(record.CreatedByUserId),
            request.DocumentNumber,
            request.DocumentDate,
            request.Notes,
            UtcInstant.From(record.CreatedAtUtc));
        foreach (UpdatePurchaseLineRequest line in request.Lines)
        {
            await ValidateCatalogLineAsync(
                dbContext,
                context.PharmacyId,
                line.ProductId,
                line.PackageId,
                line.FactorToBaseUnit,
                cancellationToken).ConfigureAwait(false);
            validated.AddLine(
                line.LineId,
                line.ProductId,
                line.PackageId,
                line.OrderedPackageQuantity,
                line.FactorToBaseUnit,
                line.UnitCostXof,
                line.DiscountXof,
                line.Notes);
        }

        PurchaseOrderLineRecord[] existing = await dbContext.PurchaseOrderLines
            .Where(line => line.PurchaseOrderId == purchaseId.Value)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        dbContext.PurchaseOrderLines.RemoveRange(existing);
        dbContext.PurchaseOrderLines.AddRange(
            validated.Lines.Select(line => MapLine(line, validated.Id)));
        record.DocumentNumber = validated.DocumentNumber;
        record.DocumentDate = validated.DocumentDate;
        record.Notes = validated.Notes;
        record.UpdatedAtUtc = auditEvent.OccurredAtUtc.Value;
        dbContext.AuditEvents.Add(InventoryPersistenceMapper.MapAudit(auditEvent));
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await MapDetailsAsync(
            dbContext,
            purchaseId,
            includeCosts: true,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("A compra actualizada não foi encontrada.");
    }

    public async Task CancelAsync(
        PurchaseActorContext context,
        EntityId purchaseId,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        ValidateAudit(context, auditEvent.UserId, purchaseId, auditEvent);
        await using var dbContext = new NofarmaDbContext(options);
        PurchaseOrderRecord record = await GetDraftAsync(
            dbContext,
            context.PharmacyId,
            purchaseId,
            cancellationToken).ConfigureAwait(false);
        PurchaseOrder order = PurchaseOrder.Create(
            purchaseId,
            context.PharmacyId,
            new EntityId(record.SupplierId),
            new EntityId(record.CreatedByUserId),
            record.DocumentNumber,
            record.DocumentDate,
            record.Notes,
            UtcInstant.From(record.CreatedAtUtc));
        order.Cancel();
        record.Status = (int)order.Status;
        record.UpdatedAtUtc = auditEvent.OccurredAtUtc.Value;
        dbContext.AuditEvents.Add(InventoryPersistenceMapper.MapAudit(auditEvent));
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<PurchaseReceiptDetails?> GetReceiptResultAsync(
        EntityId pharmacyId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var dbContext = new NofarmaDbContext(options);
        GoodsReceiptRecord? receipt = await dbContext.GoodsReceipts.AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.PharmacyId == pharmacyId.Value &&
                    record.IdempotencyKey == idempotencyKey.Trim(),
                cancellationToken).ConfigureAwait(false);
        return receipt is null
            ? null
            : await MapReceiptAsync(dbContext, receipt, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PurchaseReceiptDetails> ConfirmReceiptAsync(
        PurchaseActorContext context,
        ConfirmPurchaseReceiptCommand command,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        ValidateAudit(context, command.ActorUserId, command.ReceiptId, auditEvent);
        await using var dbContext = new NofarmaDbContext(options);
        await dbContext.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var connection = (SqliteConnection)dbContext.Database.GetDbConnection();
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        dbContext.Database.UseTransaction(transaction);

        GoodsReceiptRecord? repeated = await dbContext.GoodsReceipts.AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.PharmacyId == context.PharmacyId.Value &&
                    record.IdempotencyKey == command.IdempotencyKey,
                cancellationToken).ConfigureAwait(false);
        if (repeated is not null)
        {
            PurchaseReceiptDetails result = await MapReceiptAsync(
                dbContext,
                repeated,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }

        if (command.Lines.Count == 0 ||
            command.Lines.Select(line => line.PurchaseOrderLineId).Distinct().Count() !=
            command.Lines.Count)
        {
            throw new PurchaseValidationException(
                "A recepção exige linhas distintas.");
        }

        PurchaseOrderRecord purchase = await dbContext.PurchaseOrders.SingleOrDefaultAsync(
            record => record.Id == command.PurchaseId.Value &&
                record.PharmacyId == context.PharmacyId.Value,
            cancellationToken).ConfigureAwait(false)
            ?? throw new PurchaseValidationException("A compra indicada não existe.");
        if (purchase.Status is (int)PurchaseOrderStatus.Cancelled or
            (int)PurchaseOrderStatus.Received)
        {
            throw new PurchaseValidationException("O estado da compra não permite recepções.");
        }

        PurchaseOrderLineRecord[] allLines = await dbContext.PurchaseOrderLines
            .Where(line => line.PurchaseOrderId == purchase.Id)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        PurchaseOrder aggregate = RebuildOrder(purchase, allLines, command.DocumentNumber);
        purchase.DocumentNumber = command.DocumentNumber;
        purchase.DocumentDate = command.DocumentDate;
        purchase.Notes = NormalizeOptional(command.Notes) ?? purchase.Notes;
        purchase.UpdatedAtUtc = command.OccurredUtc.Value;
        dbContext.GoodsReceipts.Add(new GoodsReceiptRecord
        {
            Id = command.ReceiptId.Value,
            PharmacyId = context.PharmacyId.Value,
            PurchaseOrderId = purchase.Id,
            SupplierId = purchase.SupplierId,
            DocumentNumber = command.DocumentNumber,
            Notes = NormalizeOptional(command.Notes),
            ReceivedByUserId = command.ActorUserId.Value,
            ReceivedAtUtc = command.OccurredUtc.Value,
            IdempotencyKey = command.IdempotencyKey
        });

        foreach (ConfirmPurchaseReceiptLineCommand received in command.Lines)
        {
            PurchaseOrderLineRecord orderLine = allLines.SingleOrDefault(
                line => line.Id == received.PurchaseOrderLineId.Value)
                ?? throw new PurchaseValidationException(
                    "A linha recebida não pertence à compra.");
            aggregate.Receive(new EntityId(orderLine.Id), received.PackageQuantity);
            ProductRecord product = await dbContext.Products.SingleOrDefaultAsync(
                record => record.Id == orderLine.ProductId &&
                    record.PharmacyId == context.PharmacyId.Value &&
                    record.IsActive,
                cancellationToken).ConfigureAwait(false)
                ?? throw new PurchaseValidationException("O produto recebido não está activo.");
            ValidateReceiptLine(product, received, command.OccurredUtc);
            long quantityBase = checked(received.PackageQuantity * orderLine.FactorToBaseUnit);
            StockLotRecord lot = await ResolveLotAsync(
                dbContext,
                context,
                purchase,
                product,
                received,
                command.OccurredUtc,
                cancellationToken).ConfigureAwait(false);
            var operation = new StockOperation(
                received.MovementId,
                context.PharmacyId,
                new EntityId(product.Id),
                new EntityId(lot.Id),
                quantityBase,
                StockMovementType.PurchaseReceipt,
                null,
                command.ReceiptId,
                command.ActorUserId,
                command.OccurredUtc,
                received.MovementIdempotencyKey);
            StockMovement movement = StockLedger.CreateMovement(
                operation,
                lot.AvailableQuantityBase);
            await ApplyBalanceAsync(
                dbContext,
                lot,
                movement,
                cancellationToken).ConfigureAwait(false);
            dbContext.StockMovements.Add(MapMovement(movement));
            dbContext.GoodsReceiptLines.Add(new GoodsReceiptLineRecord
            {
                Id = received.ReceiptLineId.Value,
                GoodsReceiptId = command.ReceiptId.Value,
                PurchaseOrderLineId = orderLine.Id,
                ProductId = orderLine.ProductId,
                PackageId = orderLine.PackageId,
                StockLotId = lot.Id,
                PackageQuantity = received.PackageQuantity,
                FactorToBaseUnit = orderLine.FactorToBaseUnit,
                QuantityBase = quantityBase,
                UnitCostXof = received.UnitCostXof
            });
            orderLine.ReceivedPackageQuantity = aggregate.Lines
                .Single(line => line.Id.Value == orderLine.Id)
                .ReceivedPackageQuantity;
            product.HasMovements = true;
            product.UpdatedAtUtc = command.OccurredUtc.Value;
        }

        purchase.Status = (int)aggregate.Status;
        dbContext.AuditEvents.Add(InventoryPersistenceMapper.MapAudit(auditEvent));
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new PurchaseReceiptDetails(
            command.ReceiptId,
            command.PurchaseId,
            command.DocumentNumber,
            command.OccurredUtc,
            command.Lines.Sum(line => line.PackageQuantity),
            aggregate.Status);
    }

    private static PurchaseOrder RebuildOrder(
        PurchaseOrderRecord purchase,
        IReadOnlyCollection<PurchaseOrderLineRecord> lines,
        string documentNumber)
    {
        PurchaseOrder order = PurchaseOrder.Create(
            new EntityId(purchase.Id),
            new EntityId(purchase.PharmacyId),
            new EntityId(purchase.SupplierId),
            new EntityId(purchase.CreatedByUserId),
            documentNumber,
            purchase.DocumentDate,
            purchase.Notes,
            UtcInstant.From(purchase.CreatedAtUtc));
        foreach (PurchaseOrderLineRecord line in lines)
        {
            order.AddLine(
                new EntityId(line.Id),
                new EntityId(line.ProductId),
                new EntityId(line.PackageId),
                line.OrderedPackageQuantity,
                line.FactorToBaseUnit,
                line.UnitCostXof,
                line.DiscountXof,
                line.Notes);
        }

        foreach (PurchaseOrderLineRecord line in lines.Where(line => line.ReceivedPackageQuantity > 0))
        {
            order.Receive(new EntityId(line.Id), line.ReceivedPackageQuantity);
        }

        return order;
    }

    private static async Task<StockLotRecord> ResolveLotAsync(
        NofarmaDbContext dbContext,
        PurchaseActorContext context,
        PurchaseOrderRecord purchase,
        ProductRecord product,
        ConfirmPurchaseReceiptLineCommand received,
        UtcInstant occurredUtc,
        CancellationToken cancellationToken)
    {
        string number = string.IsNullOrWhiteSpace(received.LotNumber)
            ? "SEM-LOTE"
            : received.LotNumber.Trim().ToUpperInvariant();
        StockLotRecord? existing = dbContext.StockLots.Local.SingleOrDefault(
            record => record.ProductId == product.Id && record.NormalizedNumber == number)
            ?? await dbContext.StockLots.SingleOrDefaultAsync(
                record => record.ProductId == product.Id && record.NormalizedNumber == number,
                cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.ExpiryYear != received.Expiry?.Year ||
                existing.ExpiryMonth != received.Expiry?.Month ||
                existing.ExpiryDay != received.Expiry?.Day)
            {
                throw new PurchaseValidationException(
                    "O lote já existe com uma validade diferente.");
            }

            return existing;
        }

        StockLot lot = StockLot.Create(
            received.ProposedLotId,
            new EntityId(product.Id),
            number,
            received.Expiry,
            new EntityId(purchase.SupplierId),
            Money.Xof(received.UnitCostXof),
            occurredUtc);
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
            FirstEntryAtUtc = occurredUtc.Value,
            RowVersion = 0
        };
        dbContext.StockLots.Add(record);
        return record;
    }

    private static async Task ApplyBalanceAsync(
        NofarmaDbContext dbContext,
        StockLotRecord lot,
        StockMovement movement,
        CancellationToken cancellationToken)
    {
        if (dbContext.Entry(lot).State == EntityState.Added)
        {
            lot.AvailableQuantityBase = checked(lot.AvailableQuantityBase + movement.QuantityBase);
            lot.QuantityReceivedBase = checked(lot.QuantityReceivedBase + movement.QuantityBase);
            lot.RowVersion = checked(lot.RowVersion + 1);
            return;
        }

        long expectedBalance = lot.AvailableQuantityBase;
        long quantity = movement.QuantityBase;
        int affected = await dbContext.StockLots
            .Where(record => record.Id == lot.Id &&
                record.AvailableQuantityBase == expectedBalance)
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
        if (affected != 1)
        {
            throw new PurchaseValidationException("O saldo do lote mudou durante a recepção.");
        }

        await dbContext.Entry(lot).ReloadAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateReceiptLine(
        ProductRecord product,
        ConfirmPurchaseReceiptLineCommand line,
        UtcInstant occurredUtc)
    {
        if (line.PackageQuantity <= 0 || line.UnitCostXof <= 0)
        {
            throw new PurchaseValidationException(
                "A quantidade e o custo recebidos devem ser positivos.");
        }

        if (product.RequiresLot && string.IsNullOrWhiteSpace(line.LotNumber))
        {
            throw new PurchaseValidationException("O número do lote é obrigatório.");
        }

        if (product.RequiresExpiry && line.Expiry is null)
        {
            throw new PurchaseValidationException("A validade do lote é obrigatória.");
        }

        DateOnly businessDate = DateOnly.FromDateTime(occurredUtc.Value.UtcDateTime);
        if (line.Expiry is { } expiry && businessDate >= expiry.BlockingDate)
        {
            throw new PurchaseValidationException("Não é permitido receber um lote expirado.");
        }
    }

    private static async Task ValidateCatalogLineAsync(
        NofarmaDbContext context,
        EntityId pharmacyId,
        EntityId productId,
        EntityId packageId,
        long factorToBaseUnit,
        CancellationToken cancellationToken)
    {
        bool valid = await (
            from product in context.Products.AsNoTracking()
            join package in context.ProductPackages.AsNoTracking()
                on product.Id equals package.ProductId
            where product.Id == productId.Value &&
                product.PharmacyId == pharmacyId.Value &&
                product.IsActive &&
                package.Id == packageId.Value &&
                package.IsActive &&
                package.FactorToBaseUnit == factorToBaseUnit
            select product.Id).AnyAsync(cancellationToken).ConfigureAwait(false);
        if (!valid)
        {
            throw new PurchaseValidationException(
                "O produto, a embalagem ou o factor documental não é válido.");
        }
    }

    private static async Task<PurchaseOrderRecord> GetDraftAsync(
        NofarmaDbContext context,
        EntityId pharmacyId,
        EntityId purchaseId,
        CancellationToken cancellationToken)
    {
        PurchaseOrderRecord record = await context.PurchaseOrders.SingleOrDefaultAsync(
            candidate => candidate.Id == purchaseId.Value &&
                candidate.PharmacyId == pharmacyId.Value,
            cancellationToken).ConfigureAwait(false)
            ?? throw new PurchaseValidationException("A compra indicada não existe.");
        if (record.Status != (int)PurchaseOrderStatus.Draft)
        {
            throw new PurchaseValidationException(
                "Apenas uma compra em rascunho pode ser alterada.");
        }

        return record;
    }

    private static async Task<PurchaseDetails?> MapDetailsAsync(
        NofarmaDbContext context,
        EntityId purchaseId,
        bool includeCosts,
        CancellationToken cancellationToken)
    {
        PurchaseOrderRecord? purchase = await context.PurchaseOrders.AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.Id == purchaseId.Value,
                cancellationToken).ConfigureAwait(false);
        if (purchase is null)
        {
            return null;
        }

        string supplierName = await context.Suppliers.AsNoTracking()
            .Where(record => record.Id == purchase.SupplierId)
            .Select(record => record.Name)
            .SingleAsync(cancellationToken).ConfigureAwait(false);
        PurchaseOrderLineRecord[] lines = await context.PurchaseOrderLines.AsNoTracking()
            .Where(record => record.PurchaseOrderId == purchase.Id)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        Guid[] productIds = lines.Select(line => line.ProductId).Distinct().ToArray();
        Guid[] packageIds = lines.Select(line => line.PackageId).Distinct().ToArray();
        Dictionary<Guid, string> productNames = await context.Products.AsNoTracking()
            .Where(record => productIds.Contains(record.Id))
            .ToDictionaryAsync(record => record.Id, record => record.Name, cancellationToken)
            .ConfigureAwait(false);
        Dictionary<Guid, string> packageNames = await context.ProductPackages.AsNoTracking()
            .Where(record => packageIds.Contains(record.Id))
            .ToDictionaryAsync(record => record.Id, record => record.Name, cancellationToken)
            .ConfigureAwait(false);
        GoodsReceiptRecord[] receipts = await context.GoodsReceipts.AsNoTracking()
            .Where(record => record.PurchaseOrderId == purchase.Id)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        Guid[] receiptIds = receipts.Select(receipt => receipt.Id).ToArray();
        GoodsReceiptLineRecord[] receiptLines = await context.GoodsReceiptLines.AsNoTracking()
            .Where(record => receiptIds.Contains(record.GoodsReceiptId))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        PurchaseLineDetails[] details = lines.OrderBy(line => productNames[line.ProductId])
            .Select(line => new PurchaseLineDetails(
                new EntityId(line.Id),
                new EntityId(line.ProductId),
                new EntityId(line.PackageId),
                productNames[line.ProductId],
                packageNames[line.PackageId],
                line.OrderedPackageQuantity,
                line.ReceivedPackageQuantity,
                line.FactorToBaseUnit,
                includeCosts ? line.UnitCostXof : null,
                includeCosts ? line.DiscountXof : null,
                includeCosts
                    ? checked(line.OrderedPackageQuantity * line.UnitCostXof - line.DiscountXof)
                    : null)).ToArray();
        PurchaseReceiptSummary[] history = receipts
            .OrderByDescending(receipt => receipt.ReceivedAtUtc)
            .Select(receipt => new PurchaseReceiptSummary(
                new EntityId(receipt.Id),
                receipt.DocumentNumber ?? string.Empty,
                UtcInstant.From(receipt.ReceivedAtUtc),
                receiptLines.Where(line => line.GoodsReceiptId == receipt.Id)
                    .Sum(line => line.PackageQuantity)))
            .ToArray();
        return new PurchaseDetails(
            new EntityId(purchase.Id),
            new EntityId(purchase.SupplierId),
            supplierName,
            (PurchaseOrderStatus)purchase.Status,
            purchase.DocumentNumber,
            purchase.DocumentDate,
            purchase.Notes,
            includeCosts ? details.Sum(line => line.TotalXof ?? 0) : null,
            details,
            history);
    }

    private static async Task<PurchaseReceiptDetails> MapReceiptAsync(
        NofarmaDbContext context,
        GoodsReceiptRecord receipt,
        CancellationToken cancellationToken)
    {
        PurchaseOrderStatus status = (PurchaseOrderStatus)await context.PurchaseOrders.AsNoTracking()
            .Where(record => record.Id == receipt.PurchaseOrderId)
            .Select(record => record.Status)
            .SingleAsync(cancellationToken).ConfigureAwait(false);
        long quantity = await context.GoodsReceiptLines.AsNoTracking()
            .Where(record => record.GoodsReceiptId == receipt.Id)
            .SumAsync(record => record.PackageQuantity, cancellationToken).ConfigureAwait(false);
        return new PurchaseReceiptDetails(
            new EntityId(receipt.Id),
            new EntityId(receipt.PurchaseOrderId!.Value),
            receipt.DocumentNumber ?? string.Empty,
            UtcInstant.From(receipt.ReceivedAtUtc),
            quantity,
            status);
    }

    private static PurchaseOrderLineRecord MapLine(
        PurchaseOrderLine line,
        EntityId purchaseOrderId) => new()
        {
            Id = line.Id.Value,
            PurchaseOrderId = purchaseOrderId.Value,
            ProductId = line.ProductId.Value,
            PackageId = line.PackageId.Value,
            OrderedPackageQuantity = line.OrderedPackageQuantity,
            ReceivedPackageQuantity = line.ReceivedPackageQuantity,
            FactorToBaseUnit = line.FactorToBaseUnit,
            UnitCostXof = line.UnitCostXof,
            DiscountXof = line.DiscountXof,
            Notes = line.Notes
        };

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

    private static void ValidateAudit(
        PurchaseActorContext context,
        EntityId? actorUserId,
        EntityId objectId,
        AuditEvent audit)
    {
        if (actorUserId is null ||
            actorUserId != context.ActorUserId ||
            audit.PharmacyId != context.PharmacyId ||
            audit.DeviceId != context.DeviceId ||
            audit.UserId != actorUserId ||
            audit.ObjectId != objectId.Value.ToString("D"))
        {
            throw new PurchaseValidationException("O contexto de auditoria da compra não é válido.");
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
