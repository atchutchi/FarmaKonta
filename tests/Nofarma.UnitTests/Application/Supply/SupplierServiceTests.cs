using Nofarma.Application.Abstractions;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Application.Identity.Authorization;
using Nofarma.Application.Supply;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;

namespace Nofarma.UnitTests.Application.Supply;

public sealed class SupplierServiceTests
{
    [Fact]
    public async Task StockManagerCreatesSupplier()
    {
        var store = new SupplierStore();
        SupplierService service = CreateService(store);

        SupplierDetails result = await service.CreateAsync(
            Session(UserRole.StockManager),
            new CreateSupplierRequest(
                "Distribuidora Bissau",
                "500123456",
                "955000000",
                "compras@example.test",
                "Bissau",
                null),
            CancellationToken.None);

        Assert.Equal("Distribuidora Bissau", result.Name);
        Assert.Equal("supplier.created", store.LastAudit?.Action);
    }

    [Fact]
    public async Task CashierCannotSearchSuppliers()
    {
        SupplierService service = CreateService(new SupplierStore());

        await Assert.ThrowsAsync<AuthorizationException>(() => service.SearchAsync(
            Session(UserRole.Cashier),
            string.Empty,
            CancellationToken.None));
    }

    [Fact]
    public async Task StockManagerUpdatesSupplierWithAudit()
    {
        var store = new SupplierStore();
        SupplierService service = CreateService(store);

        SupplierDetails result = await service.UpdateAsync(
            Session(UserRole.StockManager),
            EntityId.New(),
            new UpdateSupplierRequest(
                "Novo nome",
                null,
                "966000000",
                null,
                "Bissau",
                null),
            CancellationToken.None);

        Assert.Equal("Novo nome", result.Name);
        Assert.Equal("supplier.updated", store.LastAudit?.Action);
    }

    private static SupplierService CreateService(ISupplierStore store)
    {
        var clock = new FixedClock();
        return new SupplierService(store, new AuthorizationService(clock), clock);
    }

    private static LocalSession Session(UserRole role) => new(
        EntityId.New(),
        EntityId.New(),
        role,
        FixedClock.Now,
        FixedClock.Now,
        null);

    private sealed class SupplierStore : ISupplierStore
    {
        public AuditEvent? LastAudit { get; private set; }

        public Task<SupplierActorContext?> GetContextAsync(
            EntityId actorUserId,
            CancellationToken cancellationToken) => Task.FromResult<SupplierActorContext?>(new(
                EntityId.New(),
                EntityId.New()));

        public Task<SupplierDetails> CreateAsync(
            SupplierActorContext context,
            EntityId supplierId,
            CreateSupplierRequest request,
            AuditEvent auditEvent,
            CancellationToken cancellationToken)
        {
            LastAudit = auditEvent;
            return Task.FromResult(new SupplierDetails(
                supplierId,
                request.Name,
                request.TaxIdentifier,
                request.Phone,
                request.Email,
                request.Address,
                request.Notes,
                true));
        }

        public Task<IReadOnlyList<SupplierSummary>> SearchAsync(
            EntityId pharmacyId,
            string query,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SupplierSummary>>([]);

        public Task DeactivateAsync(
            SupplierActorContext context,
            EntityId supplierId,
            AuditEvent auditEvent,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<SupplierDetails> UpdateAsync(
            SupplierActorContext context,
            EntityId supplierId,
            UpdateSupplierRequest request,
            AuditEvent auditEvent,
            CancellationToken cancellationToken)
        {
            LastAudit = auditEvent;
            return Task.FromResult(new SupplierDetails(
                supplierId,
                request.Name,
                request.TaxIdentifier,
                request.Phone,
                request.Email,
                request.Address,
                request.Notes,
                true));
        }
    }

    private sealed class FixedClock : IUtcClock
    {
        public static readonly UtcInstant Now = UtcInstant.From(
            new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero));

        public UtcInstant GetCurrentInstant() => Now;
    }
}
