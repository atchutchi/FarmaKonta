namespace Nofarma.Infrastructure.Persistence.Records;

public sealed class InstallationRecord
{
    public Guid Id { get; set; }

    public string SingletonKey { get; set; } = "LOCAL";

    public int Status { get; set; }

    public Guid PharmacyId { get; set; }

    public Guid DeviceId { get; set; }

    public Guid PrimaryAdministratorId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? ReadyForActivationAtUtc { get; set; }

    public PharmacyRecord Pharmacy { get; set; } = null!;

    public DeviceRecord Device { get; set; } = null!;
}
