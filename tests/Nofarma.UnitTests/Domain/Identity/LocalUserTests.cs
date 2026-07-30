using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;

namespace Nofarma.UnitTests.Domain.Identity;

public sealed class LocalUserTests
{
    [Fact]
    public void CashierUsesPinAndNeverAdministrativePassword()
    {
        LocalUser cashier = LocalUser.CreateCashier(
            EntityId.New(),
            "Fátima Gomes",
            "fatima.gomes",
            UtcInstant.From(new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero)));

        Assert.Equal(UserRole.Cashier, cashier.Role);
        Assert.Equal(CredentialKind.Pin, cashier.CredentialKind);
        Assert.False(cashier.IsPrimaryAdministrator);
    }

    [Fact]
    public void FifthFailedLoginLocksUserForFifteenMinutes()
    {
        UtcInstant now = UtcInstant.From(
            new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero));
        LocalUser cashier = LocalUser.CreateCashier(
            EntityId.New(),
            "Fátima Gomes",
            "fatima.gomes",
            now);

        for (int attempt = 0; attempt < 5; attempt++)
        {
            cashier.RecordFailedLogin(now);
        }

        Assert.Equal(5, cashier.FailedLoginCount);
        Assert.Equal(now.Value.AddMinutes(15), cashier.LockedUntilUtc?.Value);
        Assert.True(cashier.IsLockedAt(UtcInstant.From(now.Value.AddMinutes(14))));
        Assert.False(cashier.IsLockedAt(UtcInstant.From(now.Value.AddMinutes(15))));
    }

    [Fact]
    public void SuccessfulLoginClearsPreviousFailures()
    {
        UtcInstant now = UtcInstant.From(
            new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero));
        LocalUser administrator = LocalUser.CreatePrimaryAdministrator(
            EntityId.New(),
            "Maria Indjai",
            "maria.indjai",
            now);
        administrator.RecordFailedLogin(now);
        administrator.RecordFailedLogin(now);

        administrator.RecordSuccessfulLogin(now);

        Assert.Equal(0, administrator.FailedLoginCount);
        Assert.Null(administrator.LockedUntilUtc);
        Assert.Equal(now, administrator.LastSuccessfulLoginUtc);
    }
}
