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

public sealed class StoredLicense
{
    private readonly byte[] _document;
    private readonly byte[] _concurrencyToken;

    public StoredLicense(
        ReadOnlyMemory<byte> document,
        ReadOnlyMemory<byte> concurrencyToken,
        bool hasValidIntegrity)
    {
        _document = document.ToArray();
        _concurrencyToken = concurrencyToken.ToArray();
        if (_concurrencyToken.Length == 0)
        {
            throw new ArgumentException(
                "The license concurrency token cannot be empty.",
                nameof(concurrencyToken));
        }

        HasValidIntegrity = hasValidIntegrity;
    }

    public ReadOnlyMemory<byte> Document => _document.ToArray();

    public ReadOnlyMemory<byte> ConcurrencyToken => _concurrencyToken.ToArray();

    public bool HasValidIntegrity { get; }
}

public enum LicenseStoreReplaceResult
{
    Applied = 1,
    AlreadyCurrent = 2,
    Conflict = 3
}

public sealed class LicenseStorePrecondition
{
    private readonly byte[] _expectedToken;

    private LicenseStorePrecondition(bool expectsAbsence, ReadOnlyMemory<byte> expectedToken)
    {
        ExpectsAbsence = expectsAbsence;
        _expectedToken = expectedToken.ToArray();
    }

    public bool ExpectsAbsence { get; }

    public ReadOnlyMemory<byte> ExpectedToken => _expectedToken.ToArray();

    public static LicenseStorePrecondition ExpectedAbsent() =>
        new(expectsAbsence: true, ReadOnlyMemory<byte>.Empty);

    public static LicenseStorePrecondition Matching(ReadOnlyMemory<byte> expectedToken)
    {
        if (expectedToken.IsEmpty)
        {
            throw new ArgumentException(
                "The expected license token cannot be empty.",
                nameof(expectedToken));
        }

        return new LicenseStorePrecondition(expectsAbsence: false, expectedToken);
    }
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
