using Nofarma.Application.Identity.Authentication;
using Nofarma.Application.Identity.Users;
using Nofarma.Domain.Identity;

namespace Nofarma.Desktop.ViewModels;

public sealed class UsersViewModel(
    UserAdministrationService userAdministration,
    CurrentSession currentSession)
{
    public IReadOnlyList<ManagedUserSummary> Users { get; private set; } = [];

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        LocalSession session = currentSession.Active
            ?? throw new InvalidOperationException("Não existe uma sessão activa.");
        Users = await userAdministration.ListAsync(session, cancellationToken);
    }

    public async Task CreateAsync(
        string displayName,
        string login,
        UserRole role,
        string credential,
        CancellationToken cancellationToken)
    {
        LocalSession session = currentSession.Active
            ?? throw new InvalidOperationException("Não existe uma sessão activa.");
        await userAdministration.CreateAsync(
            session,
            new CreateUserRequest(displayName, login, role, credential),
            cancellationToken);
        await LoadAsync(cancellationToken);
    }
}
