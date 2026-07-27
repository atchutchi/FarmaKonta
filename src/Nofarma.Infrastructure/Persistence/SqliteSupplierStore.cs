using Microsoft.EntityFrameworkCore;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Supply;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;
using Nofarma.Domain.Supply;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.Infrastructure.Persistence;

public sealed class SqliteSupplierStore(
    DbContextOptions<NofarmaDbContext> options) : ISupplierStore
{
    public async Task<SupplierActorContext?> GetContextAsync(
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
        return new SupplierActorContext(new EntityId(actor.PharmacyId), new EntityId(deviceId));
    }

    public async Task<SupplierDetails> CreateAsync(
        SupplierActorContext context,
        EntityId supplierId,
        CreateSupplierRequest request,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        Supplier supplier = Supplier.Create(
            supplierId,
            context.PharmacyId,
            request.Name,
            request.TaxIdentifier,
            request.Phone,
            request.Email,
            request.Address,
            request.Notes);
        DateTimeOffset now = auditEvent.OccurredAtUtc.Value;
        await using var dbContext = new NofarmaDbContext(options);
        dbContext.Suppliers.Add(new SupplierRecord
        {
            Id = supplier.Id.Value,
            PharmacyId = supplier.PharmacyId.Value,
            Name = supplier.Name,
            NormalizedName = supplier.Name.ToUpperInvariant(),
            TaxIdentifier = supplier.TaxIdentifier,
            Phone = supplier.Phone,
            Email = supplier.Email,
            Address = supplier.Address,
            Notes = supplier.Notes,
            IsActive = supplier.IsActive,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        dbContext.AuditEvents.Add(InventoryPersistenceMapper.MapAudit(auditEvent));
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return MapDetails(supplier);
    }

    public async Task<IReadOnlyList<SupplierSummary>> SearchAsync(
        EntityId pharmacyId,
        string query,
        CancellationToken cancellationToken)
    {
        await using var context = new NofarmaDbContext(options);
        string normalized = query.Trim().ToUpperInvariant();
        IQueryable<SupplierRecord> source = context.Suppliers.AsNoTracking()
            .Where(record => record.PharmacyId == pharmacyId.Value);
        if (normalized.Length > 0)
        {
            string pattern = $"%{normalized}%";
            source = source.Where(record =>
                EF.Functions.Like(record.NormalizedName, pattern) ||
                (record.TaxIdentifier != null && EF.Functions.Like(record.TaxIdentifier, pattern)) ||
                (record.Phone != null && EF.Functions.Like(record.Phone, pattern)));
        }

        return await source.OrderBy(record => record.Name)
            .Select(record => new SupplierSummary(
                new EntityId(record.Id),
                record.Name,
                record.Phone,
                record.IsActive))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeactivateAsync(
        SupplierActorContext context,
        EntityId supplierId,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        await using var dbContext = new NofarmaDbContext(options);
        SupplierRecord supplier = await dbContext.Suppliers.SingleOrDefaultAsync(
            record => record.PharmacyId == context.PharmacyId.Value &&
                record.Id == supplierId.Value,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("O fornecedor indicado não existe.");
        supplier.IsActive = false;
        supplier.UpdatedAtUtc = auditEvent.OccurredAtUtc.Value;
        dbContext.AuditEvents.Add(InventoryPersistenceMapper.MapAudit(auditEvent));
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<SupplierDetails> UpdateAsync(
        SupplierActorContext context,
        EntityId supplierId,
        UpdateSupplierRequest request,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        await using var dbContext = new NofarmaDbContext(options);
        SupplierRecord record = await dbContext.Suppliers.SingleOrDefaultAsync(
            candidate => candidate.PharmacyId == context.PharmacyId.Value &&
                candidate.Id == supplierId.Value,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("O fornecedor indicado não existe.");
        Supplier supplier = Supplier.Create(
            new EntityId(record.Id),
            new EntityId(record.PharmacyId),
            record.Name,
            record.TaxIdentifier,
            record.Phone,
            record.Email,
            record.Address,
            record.Notes);
        supplier.Update(
            request.Name,
            request.TaxIdentifier,
            request.Phone,
            request.Email,
            request.Address,
            request.Notes);
        record.Name = supplier.Name;
        record.NormalizedName = supplier.Name.ToUpperInvariant();
        record.TaxIdentifier = supplier.TaxIdentifier;
        record.Phone = supplier.Phone;
        record.Email = supplier.Email;
        record.Address = supplier.Address;
        record.Notes = supplier.Notes;
        record.UpdatedAtUtc = auditEvent.OccurredAtUtc.Value;
        dbContext.AuditEvents.Add(InventoryPersistenceMapper.MapAudit(auditEvent));
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new SupplierDetails(
            supplier.Id,
            supplier.Name,
            supplier.TaxIdentifier,
            supplier.Phone,
            supplier.Email,
            supplier.Address,
            supplier.Notes,
            record.IsActive);
    }

    private static SupplierDetails MapDetails(Supplier supplier) => new(
        supplier.Id,
        supplier.Name,
        supplier.TaxIdentifier,
        supplier.Phone,
        supplier.Email,
        supplier.Address,
        supplier.Notes,
        supplier.IsActive);
}
