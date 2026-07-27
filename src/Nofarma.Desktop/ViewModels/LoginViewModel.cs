using Nofarma.Application.Abstractions;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Domain.Identity;

namespace Nofarma.Desktop.ViewModels;

public sealed class LoginViewModel(
    ILocalProfileStore profileStore,
    AuthenticationService authenticationService,
    CurrentSession currentSession)
{
    public IReadOnlyList<LocalProfile> AdministratorProfiles { get; private set; } = [];

    public IReadOnlyList<LocalProfile> CashierProfiles { get; private set; } = [];

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<LocalProfile> profiles = await profileStore
            .ListProfilesAsync(cancellationToken);
        AdministratorProfiles = profiles
            .Where(profile =>
                profile.CredentialKind == CredentialKind.Password &&
                profile.Status == UserStatus.Active)
            .ToArray();
        CashierProfiles = profiles
            .Where(profile =>
                profile.CredentialKind == CredentialKind.Pin &&
                profile.Status == UserStatus.Active)
            .ToArray();
    }

    public async Task<SignInResult> SignInAsync(
        LocalProfile profile,
        string credential,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        SignInResult result = await authenticationService.SignInAsync(
            new SignInRequest(profile.Login, credential),
            cancellationToken);
        if (result.Succeeded)
        {
            currentSession.SetDisplayName(profile.DisplayName);
        }

        return result;
    }
}
