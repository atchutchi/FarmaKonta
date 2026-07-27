using Nofarma.Domain.Identity;

namespace Nofarma.UnitTests.Domain.Identity;

public sealed class RolePermissionsTests
{
    public static TheoryData<Capability> AdministrativePermissions => new()
    {
        Capability.ManageUsers,
        Capability.ManagePermissions,
        Capability.ConfigureFiscalSettings,
        Capability.ViewPurchasePrices,
        Capability.ManageBackups
    };

    [Theory]
    [MemberData(nameof(AdministrativePermissions))]
    public void CashierIsDeniedAdministrativePermissions(Capability capability)
    {
        Assert.False(RolePermissions.IsAllowed(UserRole.Cashier, capability));
    }

    [Theory]
    [InlineData(Capability.SignIn)]
    [InlineData(Capability.LockOwnSession)]
    [InlineData(Capability.CreateSale)]
    [InlineData(Capability.ManageCashShift)]
    public void CashierReceivesOnlyOperationalPermissions(Capability capability)
    {
        Assert.True(RolePermissions.IsAllowed(UserRole.Cashier, capability));
    }

    [Fact]
    public void SupportRoleHasNoPermanentPermissions()
    {
        Assert.Empty(RolePermissions.GetPermissions(UserRole.AbiptomSupport));
    }

    [Fact]
    public void AdministratorPermissionsAreExplicitlyEnumerated()
    {
        IReadOnlySet<Capability> permissions =
            RolePermissions.GetPermissions(UserRole.Administrator);

        Assert.Contains(Capability.ManageUsers, permissions);
        Assert.Contains(Capability.ConfigureFiscalSettings, permissions);
        Assert.Equal(Enum.GetValues<Capability>().Length, permissions.Count);
    }
}
