using Nofarma.Application.Identity.Authentication;

namespace Nofarma.Application.Abstractions;

public interface ILocalProfileStore
{
    Task<IReadOnlyList<LocalProfile>> ListProfilesAsync(
        CancellationToken cancellationToken);
}
