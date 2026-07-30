using Nofarma.Domain.Common;

namespace Nofarma.Domain.Identity;

public sealed class Installation
{
    private Installation(
        EntityId id,
        Pharmacy pharmacy,
        Device device,
        LocalUser primaryAdministrator,
        UtcInstant createdAtUtc)
    {
        Id = id;
        Pharmacy = pharmacy;
        Device = device;
        PrimaryAdministrator = primaryAdministrator;
        CreatedAtUtc = createdAtUtc;
        Status = InstallationStatus.Preparing;
    }

    public EntityId Id { get; }

    public Pharmacy Pharmacy { get; }

    public Device Device { get; }

    public LocalUser PrimaryAdministrator { get; }

    public UtcInstant CreatedAtUtc { get; }

    public UtcInstant? ReadyForActivationAtUtc { get; private set; }

    public InstallationStatus Status { get; private set; }

    public static Installation Create(
        EntityId id,
        Pharmacy pharmacy,
        Device device,
        LocalUser primaryAdministrator,
        UtcInstant createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(pharmacy);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(primaryAdministrator);

        if (!primaryAdministrator.IsPrimaryAdministrator ||
            primaryAdministrator.Role != UserRole.Administrator ||
            primaryAdministrator.CredentialKind != CredentialKind.Password)
        {
            throw new ArgumentException(
                "The installation requires a primary administrator.",
                nameof(primaryAdministrator));
        }

        return new Installation(id, pharmacy, device, primaryAdministrator, createdAtUtc);
    }

    public void MarkReadyForActivation(UtcInstant occurredAtUtc)
    {
        if (Status != InstallationStatus.Preparing)
        {
            throw new InvalidOperationException(
                "Only an installation in preparation can become ready for activation.");
        }

        Status = InstallationStatus.ReadyForActivation;
        ReadyForActivationAtUtc = occurredAtUtc;
    }
}
