using Nofarma.Application.Identity.Authentication;
using Nofarma.Application.Identity.Authorization;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;

namespace Nofarma.UnitTests.Application.Identity;

public sealed class AuthorizationServiceTests
{
    private static readonly UtcInstant Now = UtcInstant.From(
        new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void CashierCannotManageUsers()
    {
        LocalSession session = CreateSession(UserRole.Cashier);
        var service = new AuthorizationService(new FixedClock());

        Assert.Throws<AuthorizationException>(() =>
            service.EnsureAllowed(session, Capability.ManageUsers));
    }

    [Fact]
    public void CashierCanLockOwnSession()
    {
        LocalSession session = CreateSession(UserRole.Cashier);
        var service = new AuthorizationService(new FixedClock());

        service.EnsureAllowed(session, Capability.LockOwnSession);
    }

    [Fact]
    public void ExpiredSessionIsRejectedBeforeRoleEvaluation()
    {
        LocalSession session = CreateSession(
            UserRole.Administrator,
            UtcInstant.From(Now.Value.AddMinutes(-1)));
        var service = new AuthorizationService(new FixedClock());

        Assert.Throws<AuthorizationException>(() =>
            service.EnsureAllowed(session, Capability.ManageUsers));
    }

    private static LocalSession CreateSession(
        UserRole role,
        UtcInstant? expiresAtUtc = null) =>
        new(
            EntityId.New(),
            EntityId.New(),
            role,
            Now,
            Now,
            expiresAtUtc ?? UtcInstant.From(Now.Value.AddMinutes(15)));

    private sealed class FixedClock : Nofarma.Application.Abstractions.IUtcClock
    {
        public UtcInstant GetCurrentInstant() => Now;
    }
}
