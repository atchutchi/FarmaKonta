using System.Collections.Frozen;

namespace Nofarma.Domain.Identity;

public static class RolePermissions
{
    private static readonly FrozenSet<Capability> AdministratorPermissions =
        new[]
        {
            Capability.SignIn,
            Capability.LockOwnSession,
            Capability.CreateSale,
            Capability.ManageCashShift,
            Capability.ViewInvoices,
            Capability.ViewProducts,
            Capability.ManageProducts,
            Capability.ViewStock,
            Capability.ManageStock,
            Capability.ViewPurchasePrices,
            Capability.ManagePurchases,
            Capability.ManageSuppliers,
            Capability.ViewReports,
            Capability.ViewAudit,
            Capability.ManageUsers,
            Capability.ManagePermissions,
            Capability.ConfigurePharmacy,
            Capability.ConfigureFiscalSettings,
            Capability.ManageBackups,
            Capability.ViewSuppliers,
            Capability.ViewPurchases,
            Capability.ImportInventory,
            Capability.AdjustStock,
            Capability.CompensateStock,
            Capability.ApplySaleDiscount
        }.ToFrozenSet();

    private static readonly FrozenDictionary<UserRole, FrozenSet<Capability>> Matrix =
        new Dictionary<UserRole, FrozenSet<Capability>>
        {
            [UserRole.Administrator] = AdministratorPermissions,
            [UserRole.Manager] = Set(
                Capability.SignIn,
                Capability.LockOwnSession,
                Capability.CreateSale,
                Capability.ManageCashShift,
                Capability.ViewInvoices,
                Capability.ViewProducts,
                Capability.ManageProducts,
                Capability.ViewStock,
                Capability.ManageStock,
                Capability.ViewPurchasePrices,
                Capability.ManagePurchases,
                Capability.ManageSuppliers,
                Capability.ViewReports,
                Capability.ViewAudit,
                Capability.ManageUsers,
                Capability.ViewSuppliers,
                Capability.ViewPurchases,
                Capability.ApplySaleDiscount),
            [UserRole.Pharmacist] = Set(
                Capability.SignIn,
                Capability.LockOwnSession,
                Capability.CreateSale,
                Capability.ViewInvoices,
                Capability.ViewProducts,
                Capability.ViewStock,
                Capability.ViewReports),
            [UserRole.StockManager] = Set(
                Capability.SignIn,
                Capability.LockOwnSession,
                Capability.ViewProducts,
                Capability.ManageProducts,
                Capability.ViewStock,
                Capability.ManageStock,
                Capability.ViewPurchasePrices,
                Capability.ManagePurchases,
                Capability.ManageSuppliers,
                Capability.ViewReports,
                Capability.ViewSuppliers,
                Capability.ViewPurchases,
                Capability.ImportInventory,
                Capability.AdjustStock),
            [UserRole.Cashier] = Set(
                Capability.SignIn,
                Capability.LockOwnSession,
                Capability.CreateSale,
                Capability.ManageCashShift,
                Capability.ViewInvoices,
                Capability.ViewProducts,
                Capability.ViewStock),
            [UserRole.Auditor] = Set(
                Capability.SignIn,
                Capability.LockOwnSession,
                Capability.ViewInvoices,
                Capability.ViewProducts,
                Capability.ViewStock,
                Capability.ViewReports,
                Capability.ViewAudit,
                Capability.ViewSuppliers,
                Capability.ViewPurchases),
            [UserRole.AbiptomSupport] = FrozenSet<Capability>.Empty
        }.ToFrozenDictionary();

    public static bool IsAllowed(UserRole role, Capability capability) =>
        Matrix.TryGetValue(role, out FrozenSet<Capability>? permissions) &&
        permissions.Contains(capability);

    public static IReadOnlySet<Capability> GetPermissions(UserRole role) =>
        Matrix.TryGetValue(role, out FrozenSet<Capability>? permissions)
            ? permissions
            : FrozenSet<Capability>.Empty;

    private static FrozenSet<Capability> Set(params Capability[] permissions) =>
        permissions.ToFrozenSet();
}
