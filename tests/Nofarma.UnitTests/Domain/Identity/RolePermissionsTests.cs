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
        Capability.ManageBackups,
        Capability.ViewSuppliers,
        Capability.ViewPurchases,
        Capability.ImportInventory,
        Capability.AdjustStock,
        Capability.CompensateStock
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

    [Theory]
    [InlineData(Capability.ViewSuppliers)]
    [InlineData(Capability.ViewPurchases)]
    [InlineData(Capability.ImportInventory)]
    [InlineData(Capability.AdjustStock)]
    [InlineData(Capability.CompensateStock)]
    public void CashierIsDeniedInventoryAdministration(Capability capability)
    {
        Assert.False(RolePermissions.IsAllowed(UserRole.Cashier, capability));
    }

    [Theory]
    [InlineData(Capability.ViewSuppliers)]
    [InlineData(Capability.ViewPurchases)]
    public void AuditorCanReadSupplyHistory(Capability capability)
    {
        Assert.True(RolePermissions.IsAllowed(UserRole.Auditor, capability));
    }

    [Theory]
    [InlineData(Capability.ImportInventory)]
    [InlineData(Capability.AdjustStock)]
    public void StockManagerCanOperateInventory(Capability capability)
    {
        Assert.True(RolePermissions.IsAllowed(UserRole.StockManager, capability));
    }

    [Fact]
    public void StockManagerCannotCompensateConfirmedMovementByDefault()
    {
        Assert.False(RolePermissions.IsAllowed(
            UserRole.StockManager,
            Capability.CompensateStock));
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

    [Theory]
    [InlineData(UserRole.Administrator, true)]
    [InlineData(UserRole.Manager, true)]
    [InlineData(UserRole.Pharmacist, false)]
    [InlineData(UserRole.Cashier, false)]
    public void SaleDiscountRequiresAdministrativeRole(UserRole role, bool expected)
    {
        Assert.Equal(
            expected,
            RolePermissions.IsAllowed(role, Capability.ApplySaleDiscount));
    }
}
