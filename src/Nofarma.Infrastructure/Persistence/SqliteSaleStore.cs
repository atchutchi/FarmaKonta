using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Sales;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;
using Nofarma.Domain.Inventory;
using Nofarma.Domain.Sales;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.Infrastructure.Persistence;

public sealed class SqliteSaleStore(
    DbContextOptions<NofarmaDbContext> options) : ISaleStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<SaleActorContext?> GetActorContextAsync(
        EntityId userId,
        CancellationToken cancellationToken)
    {
        await using var db = new NofarmaDbContext(options);
        var result = await (
            from user in db.LocalUsers.AsNoTracking()
            join pharmacy in db.Pharmacies.AsNoTracking()
                on user.PharmacyId equals pharmacy.Id
            join installation in db.Installations.AsNoTracking()
                on user.PharmacyId equals installation.PharmacyId
            where user.Id == userId.Value &&
                user.Status == (int)UserStatus.Active &&
                (installation.Status == (int)InstallationStatus.ReadyForActivation ||
                 installation.Status == (int)InstallationStatus.Active)
            select new
            {
                PharmacyId = pharmacy.Id,
                PharmacyName = pharmacy.Name,
                pharmacy.TimeZoneId,
                installation.DeviceId,
                ActorUserId = user.Id,
                ActorDisplayName = user.DisplayName
            }).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return result is null
            ? null
            : new SaleActorContext(
                new EntityId(result.PharmacyId),
                result.PharmacyName,
                result.TimeZoneId,
                new EntityId(result.DeviceId),
                new EntityId(result.ActorUserId),
                result.ActorDisplayName);
    }

    public async Task<IReadOnlyList<SaleProductResult>> SearchProductsAsync(
        EntityId pharmacyId,
        string query,
        DateOnly businessDate,
        int limit,
        CancellationToken cancellationToken)
    {
        int safeLimit = Math.Clamp(limit, 1, 50);
        string normalized = query.Trim().ToUpperInvariant();
        string searchPattern = $"%{query.Trim()}%";
        await using var db = new NofarmaDbContext(options);
        var candidates = await (
            from product in db.Products.AsNoTracking()
            join package in db.ProductPackages.AsNoTracking()
                on product.Id equals package.ProductId
            where product.PharmacyId == pharmacyId.Value &&
                product.IsActive &&
                package.IsActive &&
                (product.NormalizedCode.Contains(normalized) ||
                 EF.Functions.Like(product.Name, searchPattern) ||
                 db.ProductBarcodes.Any(barcode =>
                     barcode.PharmacyId == pharmacyId.Value &&
                     barcode.ProductId == product.Id &&
                     barcode.PackageId == package.Id &&
                     barcode.Value.Contains(normalized)))
            orderby product.Name, package.Name
            select new
            {
                Product = product,
                Package = package
            }).Take(safeLimit * 2).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<SaleProductResult>();
        foreach (var candidate in candidates)
        {
            StockLotRecord[] lots = await db.StockLots.AsNoTracking()
                .Where(lot => lot.PharmacyId == pharmacyId.Value &&
                    lot.ProductId == candidate.Product.Id &&
                    lot.AvailableQuantityBase > 0)
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);
            StockLotRecord[] eligible = lots
                .Where(lot => !IsBlocked(lot, businessDate))
                .OrderBy(lot => GetBlockingDate(lot) ?? DateOnly.MaxValue)
                .ThenBy(lot => lot.FirstEntryAtUtc)
                .ThenBy(lot => lot.Id)
                .ToArray();
            long available = eligible.Sum(lot => lot.AvailableQuantityBase);
            if (available == 0)
            {
                continue;
            }
            StockLotRecord first = eligible[0];
            results.Add(new SaleProductResult(
                new EntityId(candidate.Product.Id),
                new EntityId(candidate.Package.Id),
                candidate.Product.Code,
                candidate.Product.Name,
                candidate.Package.Name,
                candidate.Package.FactorToBaseUnit,
                available,
                checked(candidate.Product.SalePriceXof * candidate.Package.FactorToBaseUnit),
                first.Number,
                GetExpiryDisplayDate(first),
                candidate.Product.RequiresPrescription));
            if (results.Count == safeLimit)
            {
                break;
            }
        }
        return results.AsReadOnly();
    }

    public async Task<StoredCashShift?> GetOpenShiftAsync(
        EntityId pharmacyId,
        EntityId deviceId,
        CancellationToken cancellationToken)
    {
        await using var db = new NofarmaDbContext(options);
        CashShiftRecord? record = await db.CashShifts.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.PharmacyId == pharmacyId.Value &&
                    item.DeviceId == deviceId.Value &&
                    item.Status == (int)CashShiftStatus.Open,
                cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }
        CashMovementRecord[] movements = await db.CashMovements.AsNoTracking()
            .Where(item => item.CashShiftId == record.Id)
            .OrderBy(item => item.Sequence)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        CashShift shift = CashShift.Open(
            new EntityId(record.Id),
            new EntityId(record.PharmacyId),
            new EntityId(record.DeviceId),
            new EntityId(record.UserId),
            Money.Xof(record.OpeningCashXof),
            UtcInstant.From(record.OpenedAtUtc));
        foreach (CashMovementRecord movement in movements)
        {
            shift.RecordMovement(
                new EntityId(movement.Id),
                (CashMovementType)movement.Type,
                Money.Xof(movement.AmountXof),
                movement.SourceSaleId is { } saleId ? new EntityId(saleId) : null,
                movement.Reason,
                UtcInstant.From(movement.OccurredAtUtc));
        }
        return new StoredCashShift(shift, record.RowVersion);
    }

    public async Task<SaleProductSnapshot?> GetProductSnapshotAsync(
        EntityId pharmacyId,
        EntityId productId,
        EntityId packageId,
        DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        await using var db = new NofarmaDbContext(options);
        var candidate = await (
            from product in db.Products.AsNoTracking()
            join package in db.ProductPackages.AsNoTracking()
                on product.Id equals package.ProductId
            where product.PharmacyId == pharmacyId.Value &&
                product.Id == productId.Value &&
                package.Id == packageId.Value &&
                product.IsActive &&
                package.IsActive
            select new
            {
                Product = product,
                Package = package
            }).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (candidate is null)
        {
            return null;
        }
        StockLotRecord[] records = await db.StockLots.AsNoTracking()
            .Where(lot => lot.PharmacyId == pharmacyId.Value &&
                lot.ProductId == productId.Value &&
                lot.AvailableQuantityBase > 0)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        SaleLotSnapshot[] lots = records
            .Where(lot => !IsBlocked(lot, businessDate))
            .Select(lot => new SaleLotSnapshot(
                MapLot(lot),
                lot.AvailableQuantityBase,
                lot.RowVersion))
            .ToArray();
        return new SaleProductSnapshot(
            productId,
            packageId,
            candidate.Product.Code,
            candidate.Product.Name,
            candidate.Package.Name,
            candidate.Package.FactorToBaseUnit,
            checked(candidate.Product.SalePriceXof * candidate.Package.FactorToBaseUnit),
            candidate.Product.RequiresPrescription,
            lots);
    }

    public async Task<SaleCommandResult?> GetCommandResultAsync(
        EntityId pharmacyId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var db = new NofarmaDbContext(options);
        SaleCommandRecord? command = await db.SaleCommands.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.PharmacyId == pharmacyId.Value &&
                    item.IdempotencyKey == idempotencyKey,
                cancellationToken).ConfigureAwait(false);
        return command is null ? null : DeserializeCommand(command);
    }

    public async Task<long> GetNextSaleSequenceAsync(
        EntityId pharmacyId,
        DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        await using var db = new NofarmaDbContext(options);
        long? current = await db.Sales.AsNoTracking()
            .Where(item => item.PharmacyId == pharmacyId.Value &&
                item.BusinessDate == businessDate)
            .MaxAsync(item => (long?)item.DailySequence, cancellationToken)
            .ConfigureAwait(false);
        return checked((current ?? 0) + 1);
    }

    public async Task<SaleSummary> CompleteAsync(
        SaleCompletion completion,
        CancellationToken cancellationToken)
    {
        ValidateCompletion(completion);
        await using var db = new NofarmaDbContext(options);
        await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        db.Database.UseTransaction(transaction);
        await EnsurePersistedContextAsync(db, completion, cancellationToken).ConfigureAwait(false);

        SaleSummary? duplicate = await GetDuplicateAsync(
            db,
            completion.Context.PharmacyId,
            completion.Command,
            cancellationToken).ConfigureAwait(false);
        if (duplicate is not null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return duplicate;
        }
        DateOnly businessDate = ParseBusinessDate(completion.Sale.Number);
        long dailySequence = ParseSequence(completion.Sale.Number);
        bool numberExists = await db.Sales.AsNoTracking().AnyAsync(
            item => item.PharmacyId == completion.Context.PharmacyId.Value &&
                (item.Number == completion.Sale.Number.Value ||
                 item.BusinessDate == businessDate &&
                 item.DailySequence == dailySequence),
            cancellationToken).ConfigureAwait(false);
        if (numberExists)
        {
            throw new SaleConcurrencyException(SaleConcurrencyReason.Sequence);
        }

        await UpdateLotsAsync(db, completion, cancellationToken).ConfigureAwait(false);
        await UpdateCashShiftAsync(db, completion, cancellationToken).ConfigureAwait(false);
        AddOperationalRecords(db, completion);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraint(exception))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            SaleCommandResult? concurrent = await GetCommandResultAsync(
                completion.Context.PharmacyId,
                completion.Command.IdempotencyKey,
                cancellationToken).ConfigureAwait(false);
            if (concurrent is not null && string.Equals(
                    concurrent.RequestFingerprint,
                    completion.Command.RequestFingerprint,
                    StringComparison.Ordinal))
            {
                return concurrent.Result;
            }
            throw new SaleConcurrencyException(SaleConcurrencyReason.Sequence);
        }
        return MapSummary(completion);
    }

    public Task<IReadOnlyList<SuspendedSaleSummary>> GetSuspendedAsync(
        EntityId pharmacyId,
        EntityId deviceId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SuspendedSaleSummary>>([]);

    public Task<SuspendedSaleDetails?> GetSuspendedDetailsAsync(
        EntityId pharmacyId,
        EntityId deviceId,
        EntityId suspendedSaleId,
        CancellationToken cancellationToken) =>
        Task.FromResult<SuspendedSaleDetails?>(null);

    public Task<SuspendedSaleSummary> SaveSuspendedAsync(
        SuspendedSale sale,
        Domain.Auditing.AuditEvent auditEvent,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("As vendas suspensas entram na tarefa seguinte.");

    public Task<bool> DeleteSuspendedAsync(
        EntityId pharmacyId,
        EntityId deviceId,
        EntityId suspendedSaleId,
        Domain.Auditing.AuditEvent auditEvent,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("As vendas suspensas entram na tarefa seguinte.");

    public async Task<ReceiptDetails?> GetReceiptAsync(
        EntityId pharmacyId,
        EntityId receiptId,
        CancellationToken cancellationToken)
    {
        await using var db = new NofarmaDbContext(options);
        ReceiptRecord? record = await db.Receipts.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.PharmacyId == pharmacyId.Value &&
                    item.Id == receiptId.Value,
                cancellationToken).ConfigureAwait(false);
        return record is null ? null : DeserializeReceipt(record);
    }

    private static async Task UpdateLotsAsync(
        NofarmaDbContext db,
        SaleCompletion completion,
        CancellationToken cancellationToken)
    {
        foreach (IGrouping<EntityId, SaleStockAllocation> group in completion.Allocations
                     .GroupBy(item => item.LotId))
        {
            SaleStockAllocation[] chain = group.ToArray();
            for (int index = 1; index < chain.Length; index++)
            {
                if (chain[index].PreviousLotBalance != chain[index - 1].ResultingLotBalance ||
                    chain[index].ExpectedLotVersion != chain[0].ExpectedLotVersion)
                {
                    throw new SalesValidationException(
                        "A cadeia de alocação do lote não é válida.");
                }
            }
            SaleStockAllocation first = chain[0];
            SaleStockAllocation last = chain[^1];
            int affected = await db.StockLots
                .Where(record => record.Id == group.Key.Value &&
                    record.PharmacyId == completion.Context.PharmacyId.Value &&
                    record.ProductId == first.ProductId.Value &&
                    record.RowVersion == first.ExpectedLotVersion &&
                    record.AvailableQuantityBase == first.PreviousLotBalance)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            record => record.AvailableQuantityBase,
                            last.ResultingLotBalance)
                        .SetProperty(record => record.RowVersion, record => record.RowVersion + 1),
                    cancellationToken).ConfigureAwait(false);
            if (affected != 1)
            {
                throw new SaleConcurrencyException(SaleConcurrencyReason.Stock);
            }
        }
    }

    private static async Task UpdateCashShiftAsync(
        NofarmaDbContext db,
        SaleCompletion completion,
        CancellationToken cancellationToken)
    {
        CashShift shift = completion.Shift.Shift;
        if (completion.CashMovement is null)
        {
            bool current = await db.CashShifts.AsNoTracking().AnyAsync(
                record => record.Id == shift.Id.Value &&
                    record.PharmacyId == completion.Context.PharmacyId.Value &&
                    record.DeviceId == completion.Context.DeviceId.Value &&
                    record.UserId == completion.Context.ActorUserId.Value &&
                    record.Status == (int)CashShiftStatus.Open &&
                    record.RowVersion == completion.ExpectedCashShiftVersion,
                cancellationToken).ConfigureAwait(false);
            if (!current)
            {
                throw new SaleConcurrencyException(SaleConcurrencyReason.CashShift);
            }
            return;
        }
        int affected = await db.CashShifts
            .Where(record => record.Id == shift.Id.Value &&
                record.PharmacyId == completion.Context.PharmacyId.Value &&
                record.DeviceId == completion.Context.DeviceId.Value &&
                record.UserId == completion.Context.ActorUserId.Value &&
                record.Status == (int)CashShiftStatus.Open &&
                record.RowVersion == completion.ExpectedCashShiftVersion)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(record => record.ExpectedCashXof, shift.ExpectedCash.Amount)
                    .SetProperty(record => record.RowVersion, record => record.RowVersion + 1),
                cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            throw new SaleConcurrencyException(SaleConcurrencyReason.CashShift);
        }
    }

    private static void AddOperationalRecords(
        NofarmaDbContext db,
        SaleCompletion completion)
    {
        Sale sale = completion.Sale;
        db.Sales.Add(new SaleRecord
        {
            Id = sale.Id.Value,
            PharmacyId = sale.PharmacyId.Value,
            DeviceId = sale.DeviceId.Value,
            UserId = sale.UserId.Value,
            CashShiftId = sale.CashShiftId.Value,
            Number = sale.Number.Value,
            BusinessDate = ParseBusinessDate(sale.Number),
            DailySequence = ParseSequence(sale.Number),
            Status = (int)sale.Status,
            GrossSubtotalXof = sale.GrossSubtotal.Amount,
            LineDiscountXof = sale.LineDiscountTotal.Amount,
            TotalDiscountXof = sale.TotalDiscount.Amount,
            TotalDiscountAuthorizedByUserId = sale.TotalDiscountAuthorizedByUserId?.Value,
            TotalXof = sale.Total.Amount,
            PaidXof = sale.Paid.Amount,
            ChangeXof = sale.Change.Amount,
            CompletedAtUtc = sale.CompletedAt.Value
        });
        for (int index = 0; index < sale.Lines.Count; index++)
        {
            SaleLine line = sale.Lines[index];
            db.SaleLines.Add(new SaleLineRecord
            {
                Id = line.Id.Value,
                SaleId = sale.Id.Value,
                Sequence = index + 1,
                ProductId = line.ProductId.Value,
                PackageId = line.PackageId.Value,
                Description = line.Description,
                UnitName = line.UnitName,
                PackageFactor = line.PackageFactor,
                QuantityPackages = line.QuantityPackages,
                QuantityBase = line.QuantityBase,
                UnitPriceXof = line.UnitPrice.Amount,
                GrossXof = line.Gross.Amount,
                DiscountXof = line.Discount.Amount,
                NetXof = line.Net.Amount,
                CapturedCostXof = line.CapturedCost.Amount,
                DiscountAuthorizedByUserId = line.DiscountAuthorizedByUserId?.Value
            });
        }
        for (int index = 0; index < sale.Payments.Count; index++)
        {
            SalePayment payment = sale.Payments[index];
            db.SalePayments.Add(new SalePaymentRecord
            {
                Id = payment.Id.Value,
                SaleId = sale.Id.Value,
                Sequence = index + 1,
                Method = (int)payment.Method,
                AmountXof = payment.Amount.Amount,
                Reference = payment.Reference
            });
        }
        foreach (StockMovement movement in completion.StockMovements)
        {
            db.StockMovements.Add(new StockMovementRecord
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
                RequestFingerprint = completion.Command.RequestFingerprint,
                ResultingLotBalance = movement.ResultingLotBalance
            });
        }
        foreach (SaleStockAllocation allocation in completion.Allocations)
        {
            db.SaleStockAllocations.Add(new SaleStockAllocationRecord
            {
                Id = Guid.NewGuid(),
                SaleId = sale.Id.Value,
                SaleLineId = allocation.SaleLineId.Value,
                ProductId = allocation.ProductId.Value,
                StockLotId = allocation.LotId.Value,
                StockMovementId = allocation.StockMovementId.Value,
                QuantityBase = allocation.QuantityBase,
                OriginUnitCostXof = allocation.OriginUnitCostXof,
                PreviousLotBalance = allocation.PreviousLotBalance,
                ResultingLotBalance = allocation.ResultingLotBalance
            });
        }
        if (completion.CashMovement is { } cashMovement)
        {
            db.CashMovements.Add(new CashMovementRecord
            {
                Id = cashMovement.Id.Value,
                PharmacyId = completion.Context.PharmacyId.Value,
                CashShiftId = cashMovement.CashShiftId.Value,
                Sequence = completion.ExpectedCashShiftVersion,
                Type = (int)cashMovement.Type,
                AmountXof = cashMovement.Amount.Amount,
                SourceSaleId = cashMovement.SourceSaleId?.Value,
                Reason = cashMovement.Reason,
                OccurredAtUtc = cashMovement.OccurredAt.Value
            });
        }
        ReceiptDetails receiptDetails = MapReceipt(completion.Receipt);
        db.Receipts.Add(new ReceiptRecord
        {
            Id = completion.Receipt.Id.Value,
            PharmacyId = completion.Receipt.PharmacyId.Value,
            SaleId = completion.Receipt.SaleId.Value,
            Number = completion.Receipt.Number.Value,
            Type = 1,
            ContentJson = JsonSerializer.Serialize(receiptDetails, JsonOptions),
            CreatedAtUtc = completion.Receipt.CreatedAt.Value
        });
        SaleSummary summary = MapSummary(completion);
        db.SaleCommands.Add(new SaleCommandRecord
        {
            Id = Guid.NewGuid(),
            PharmacyId = completion.Context.PharmacyId.Value,
            IdempotencyKey = completion.Command.IdempotencyKey,
            RequestFingerprint = completion.Command.RequestFingerprint,
            SaleId = sale.Id.Value,
            ResultJson = SerializeSummary(summary),
            CreatedAtUtc = sale.CompletedAt.Value
        });
        db.AuditEvents.Add(InventoryPersistenceMapper.MapAudit(completion.Audit));
        db.OutboxEvents.Add(new OutboxEventRecord
        {
            Id = completion.Outbox.Id.Value,
            PharmacyId = completion.Outbox.PharmacyId.Value,
            DeviceId = completion.Outbox.DeviceId.Value,
            EventType = completion.Outbox.EventType,
            AggregateId = completion.Outbox.AggregateId.Value,
            PayloadJson = completion.Outbox.PayloadJson,
            OccurredAtUtc = completion.Outbox.OccurredAtUtc.Value
        });
    }

    private static void ValidateCompletion(SaleCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        long retainedCash = checked(
            completion.Sale.CashReceived.Amount - completion.Sale.Change.Amount);
        bool cashMovementIsValid = retainedCash == 0
            ? completion.CashMovement is null
            : completion.CashMovement is
            {
                Type: CashMovementType.Sale,
                SourceSaleId: { } sourceSaleId
            } movement &&
              sourceSaleId == completion.Sale.Id &&
              movement.Amount.Amount == retainedCash;
        if (completion.Context.PharmacyId != completion.Sale.PharmacyId ||
            completion.Context.DeviceId != completion.Sale.DeviceId ||
            completion.Context.ActorUserId != completion.Sale.UserId ||
            completion.Receipt.SaleId != completion.Sale.Id ||
            completion.Receipt.PharmacyId != completion.Sale.PharmacyId ||
            completion.Command.IdempotencyKey.Length is < 1 or > 160 ||
            completion.Command.RequestFingerprint.Length != 64 ||
            completion.Audit.ObjectId != completion.Sale.Id.Value.ToString("D") ||
            completion.Audit.Action != "sale.completed" ||
            completion.Outbox.AggregateId != completion.Sale.Id ||
            completion.Outbox.PharmacyId != completion.Context.PharmacyId ||
            completion.Outbox.DeviceId != completion.Context.DeviceId ||
            completion.Outbox.EventType != "sale.completed.v1" ||
            completion.ExpectedCashShiftVersion < 1 ||
            !cashMovementIsValid ||
            completion.Allocations.Count != completion.StockMovements.Count ||
            completion.Allocations.Any(allocation =>
                !completion.StockMovements.Any(movement =>
                    movement.Id == allocation.StockMovementId &&
                    movement.LotId == allocation.LotId &&
                    movement.ProductId == allocation.ProductId &&
                    movement.Type == StockMovementType.Sale &&
                    movement.SourceDocumentId == completion.Sale.Id &&
                    movement.QuantityBase == -allocation.QuantityBase &&
                    movement.ResultingLotBalance == allocation.ResultingLotBalance)))
        {
            throw new SalesValidationException("A unidade de trabalho da venda não é válida.");
        }
    }

    private static async Task EnsurePersistedContextAsync(
        NofarmaDbContext db,
        SaleCompletion completion,
        CancellationToken cancellationToken)
    {
        bool valid = await (
            from installation in db.Installations.AsNoTracking()
            join user in db.LocalUsers.AsNoTracking()
                on installation.PharmacyId equals user.PharmacyId
            where installation.PharmacyId == completion.Context.PharmacyId.Value &&
                installation.DeviceId == completion.Context.DeviceId.Value &&
                user.Id == completion.Context.ActorUserId.Value &&
                user.Status == (int)UserStatus.Active &&
                (installation.Status == (int)InstallationStatus.ReadyForActivation ||
                 installation.Status == (int)InstallationStatus.Active)
            select installation.Id).AnyAsync(cancellationToken).ConfigureAwait(false);
        if (!valid)
        {
            throw new SalesValidationException("O contexto local da venda deixou de ser válido.");
        }
    }

    private static async Task<SaleSummary?> GetDuplicateAsync(
        NofarmaDbContext db,
        EntityId pharmacyId,
        SaleCommandEnvelope command,
        CancellationToken cancellationToken)
    {
        SaleCommandRecord? existing = await db.SaleCommands.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.PharmacyId == pharmacyId.Value &&
                    item.IdempotencyKey == command.IdempotencyKey,
                cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return null;
        }
        if (!string.Equals(
            existing.RequestFingerprint,
            command.RequestFingerprint,
            StringComparison.Ordinal))
        {
            throw new SaleConflictException();
        }
        return DeserializeCommand(existing).Result;
    }

    private static SaleSummary MapSummary(SaleCompletion completion) => new(
        completion.Sale.Id,
        completion.Sale.Number.Value,
        completion.Sale.Total.Amount,
        completion.Sale.Paid.Amount,
        completion.Sale.Change.Amount,
        completion.Sale.CompletedAt,
        completion.Receipt.Id);

    private static ReceiptDetails MapReceipt(Receipt receipt) => new(
        receipt.Id,
        receipt.SaleId,
        receipt.Number.Value,
        receipt.PharmacyName,
        receipt.OperatorName,
        receipt.CreatedAt,
        receipt.Lines.Select(line => new ReceiptLineDetails(
            line.Description,
            line.UnitName,
            line.QuantityPackages,
            line.UnitPrice.Amount,
            line.Discount.Amount,
            line.Net.Amount)).ToArray(),
        receipt.Payments.Select(payment => new ReceiptPaymentDetails(
            payment.Method,
            payment.Amount.Amount,
            payment.Reference)).ToArray(),
        receipt.Total.Amount,
        receipt.Change.Amount,
        receipt.DocumentLabel);

    private static string SerializeSummary(SaleSummary summary) => JsonSerializer.Serialize(
        new SaleSummarySnapshot(
            summary.Id.Value,
            summary.Number,
            summary.TotalXof,
            summary.PaidXof,
            summary.ChangeXof,
            summary.CompletedAtUtc.Value,
            summary.ReceiptId.Value),
        JsonOptions);

    private static SaleCommandResult DeserializeCommand(SaleCommandRecord record)
    {
        SaleSummarySnapshot snapshot = JsonSerializer.Deserialize<SaleSummarySnapshot>(
            record.ResultJson,
            JsonOptions) ?? throw new InvalidOperationException(
                "O resultado idempotente da venda não é válido.");
        return new SaleCommandResult(
            record.RequestFingerprint,
            new SaleSummary(
                new EntityId(snapshot.Id),
                snapshot.Number,
                snapshot.TotalXof,
                snapshot.PaidXof,
                snapshot.ChangeXof,
                UtcInstant.From(snapshot.CompletedAtUtc),
                new EntityId(snapshot.ReceiptId)));
    }

    private static ReceiptDetails DeserializeReceipt(ReceiptRecord record) =>
        JsonSerializer.Deserialize<ReceiptDetails>(record.ContentJson, JsonOptions)
        ?? throw new InvalidOperationException("O conteúdo do recibo não é válido.");

    private static StockLot MapLot(StockLotRecord record) => StockLot.Create(
        new EntityId(record.Id),
        new EntityId(record.ProductId),
        record.Number,
        MapExpiry(record),
        record.SupplierId is { } supplierId ? new EntityId(supplierId) : null,
        Money.Xof(record.OriginCostXof),
        UtcInstant.From(record.FirstEntryAtUtc));

    private static ExpiryDate? MapExpiry(StockLotRecord record) =>
        record.ExpiryYear is not { } year || record.ExpiryMonth is not { } month
            ? null
            : record.ExpiryDay is { } day
                ? ExpiryDate.ForDay(year, month, day)
                : ExpiryDate.ForMonth(year, month);

    private static bool IsBlocked(StockLotRecord record, DateOnly businessDate) =>
        GetBlockingDate(record) is { } blockingDate && businessDate >= blockingDate;

    private static DateOnly? GetBlockingDate(StockLotRecord record) =>
        MapExpiry(record)?.BlockingDate;

    private static DateOnly? GetExpiryDisplayDate(StockLotRecord record) =>
        record.ExpiryYear is not { } year || record.ExpiryMonth is not { } month
            ? null
            : record.ExpiryDay is { } day
                ? new DateOnly(year, month, day)
                : new DateOnly(year, month, DateTime.DaysInMonth(year, month));

    private static DateOnly ParseBusinessDate(SaleNumber number) => DateOnly.ParseExact(
        number.Value.AsSpan(2, 8),
        "yyyyMMdd",
        CultureInfo.InvariantCulture);

    private static long ParseSequence(SaleNumber number) => long.Parse(
        number.Value.AsSpan(11, 6),
        CultureInfo.InvariantCulture);

    private static bool IsUniqueConstraint(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteErrorCode: 19 };

    private sealed record SaleSummarySnapshot(
        Guid Id,
        string Number,
        long TotalXof,
        long PaidXof,
        long ChangeXof,
        DateTimeOffset CompletedAtUtc,
        Guid ReceiptId);
}
