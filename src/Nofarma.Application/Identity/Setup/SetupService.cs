using Nofarma.Application.Abstractions;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;

namespace Nofarma.Application.Identity.Setup;

public sealed class SetupService(
    ILocalIdentityStore store,
    ICredentialHasher credentialHasher,
    IRecoveryCodeGenerator recoveryCodeGenerator,
    IUtcClock clock)
{
    public async Task<SetupResult> ConfigureAsync(
        SetupRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        if (await store.IsConfiguredAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("This installation is already configured.");
        }

        UtcInstant now = clock.GetCurrentInstant();
        var pharmacy = new Pharmacy(
            EntityId.New(),
            request.PharmacyName,
            request.TaxIdentifier,
            request.Address,
            request.Contact,
            request.TimeZoneId);
        var device = new Device(EntityId.New(), request.DeviceName);
        LocalUser administrator = LocalUser.CreatePrimaryAdministrator(
            EntityId.New(),
            request.AdministratorName,
            request.AdministratorLogin,
            now);
        Installation installation = Installation.Create(
            EntityId.New(),
            pharmacy,
            device,
            administrator,
            now);
        installation.MarkReadyForActivation(now);

        string recoveryCode = recoveryCodeGenerator.Generate();
        CredentialHash administratorCredential =
            credentialHasher.Hash(request.AdministratorPassword);
        CredentialHash recoveryCredential = credentialHasher.Hash(recoveryCode);
        var auditEvent = new AuditEvent(
            EntityId.New(),
            pharmacy.Id,
            device.Id,
            administrator.Id,
            "installation.configured",
            "Installation",
            installation.Id.Value.ToString("D"),
            now,
            AuditOutcome.Success,
            null,
            "{}");

        var data = new InitialSetupData(
            installation,
            administratorCredential,
            recoveryCredential,
            auditEvent);
        await store.SaveInitialSetupAsync(data, cancellationToken).ConfigureAwait(false);

        return new SetupResult(installation.Id, recoveryCode);
    }

    private static void Validate(SetupRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PharmacyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TaxIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Address);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TimeZoneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DeviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AdministratorName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AdministratorLogin);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AdministratorPassword);

        string password = request.AdministratorPassword;
        bool isStrong = password.Length >= 12 &&
            password.Any(char.IsUpper) &&
            password.Any(char.IsLower) &&
            password.Any(char.IsDigit) &&
            password.Any(character => !char.IsLetterOrDigit(character));

        if (!isStrong)
        {
            throw new ArgumentException(
                "The administrator password does not meet the security requirements.",
                nameof(request));
        }
    }
}
