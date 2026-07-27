using Microsoft.Extensions.DependencyInjection;

namespace Nofarma.Desktop.Composition;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNofarmaDesktop(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<MainWindow>();
        return services;
    }
}
