using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nofarma.Application.Licensing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Licensing;
using Nofarma.Infrastructure.Licensing;
using Nofarma.Licensing.Qa;

namespace Nofarma.IntegrationTests.Licensing;

public sealed class QaIssuerTests
{
    private static readonly DateTimeOffset From = Utc("2026-08-01T00:00:00Z");
    private static readonly DateTimeOffset Until = Utc("2026-08-31T23:59:59Z");
    private static readonly DateTimeOffset IssuedAt = Utc("2026-07-29T12:00:00Z");
    private static readonly Guid LicenseId =
        Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly EntityId PharmacyId =
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly EntityId DeviceId =
        new(Guid.Parse("33333333-3333-3333-3333-333333333333"));
    private const string DeviceKeyThumbprint =
        "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public void CommercialBuildRejectsCryptographicallyIdenticalQaKeyAfterBase64Reencoding()
    {
        string repoRoot = FindRepoRoot();
        string temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"nofarma-qa-channel-gate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string canonicalBase64 = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
            string reencodedBase64 = string.Join(
                Environment.NewLine,
                canonicalBase64.Chunk(37).Select(characters => new string(characters)));
            string qaPublicKeyPath = Path.Combine(temporaryDirectory, "qa.spki.b64");
            string commercialPublicKeyPath = Path.Combine(temporaryDirectory, "commercial.spki.b64");
            File.WriteAllText(qaPublicKeyPath, canonicalBase64);
            File.WriteAllText(
                commercialPublicKeyPath,
                $"{Environment.NewLine}  {reencodedBase64}  {Environment.NewLine}");

            ProcessResult result = BuildCommercial(
                repoRoot,
                qaPublicKeyPath,
                commercialPublicKeyPath);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("NFLC002", result.Output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public void QaIssuedLicenseValidatesOnlyInQaRegistry()
    {
        using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var issuer = new QaLicenseIssuer(
            signingKey,
            new FixedTimeProvider(IssuedAt),
            () => LicenseId);
        byte[] document = issuer.Issue(
            ActivationRequest(LicenseBuildChannel.Qa),
            LicensePlan.Monthly,
            From,
            Until);
        var context = new LicenseContext(PharmacyId, PharmacyId, DeviceId);
        var device = new DeviceLicenseIdentity(PharmacyId, DeviceId, DeviceKeyThumbprint);
        var trustedKey = new KeyValuePair<string, ReadOnlyMemory<byte>>(
            issuer.KeyId,
            issuer.PublicKey);
        var qaVerifier = new EcdsaLicenseDocumentVerifier(
            new TrustedLicenseKeyRegistry(LicenseBuildChannel.Qa, [trustedKey]));
        var commercialVerifier = new EcdsaLicenseDocumentVerifier(
            new TrustedLicenseKeyRegistry(LicenseBuildChannel.Commercial, [trustedKey]));

        LicenseVerification qaResult = qaVerifier.Verify(document, device, context);
        LicenseVerification commercialResult =
            commercialVerifier.Verify(document, device, context);

        Assert.True(qaResult.IsValid);
        Assert.Equal("CHANNEL_MISMATCH", commercialResult.Code);
    }

    [Fact]
    public void IssuerRejectsActivationRequestFromAnotherChannel()
    {
        using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var issuer = Issuer(signingKey);

        QaIssuerException error = Assert.Throws<QaIssuerException>(() => issuer.Issue(
            ActivationRequest(LicenseBuildChannel.Commercial),
            LicensePlan.Monthly,
            From,
            Until));

        Assert.Equal("QA_REQUEST_CHANNEL_REQUIRED", error.Code);
    }

    [Theory]
    [InlineData(LicensePlan.Monthly, 31, 1)]
    [InlineData(LicensePlan.Annual, 366, 1)]
    public void IssuerRejectsValidityBeyondPlanLimit(
        LicensePlan plan,
        int days,
        int extraTicks)
    {
        using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var issuer = Issuer(signingKey);

        QaIssuerException error = Assert.Throws<QaIssuerException>(() => issuer.Issue(
            ActivationRequest(LicenseBuildChannel.Qa),
            plan,
            From,
            From.AddDays(days).AddTicks(extraTicks)));

        Assert.Equal("VALIDITY_TOO_LONG", error.Code);
    }

    [Fact]
    public void IssuerRejectsInvertedValidityDates()
    {
        using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var issuer = Issuer(signingKey);

        QaIssuerException error = Assert.Throws<QaIssuerException>(() => issuer.Issue(
            ActivationRequest(LicenseBuildChannel.Qa),
            LicensePlan.Monthly,
            From,
            From.AddTicks(-1)));

        Assert.Equal("VALIDITY_DATES_INVALID", error.Code);
    }

    [Fact]
    public void IssuerRejectsDatesThatAreNotUtc()
    {
        using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var issuer = Issuer(signingKey);
        DateTimeOffset nonUtc = From.ToOffset(TimeSpan.FromHours(1));

        QaIssuerException error = Assert.Throws<QaIssuerException>(() => issuer.Issue(
            ActivationRequest(LicenseBuildChannel.Qa),
            LicensePlan.Monthly,
            nonUtc,
            nonUtc.AddDays(1)));

        Assert.Equal("VALIDITY_DATES_MUST_BE_UTC", error.Code);
    }

    [Fact]
    public void IssueToFileDoesNotOverwriteAnExistingOutput()
    {
        using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var issuer = Issuer(signingKey);
        using var directory = new TemporaryDirectory("nofarma-qa-output");
        string outputPath = Path.Combine(directory.Path, "existing.nofarma-license");
        byte[] existing = Encoding.UTF8.GetBytes("existing-output");
        File.WriteAllBytes(outputPath, existing);

        Assert.Throws<IOException>(() => issuer.IssueToFile(
            ActivationRequest(LicenseBuildChannel.Qa),
            LicensePlan.Monthly,
            From,
            Until,
            outputPath));

        Assert.Equal(existing, File.ReadAllBytes(outputPath));
    }

    [Fact]
    public void ProvisionDoesNotReplaceAnExistingPrivateKeyWithoutRotate()
    {
        using var directory = new TemporaryDirectory("nofarma-qa-provision");
        string privateKeyPath = Path.Combine(directory.Path, "qa-signing-key.bin");
        string publicKeyPath = Path.Combine(directory.Path, "qa-public.spki.b64");
        var protector = new TestProtector();
        var store = new QaKeyStore(privateKeyPath, protector);
        store.Provision(publicKeyPath, rotate: false, TextReader.Null);
        byte[] originalProtectedKey = File.ReadAllBytes(privateKeyPath);
        byte[] originalPublicKey = File.ReadAllBytes(publicKeyPath);

        store.Provision(publicKeyPath, rotate: false, TextReader.Null);

        Assert.Equal(originalProtectedKey, File.ReadAllBytes(privateKeyPath));
        Assert.Equal(originalPublicKey, File.ReadAllBytes(publicKeyPath));
    }

    [Fact]
    public void RotationRequiresTheExactTerminalConfirmation()
    {
        using var directory = new TemporaryDirectory("nofarma-qa-rotation");
        string privateKeyPath = Path.Combine(directory.Path, "qa-signing-key.bin");
        string publicKeyPath = Path.Combine(directory.Path, "qa-public.spki.b64");
        var store = new QaKeyStore(privateKeyPath, new TestProtector());
        store.Provision(publicKeyPath, rotate: false, TextReader.Null);
        byte[] originalProtectedKey = File.ReadAllBytes(privateKeyPath);

        QaIssuerException error = Assert.Throws<QaIssuerException>(() => store.Provision(
            publicKeyPath,
            rotate: true,
            new StringReader("rotate-qa-key")));

        Assert.Equal("QA_ROTATION_CONFIRMATION_REQUIRED", error.Code);
        Assert.Equal(originalProtectedKey, File.ReadAllBytes(privateKeyPath));

        store.Provision(
            publicKeyPath,
            rotate: true,
            new StringReader("ROTATE-QA-KEY"));
        Assert.NotEqual(originalProtectedKey, File.ReadAllBytes(privateKeyPath));
    }

    [Fact]
    public void ProvisionRejectsPublicOutputOverTheProtectedPrivateKey()
    {
        using var directory = new TemporaryDirectory("nofarma-qa-path-guard");
        string privateKeyPath = Path.Combine(directory.Path, "qa-signing-key.bin");
        string publicKeyPath = Path.Combine(directory.Path, "qa-public.spki.b64");
        var store = new QaKeyStore(privateKeyPath, new TestProtector());
        store.Provision(publicKeyPath, rotate: false, TextReader.Null);
        byte[] originalProtectedKey = File.ReadAllBytes(privateKeyPath);

        QaIssuerException error = Assert.Throws<QaIssuerException>(() => store.Provision(
            privateKeyPath,
            rotate: false,
            TextReader.Null));

        Assert.Equal("QA_PUBLIC_OUTPUT_INVALID", error.Code);
        Assert.Equal(originalProtectedKey, File.ReadAllBytes(privateKeyPath));
    }

    [Fact]
    public void ProvisionWritesOnlyAValidP256PublicSpki()
    {
        using var directory = new TemporaryDirectory("nofarma-qa-public");
        string privateKeyPath = Path.Combine(directory.Path, "qa-signing-key.bin");
        string publicKeyPath = Path.Combine(directory.Path, "qa-public.spki.b64");
        var store = new QaKeyStore(privateKeyPath, new TestProtector());

        store.Provision(publicKeyPath, rotate: false, TextReader.Null);

        string publicText = File.ReadAllText(publicKeyPath);
        Assert.DoesNotContain("PRIVATE KEY", publicText, StringComparison.Ordinal);
        byte[] publicSpki = Convert.FromBase64String(publicText);
        using ECDsa publicKey = ECDsa.Create();
        publicKey.ImportSubjectPublicKeyInfo(publicSpki, out int bytesRead);
        Assert.Equal(publicSpki.Length, bytesRead);
        Assert.Equal(256, publicKey.KeySize);
        using ECDsa privateKey = store.OpenSigningKey();
        Assert.Equal(
            privateKey.ExportSubjectPublicKeyInfo(),
            publicKey.ExportSubjectPublicKeyInfo());
    }

    [Fact]
    public void PublicKeyGateRejectsEquivalentKeysAndNonP256CommercialKeys()
    {
        using var directory = new TemporaryDirectory("nofarma-qa-key-gate");
        using ECDsa qaKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa p384Key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        string qaPath = Path.Combine(directory.Path, "qa.spki.b64");
        string equalPath = Path.Combine(directory.Path, "equal.spki.b64");
        string p384Path = Path.Combine(directory.Path, "p384.spki.b64");
        string distinctPath = Path.Combine(directory.Path, "distinct.spki.b64");
        string qaBase64 = Convert.ToBase64String(qaKey.ExportSubjectPublicKeyInfo());
        using ECDsa distinctKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(qaPath, qaBase64);
        File.WriteAllText(equalPath, InsertWhitespace(qaBase64));
        File.WriteAllText(
            p384Path,
            Convert.ToBase64String(p384Key.ExportSubjectPublicKeyInfo()));
        File.WriteAllText(
            distinctPath,
            Convert.ToBase64String(distinctKey.ExportSubjectPublicKeyInfo()));

        LicenseChannelKeyValidation equal =
            QaPublicKeyValidator.ValidateCommercial(qaPath, equalPath);
        LicenseChannelKeyValidation wrongCurve =
            QaPublicKeyValidator.ValidateCommercial(qaPath, p384Path);
        LicenseChannelKeyValidation wrongQaCurve =
            QaPublicKeyValidator.ValidateCommercial(p384Path, distinctPath);
        LicenseChannelKeyValidation distinct =
            QaPublicKeyValidator.ValidateCommercial(qaPath, distinctPath);

        Assert.Equal("NFLC002", equal.Code);
        Assert.Equal("NFLC004", wrongCurve.Code);
        Assert.Equal("NFLC003", wrongQaCurve.Code);
        Assert.True(distinct.IsValid);
        Assert.Null(distinct.Code);
    }

    [Fact]
    public void RepositoryDoesNotContainQaPrivateKey()
    {
        string repoRoot = FindRepoRoot();
        string qaPublicKeyPath = Path.Combine(
            repoRoot,
            "build",
            "keys",
            "nofarma-qa-public.spki.b64");
        Assert.False(File.Exists(Path.Combine(
            repoRoot,
            "build",
            "keys",
            "nofarma-qa-private.p8")));
        Assert.True(File.Exists(qaPublicKeyPath));
        string publicText = File.ReadAllText(qaPublicKeyPath);
        Assert.DoesNotContain("PRIVATE KEY", publicText, StringComparison.Ordinal);
        byte[] publicSpki = Convert.FromBase64String(publicText);
        using ECDsa publicKey = ECDsa.Create();
        publicKey.ImportSubjectPublicKeyInfo(publicSpki, out int bytesRead);
        Assert.Equal(publicSpki.Length, bytesRead);
        Assert.Equal(256, publicKey.KeySize);
    }

    private static ProcessResult BuildCommercial(
        string repoRoot,
        string qaPublicKeyPath,
        string commercialPublicKeyPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(
            Path.Combine(repoRoot, "src", "Nofarma.Desktop", "Nofarma.Desktop.csproj"));
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("--no-restore");
        startInfo.ArgumentList.Add("-p:NofarmaLicenseChannel=Commercial");
        startInfo.ArgumentList.Add($"-p:NofarmaQaPublicKeyPath={qaPublicKeyPath}");
        startInfo.ArgumentList.Add($"-p:NofarmaCommercialPublicKeyPath={commercialPublicKeyPath}");

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the Commercial build.");
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(
            process.ExitCode,
            string.Concat(standardOutput, Environment.NewLine, standardError));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Nofarma.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static QaLicenseIssuer Issuer(ECDsa signingKey) =>
        new(signingKey, new FixedTimeProvider(IssuedAt), () => LicenseId);

    private static byte[] ActivationRequest(LicenseBuildChannel channel)
    {
        var context = new LicenseContext(PharmacyId, PharmacyId, DeviceId);
        var device = new DeviceLicenseIdentity(PharmacyId, DeviceId, DeviceKeyThumbprint);
        return CanonicalLicenseActivationRequestJson.Serialize(
            new LicenseActivationRequest(context, device),
            channel);
    }

    private static string InsertWhitespace(string base64) =>
        string.Concat(
            Environment.NewLine,
            " ",
            string.Join(
                $"{Environment.NewLine}\t",
                base64.Chunk(29).Select(characters => new string(characters))),
            Environment.NewLine);

    private static DateTimeOffset Utc(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private sealed record ProcessResult(int ExitCode, string Output);

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class TestProtector : ILocalDataProtector
    {
        private readonly byte[] _mask = RandomNumberGenerator.GetBytes(32);

        public byte[] Protect(ReadOnlySpan<byte> clear, ReadOnlySpan<byte> entropy) =>
            Transform(clear, entropy);

        public byte[] Unprotect(ReadOnlySpan<byte> encrypted, ReadOnlySpan<byte> entropy) =>
            Transform(encrypted, entropy);

        private byte[] Transform(ReadOnlySpan<byte> value, ReadOnlySpan<byte> entropy)
        {
            byte[] entropyHash = SHA256.HashData(entropy);
            byte[] transformed = new byte[value.Length];
            for (int index = 0; index < value.Length; index++)
            {
                transformed[index] = (byte)(
                    value[index]
                    ^ _mask[index % _mask.Length]
                    ^ entropyHash[index % entropyHash.Length]);
            }

            return transformed;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory(string prefix)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"{prefix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
