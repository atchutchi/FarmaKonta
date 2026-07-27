using Nofarma.Application.Identity.Setup;

namespace Nofarma.Application.Abstractions;

public interface ILocalIdentityStore
{
    Task<bool> IsConfiguredAsync(CancellationToken cancellationToken);

    Task SaveInitialSetupAsync(
        InitialSetupData data,
        CancellationToken cancellationToken);
}
