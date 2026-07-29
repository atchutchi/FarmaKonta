using System.Globalization;
using Nofarma.Application.Licensing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Licensing;

namespace Nofarma.UnitTests.TestSupport.Licensing;

internal static class LicenseTestData
{
    internal static readonly EntityId PharmacyId = Id("11111111-1111-1111-1111-111111111111");
    internal static readonly EntityId OtherEstablishmentId = Id("22222222-2222-2222-2222-222222222222");
    private static readonly EntityId EstablishmentId = Id("11111111-1111-1111-1111-111111111111");
    internal static readonly EntityId DeviceId = Id("33333333-3333-3333-3333-333333333333");
    private static readonly EntityId LicenseId = Id("44444444-4444-4444-4444-444444444444");

    internal static LicenseGrant Monthly(string from, string until, string grace) =>
        Create(
            plan: LicensePlan.Monthly,
            validFrom: from,
            validUntil: until,
            graceUntil: grace);

    internal static LicenseGrant Annual(string validFrom, string validUntil, string graceUntil) =>
        Create(
            plan: LicensePlan.Annual,
            validFrom: validFrom,
            validUntil: validUntil,
            graceUntil: graceUntil);

    internal static LicenseGrant Active(long sequence) => Create(sequence: sequence);

    internal static StoredLicense StoredActive(long sequence, ReadOnlyMemory<byte> document = default) =>
        Stored(Active(sequence), document);

    internal static StoredLicense Stored(LicenseGrant grant, ReadOnlyMemory<byte> document) =>
        new(document);

    internal static UtcInstant Instant(string value) =>
        UtcInstant.From(DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal));

    internal static LicenseGrant Create(
        EntityId? id = null,
        EntityId? pharmacyId = null,
        EntityId? establishmentId = null,
        EntityId? deviceId = null,
        LicensePlan plan = LicensePlan.Monthly,
        long sequence = 1,
        string issuedAt = "2026-08-01T00:00:00Z",
        string validFrom = "2026-08-01T00:00:00Z",
        string validUntil = "2026-08-31T23:59:59Z",
        string graceUntil = "2026-09-07T23:59:59Z") =>
        new(
            id ?? LicenseId,
            pharmacyId ?? PharmacyId,
            establishmentId ?? EstablishmentId,
            deviceId ?? DeviceId,
            plan,
            sequence,
            Instant(issuedAt),
            Instant(validFrom),
            Instant(validUntil),
            Instant(graceUntil));

    private static EntityId Id(string value) => new(Guid.Parse(value));
}
