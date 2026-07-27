namespace Nofarma.Domain.Identity;

public enum Capability
{
    SignIn = 1,
    LockOwnSession = 2,
    CreateSale = 3,
    ManageCashShift = 4,
    ViewInvoices = 5,
    ViewProducts = 6,
    ManageProducts = 7,
    ViewStock = 8,
    ManageStock = 9,
    ViewPurchasePrices = 10,
    ManagePurchases = 11,
    ManageSuppliers = 12,
    ViewReports = 13,
    ViewAudit = 14,
    ManageUsers = 15,
    ManagePermissions = 16,
    ConfigurePharmacy = 17,
    ConfigureFiscalSettings = 18,
    ManageBackups = 19
}
