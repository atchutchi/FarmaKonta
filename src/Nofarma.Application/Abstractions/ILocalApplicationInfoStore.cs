using Nofarma.Application.Configuration;

namespace Nofarma.Application.Abstractions;

public interface ILocalApplicationInfoStore
{
    Task<LocalApplicationInfo?> GetAsync(CancellationToken cancellationToken);
}
