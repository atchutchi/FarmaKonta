using Microsoft.Extensions.DependencyInjection;
using Nofarma.Desktop.Services;
using Nofarma.Desktop.ViewModels;

namespace Nofarma.Desktop.Composition;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNofarmaDesktop(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<MainWindow>();
        services.AddSingleton<NavigationService>();
        services.AddTransient<SetupWizardViewModel>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<UsersViewModel>();
        services.AddTransient<IProductPageOperations, ProductPageOperations>();
        services.AddTransient<ProductsViewModel>();
        services.AddTransient<ISupplierPageOperations, SupplierPageOperations>();
        services.AddTransient<SuppliersViewModel>();
        return services;
    }
}
