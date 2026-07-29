using System.Security.Cryptography;
using Nofarma.Domain.Licensing;
using Nofarma.Infrastructure.Licensing;

namespace Nofarma.Licensing.Qa;

public sealed class QaLicenseIssuer
{
    private const int InitialSequence = 1;
    private const int MonthlyMaximumDays = 31;
    private const int AnnualMaximumDays = 366;
    private const string NistP256Oid = "1.2.840.10045.3.1.7";

    private readonly ECDsa _signingKey;
    private readonly TimeProvider _timeProvider;
    private readonly Func<Guid> _licenseIdFactory;
    private readonly byte[] _publicKey;

    public QaLicenseIssuer(
        ECDsa signingKey,
        TimeProvider? timeProvider = null,
        Func<Guid>? licenseIdFactory = null,
        IQaPrivateKeyParametersExporter? parametersExporter = null)
    {
        _signingKey = signingKey ?? throw new ArgumentNullException(nameof(signingKey));
        ValidateSigningKey(
            _signingKey,
            parametersExporter ??
                QaKeyStore.DefaultPrivateKeyParametersExporter.Instance);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _licenseIdFactory = licenseIdFactory ?? Guid.NewGuid;
        _publicKey = _signingKey.ExportSubjectPublicKeyInfo();
        byte[] hash = SHA256.HashData(_publicKey);
        KeyId = $"qa-{Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant()}";
    }

    public string KeyId { get; }

    public ReadOnlyMemory<byte> PublicKey => _publicKey.ToArray();

    public byte[] Issue(
        ReadOnlyMemory<byte> requestDocument,
        LicensePlan plan,
        DateTimeOffset validFromUtc,
        DateTimeOffset validUntilUtc)
    {
        if (!CanonicalLicenseActivationRequestJson.TryParse(
                requestDocument,
                out LicenseActivationRequestEnvelope? request)
            || request is null)
        {
            throw new QaIssuerException(
                "QA_REQUEST_INVALID",
                "The activation request is invalid.");
        }

        if (request.Channel != LicenseBuildChannel.Qa)
        {
            throw new QaIssuerException(
                "QA_REQUEST_CHANNEL_REQUIRED",
                "The QA issuer only accepts QA activation requests.");
        }

        ValidatePlan(plan);
        ValidateDates(plan, validFromUtc, validUntilUtc);

        DateTimeOffset issuedAtUtc = _timeProvider.GetUtcNow();
        if (issuedAtUtc.Offset != TimeSpan.Zero || issuedAtUtc > validUntilUtc)
        {
            throw new QaIssuerException(
                "VALIDITY_DATES_INVALID",
                "The validity dates cannot end before issuance.");
        }

        Guid licenseId = _licenseIdFactory();
        if (licenseId == Guid.Empty)
        {
            throw new QaIssuerException(
                "LICENSE_ID_INVALID",
                "The generated licence identifier is invalid.");
        }

        var unsigned = new SignedLicenseEnvelope(
            EcdsaLicenseDocumentVerifier.CurrentSchemaVersion,
            LicenseBuildChannel.Qa,
            KeyId,
            EcdsaLicenseDocumentVerifier.EcdsaP256Sha256P1363Algorithm,
            licenseId,
            request.PharmacyId,
            request.EstablishmentId,
            request.DeviceId,
            request.DeviceKeyThumbprint,
            plan,
            InitialSequence,
            issuedAtUtc,
            validFromUtc,
            validUntilUtc,
            validUntilUtc.AddDays(7),
            Array.Empty<int>(),
            ReadOnlyMemory<byte>.Empty);
        byte[] payload = CanonicalLicenseJson.SerializePayload(unsigned);
        byte[] signature = _signingKey.SignData(
            payload,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        try
        {
            return CanonicalLicenseJson.SerializeEnvelope(
                unsigned with { Signature = signature });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signature);
        }
    }

    public void IssueToFile(
        ReadOnlyMemory<byte> requestDocument,
        LicensePlan plan,
        DateTimeOffset validFromUtc,
        DateTimeOffset validUntilUtc,
        string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("The licence output path is required.", nameof(outputPath));
        }

        byte[] document = Issue(requestDocument, plan, validFromUtc, validUntilUtc);
        string fullOutputPath = Path.GetFullPath(outputPath);
        string? directory = Path.GetDirectoryName(fullOutputPath);
        if (directory is null)
        {
            throw new InvalidOperationException("The licence output path is invalid.");
        }

        Directory.CreateDirectory(directory);
        using var stream = new FileStream(
            fullOutputPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        stream.Write(document);
        stream.Flush(flushToDisk: true);
    }

    private static void ValidatePlan(LicensePlan plan)
    {
        if (plan is not (LicensePlan.Monthly or LicensePlan.Annual))
        {
            throw new QaIssuerException(
                "PLAN_INVALID",
                "The requested licence plan is not supported.");
        }
    }

    private static void ValidateDates(
        LicensePlan plan,
        DateTimeOffset validFromUtc,
        DateTimeOffset validUntilUtc)
    {
        if (validFromUtc.Offset != TimeSpan.Zero
            || validUntilUtc.Offset != TimeSpan.Zero)
        {
            throw new QaIssuerException(
                "VALIDITY_DATES_MUST_BE_UTC",
                "Licence validity dates must use UTC.");
        }

        if (validUntilUtc < validFromUtc)
        {
            throw new QaIssuerException(
                "VALIDITY_DATES_INVALID",
                "Licence validity cannot end before it starts.");
        }

        int maximumDays = plan == LicensePlan.Monthly
            ? MonthlyMaximumDays
            : AnnualMaximumDays;
        if (validUntilUtc - validFromUtc > TimeSpan.FromDays(maximumDays))
        {
            throw new QaIssuerException(
                "VALIDITY_TOO_LONG",
                "Licence validity exceeds the selected plan limit.");
        }
    }

    private static void ValidateSigningKey(
        ECDsa signingKey,
        IQaPrivateKeyParametersExporter parametersExporter)
    {
        ECParameters parameters = default;
        try
        {
            parameters = parametersExporter.Export(signingKey);
            if (signingKey.KeySize != 256
                || parameters.D is null
                || !string.Equals(
                    parameters.Curve.Oid.Value,
                    NistP256Oid,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The QA issuer requires an ECDSA NIST P-256 private signing key.",
                    nameof(signingKey));
            }
        }
        catch (CryptographicException exception)
        {
            throw new ArgumentException(
                "The QA issuer requires an ECDSA private signing key.",
                nameof(signingKey),
                exception);
        }
        finally
        {
            if (parameters.D is not null)
            {
                CryptographicOperations.ZeroMemory(parameters.D);
            }
        }
    }
}
