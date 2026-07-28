using Nofarma.Domain.Common;
using Nofarma.Domain.Licensing;

namespace Nofarma.Application.Licensing;

public sealed record LicenseContext(
    EntityId PharmacyId,
    EntityId EstablishmentId,
    EntityId DeviceId);

public sealed record DeviceLicenseIdentity(
    EntityId PharmacyId,
    EntityId DeviceId,
    string PublicKeyThumbprint);

public sealed record LicenseClockBinding(
    EntityId PharmacyId,
    EntityId DeviceId,
    string Channel,
    string DeviceKeyThumbprint);

public sealed record LicenseImportRequest(byte[] Document);

public sealed record LicenseActivationRequest(
    LicenseContext Context,
    DeviceLicenseIdentity Device);

public sealed record VerifiedLicense(
    LicenseGrant Grant,
    string Channel,
    string KeyId,
    ReadOnlyMemory<byte> Document);

public sealed record StoredLicense(VerifiedLicense License)
{
    public LicenseGrant Grant => License.Grant;

    public ReadOnlyMemory<byte> Document => License.Document;
}

public sealed record LicenseVerification(
    bool IsValid,
    string? Code,
    VerifiedLicense? License)
{
    public static LicenseVerification Valid(
        LicenseGrant grant,
        ReadOnlyMemory<byte> document = default,
        string channel = "QA",
        string keyId = "test-key") =>
        Valid(new VerifiedLicense(grant, channel, keyId, document));

    public static LicenseVerification Valid(VerifiedLicense license) =>
        new(true, null, license);

    public static LicenseVerification Invalid(string code) =>
        new(false, code, null);
}

public sealed record LicenseClockCheck(bool RollbackDetected);

public sealed record LicenseStatus(
    LicenseState State,
    bool AllowsNewOperations,
    bool AllowsReadOnlyAccess,
    LicenseGrant? Grant);

public sealed record LicensedOperationPolicyResult(bool IsAllowed, string? Code);
