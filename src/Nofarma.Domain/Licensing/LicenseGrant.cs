using Nofarma.Domain.Common;

namespace Nofarma.Domain.Licensing;

public sealed class LicenseGrant
{
    public LicenseGrant(
        EntityId id,
        EntityId pharmacyId,
        EntityId establishmentId,
        EntityId deviceId,
        LicensePlan plan,
        long sequence,
        UtcInstant issuedAtUtc,
        UtcInstant validFromUtc,
        UtcInstant validUntilUtc,
        UtcInstant graceUntilUtc)
    {
        EnsureNonEmpty(id, nameof(id));
        EnsureNonEmpty(pharmacyId, nameof(pharmacyId));
        EnsureNonEmpty(establishmentId, nameof(establishmentId));
        EnsureNonEmpty(deviceId, nameof(deviceId));

        if (establishmentId != pharmacyId)
        {
            throw new ArgumentException(
                "The establishment must belong to the licensed pharmacy.",
                nameof(establishmentId));
        }

        if (sequence < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "The license sequence must be at least one.");
        }

        if (issuedAtUtc.Value > validUntilUtc.Value)
        {
            throw new ArgumentException(
                "The license cannot be issued after its validity ends.",
                nameof(issuedAtUtc));
        }

        if (validFromUtc.Value > validUntilUtc.Value)
        {
            throw new ArgumentException(
                "The license validity cannot end before it starts.",
                nameof(validFromUtc));
        }

        if (graceUntilUtc.Value != validUntilUtc.Value.AddDays(7))
        {
            throw new ArgumentException(
                "The license grace period must end exactly seven days after validity ends.",
                nameof(graceUntilUtc));
        }

        Id = id;
        PharmacyId = pharmacyId;
        EstablishmentId = establishmentId;
        DeviceId = deviceId;
        Plan = plan;
        Sequence = sequence;
        IssuedAtUtc = issuedAtUtc;
        ValidFromUtc = validFromUtc;
        ValidUntilUtc = validUntilUtc;
        GraceUntilUtc = graceUntilUtc;
    }

    public EntityId Id { get; }

    public EntityId PharmacyId { get; }

    public EntityId EstablishmentId { get; }

    public EntityId DeviceId { get; }

    public LicensePlan Plan { get; }

    public long Sequence { get; }

    public UtcInstant IssuedAtUtc { get; }

    public UtcInstant ValidFromUtc { get; }

    public UtcInstant ValidUntilUtc { get; }

    public UtcInstant GraceUntilUtc { get; }

    private static void EnsureNonEmpty(EntityId value, string parameterName)
    {
        if (value.Value == Guid.Empty)
        {
            throw new ArgumentException("The identifier cannot be empty.", parameterName);
        }
    }
}
