using Microsoft.Extensions.DependencyInjection;
using Nofarma.Desktop.Services;
using Nofarma.Desktop.ViewModels;

namespace Nofarma.Desktop.Composition;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNofarmaDesktop(
        this IServiceCollection services,
        DesktopLicenseConfiguration licenseConfiguration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(licenseConfiguration);
        services.AddSingleton(licenseConfiguration);
        services.AddSingleton(new LicenseChannelContext(licenseConfiguration.Channel));
        services.AddSingleton<MainWindow>();
        services.AddSingleton<NavigationService>();
        services.AddTransient<SetupWizardViewModel>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<ILoginRecoveryOperations, LoginRecoveryOperations>();
        services.AddTransient<LoginRecoveryViewModel>();
        services.AddTransient<UsersViewModel>();
        services.AddTransient<IProductPageOperations, ProductPageOperations>();
        services.AddTransient<ProductsViewModel>();
        services.AddTransient<ISupplierPageOperations, SupplierPageOperations>();
        services.AddTransient<SuppliersViewModel>();
        services.AddTransient<IStockPageOperations, StockPageOperations>();
        services.AddTransient<StockViewModel>();
        services.AddTransient<IPurchasesPageOperations, PurchasesPageOperations>();
        services.AddTransient<PurchasesViewModel>();
        services.AddTransient<ICashPageOperations, CashPageOperations>();
        services.AddTransient<CashViewModel>();
        services.AddTransient<IInventoryImportPageOperations, InventoryImportPageOperations>();
        services.AddTransient<InventoryImportViewModel>();
        services.AddSingleton<LicensePageOperations>();
        services.AddSingleton<ILicensePageOperations>(provider =>
            provider.GetRequiredService<LicensePageOperations>());
        services.AddTransient<LicenseViewModel>();
        return services;
    }
}
