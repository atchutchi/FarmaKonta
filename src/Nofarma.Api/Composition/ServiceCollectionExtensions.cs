using Nofarma.Application.Abstractions;
using Nofarma.Infrastructure.Time;

namespace Nofarma.Api.Composition;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNofarmaFoundation(this IServiceCollection services)
    {
        services.AddSingleton<IUtcClock, SystemUtcClock>();
        return services;
    }
}
