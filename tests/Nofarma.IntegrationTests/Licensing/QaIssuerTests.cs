using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Nofarma.Application.Licensing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Licensing;
using Nofarma.Infrastructure.Licensing;
using Nofarma.Licensing.Qa;

namespace Nofarma.IntegrationTests.Licensing;

[Collection(PublishedChannelTestGroup.Name)]
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
    private const int ErrorFileExistsHResult = unchecked((int)0x80070050);
    private const int ErrorAlreadyExistsHResult = unchecked((int)0x800700B7);
    private const int AccessDeniedHResult = unchecked((int)0x80070005);
    private const int DiskFullHResult = unchecked((int)0x80070070);

    [Fact]
    public void CommercialBuildRejectsCryptographicallyIdenticalQaKeyAfterBase64Reencoding()
    {
        string repoRoot = FindRepoRoot();
        using var temporaryRepo = new TemporaryDirectory("nofarma-qa-channel-gate");
        CopyCommercialBuildTree(repoRoot, temporaryRepo.Path);

        string fixedKeysDirectory = Path.Combine(temporaryRepo.Path, "build", "keys");
        Directory.CreateDirectory(fixedKeysDirectory);
        using ECDsa fixedQaKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string fixedQaBase64 = Convert.ToBase64String(
            fixedQaKey.ExportSubjectPublicKeyInfo());
        File.WriteAllText(
            Path.Combine(fixedKeysDirectory, "nofarma-qa-public.spki.b64"),
            fixedQaBase64);
        File.WriteAllText(
            Path.Combine(fixedKeysDirectory, "nofarma-commercial-public.spki.b64"),
            InsertWhitespace(fixedQaBase64));

        string overridesDirectory = Path.Combine(temporaryRepo.Path, "overrides");
        Directory.CreateDirectory(overridesDirectory);
        using ECDsa overrideQaKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa overrideCommercialKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string overrideQaPath = Path.Combine(overridesDirectory, "qa.spki.b64");
        string overrideCommercialPath = Path.Combine(
            overridesDirectory,
            "commercial.spki.b64");
        File.WriteAllText(
            overrideQaPath,
            Convert.ToBase64String(overrideQaKey.ExportSubjectPublicKeyInfo()));
        File.WriteAllText(
            overrideCommercialPath,
            Convert.ToBase64String(overrideCommercialKey.ExportSubjectPublicKeyInfo()));
        string fakeIssuerProject = CreateFakeIssuer(overridesDirectory);

        ProcessResult result = BuildCommercial(
            temporaryRepo.Path,
            overrideQaPath,
            overrideCommercialPath,
            fakeIssuerProject);

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(
            result.Output.Contains("NFLC002", StringComparison.Ordinal),
            Tail(result.Output, 6000));
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

    [Theory]
    [InlineData("2026-08-01T00:00:00Z", true)]
    [InlineData("2026-08-01T00:00:00+00:00", true)]
    [InlineData("2026-08-01T00:00:00.1234567Z", true)]
    [InlineData("2026-08-01T00:00:00", false)]
    [InlineData("2026-08-01T00:00:00+01:00", false)]
    [InlineData("2026-08-01 00:00:00Z", false)]
    public void CliRequiresCanonicalIsoDateWithExplicitUtcZone(
        string value,
        bool accepted)
    {
        using var directory = new TemporaryDirectory("nofarma-qa-cli-date");
        var standardOutput = new StringWriter(CultureInfo.InvariantCulture);
        var standardError = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = QaCli.Run(
            new[]
            {
                "issue",
                "--request",
                Path.Combine(directory.Path, "missing.nofarma-request"),
                "--plan",
                "Monthly",
                "--valid-from",
                value,
                "--output",
                Path.Combine(directory.Path, "output.nofarma-license")
            },
            TextReader.Null,
            standardOutput,
            standardError);

        if (accepted)
        {
            Assert.Equal(3, exitCode);
            Assert.Contains("QA_ISSUER_FAILED", standardError.ToString(), StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(2, exitCode);
            Assert.Contains(
                "VALIDITY_DATES_INVALID",
                standardError.ToString(),
                StringComparison.Ordinal);
        }
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
    public void ProvisionWithoutRotatePreservesAConflictingPublicOutput()
    {
        using var directory = new TemporaryDirectory("nofarma-qa-public-conflict");
        string privateKeyPath = Path.Combine(directory.Path, "qa-signing-key.bin");
        string initialPublicPath = Path.Combine(directory.Path, "qa-public.spki.b64");
        string conflictingOutputPath = Path.Combine(directory.Path, "existing-output.txt");
        var store = new QaKeyStore(privateKeyPath, new TestProtector());
        store.Provision(initialPublicPath, rotate: false, TextReader.Null);
        byte[] originalProtectedKey = File.ReadAllBytes(privateKeyPath);
        byte[] arbitraryContents = Encoding.UTF8.GetBytes("preserve-this-file");
        File.WriteAllBytes(conflictingOutputPath, arbitraryContents);

        QaIssuerException error = Assert.Throws<QaIssuerException>(() => store.Provision(
            conflictingOutputPath,
            rotate: false,
            TextReader.Null));

        Assert.Equal("QA_PUBLIC_OUTPUT_CONFLICT", error.Code);
        Assert.Equal(arbitraryContents, File.ReadAllBytes(conflictingOutputPath));
        Assert.Equal(originalProtectedKey, File.ReadAllBytes(privateKeyPath));
    }

    [Fact]
    public void ProvisionWithoutRotateAcceptsEquivalentReencodedPublicOutputWithoutWriting()
    {
        using var directory = new TemporaryDirectory("nofarma-qa-public-idempotent");
        string privateKeyPath = Path.Combine(directory.Path, "qa-signing-key.bin");
        string publicKeyPath = Path.Combine(directory.Path, "qa-public.spki.b64");
        var store = new QaKeyStore(privateKeyPath, new TestProtector());
        store.Provision(publicKeyPath, rotate: false, TextReader.Null);
        string reencoded = InsertWhitespace(File.ReadAllText(publicKeyPath).Trim());
        File.WriteAllText(publicKeyPath, reencoded);
        byte[] expectedBytes = File.ReadAllBytes(publicKeyPath);

        store.Provision(publicKeyPath, rotate: false, TextReader.Null);

        Assert.Equal(expectedBytes, File.ReadAllBytes(publicKeyPath));
    }

    [Fact]
    public void ProvisionWithoutRotateContainsPlatformFailureAsOutputConflict()
    {
        using var directory = new TemporaryDirectory("nofarma-qa-public-platform");
        string privateKeyPath = Path.Combine(directory.Path, "qa-signing-key.bin");
        string publicKeyPath = Path.Combine(directory.Path, "qa-public.spki.b64");
        var protector = new TestProtector();
        var initialStore = new QaKeyStore(privateKeyPath, protector);
        initialStore.Provision(publicKeyPath, rotate: false, TextReader.Null);
        var failingStore = new QaKeyStore(
            privateKeyPath,
            protector,
            publicKeyDecoder: new AlwaysFailingPublicKeyDecoder(
                new PlatformNotSupportedException("platform-detail")));

        QaIssuerException error = Assert.Throws<QaIssuerException>(() =>
            failingStore.Provision(publicKeyPath, rotate: false, TextReader.Null));

        Assert.Equal("QA_PUBLIC_OUTPUT_CONFLICT", error.Code);
        Assert.DoesNotContain("platform-detail", error.Message, StringComparison.Ordinal);
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
    public void PublicCommitFailureRollsBackThePrivateRotation()
    {
        using var directory = new TemporaryDirectory("nofarma-qa-rotation-rollback");
        string privateKeyPath = Path.Combine(directory.Path, "qa-signing-key.bin");
        string publicKeyPath = Path.Combine(directory.Path, "qa-public.spki.b64");
        var protector = new TestProtector();
        var initialStore = new QaKeyStore(privateKeyPath, protector);
        initialStore.Provision(publicKeyPath, rotate: false, TextReader.Null);
        byte[] originalProtectedKey = File.ReadAllBytes(privateKeyPath);
        byte[] originalPublicFile = File.ReadAllBytes(publicKeyPath);
        byte[] originalPublicSpki = Convert.FromBase64String(
            File.ReadAllText(publicKeyPath));
        var failingStore = new QaKeyStore(
            privateKeyPath,
            protector,
            new FailBeforePublicCommit());

        QaIssuerException error = Assert.Throws<QaIssuerException>(() =>
            failingStore.Provision(
                publicKeyPath,
                rotate: true,
                new StringReader("ROTATE-QA-KEY")));

        Assert.Equal("QA_ROTATION_FAILED", error.Code);
        Assert.Equal(originalProtectedKey, File.ReadAllBytes(privateKeyPath));
        Assert.Equal(originalPublicFile, File.ReadAllBytes(publicKeyPath));
        Assert.Equal(originalPublicSpki, initialStore.GetPublicKey().ToArray());
    }

    [Fact]
    public void InterruptedAfterPrivateCommitRestoresThePreviousPairOnNextUse()
    {
        using var directory = new TemporaryDirectory("nofarma-qa-crash-private");
        string privateKeyPath = Path.Combine(directory.Path, "qa-signing-key.bin");
        string publicKeyPath = Path.Combine(directory.Path, "qa-public.spki.b64");
        var protector = new TestProtector();
        var initialStore = new QaKeyStore(privateKeyPath, protector);
        initialStore.Provision(publicKeyPath, rotate: false, TextReader.Null);
        byte[] originalPrivate = File.ReadAllBytes(privateKeyPath);
        byte[] originalPublic = File.ReadAllBytes(publicKeyPath);
        var interruptedStore = new QaKeyStore(
            privateKeyPath,
            protector,
            new InterruptAfterPrivateCommit());

        Assert.Throws<SimulatedProcessTermination>(() => interruptedStore.Provision(
            publicKeyPath,
            rotate: true,
            new StringReader("ROTATE-QA-KEY")));
        Assert.True(File.Exists(string.Concat(privateKeyPath, ".rotation.json")));

        var recoveredStore = new QaKeyStore(privateKeyPath, protector);
        recoveredStore.Provision(publicKeyPath, rotate: false, TextReader.Null);

        Assert.Equal(originalPrivate, File.ReadAllBytes(privateKeyPath));
        Assert.Equal(originalPublic, File.ReadAllBytes(publicKeyPath));
        AssertNoRecoveryArtifacts(privateKeyPath, publicKeyPath);
    }

    [Fact]
    public void InterruptedAfterPublicCommitFinalizesTheNewPairOnNextUse()
    {
        using var directory = new TemporaryDirectory("nofarma-qa-crash-public");
        string privateKeyPath = Path.Combine(directory.Path, "qa-signing-key.bin");
        string publicKeyPath = Path.Combine(directory.Path, "qa-public.spki.b64");
        var protector = new TestProtector();
        var initialStore = new QaKeyStore(privateKeyPath, protector);
        initialStore.Provision(publicKeyPath, rotate: false, TextReader.Null);
        byte[] originalPrivate = File.ReadAllBytes(privateKeyPath);
        byte[] originalPublic = File.ReadAllBytes(publicKeyPath);
        var interruptedStore = new QaKeyStore(
            privateKeyPath,
            protector,
            new InterruptAfterPublicCommit());

        Assert.Throws<SimulatedProcessTermination>(() => interruptedStore.Provision(
            publicKeyPath,
            rotate: true,
            new StringReader("ROTATE-QA-KEY")));
        byte[] committedPrivate = File.ReadAllBytes(privateKeyPath);
        byte[] committedPublic = File.ReadAllBytes(publicKeyPath);

        var recoveredStore = new QaKeyStore(privateKeyPath, protector);
        recoveredStore.Provision(publicKeyPath, rotate: false, TextReader.Null);

        Assert.NotEqual(originalPrivate, committedPrivate);
        Assert.NotEqual(originalPublic, committedPublic);
        Assert.Equal(committedPrivate, File.ReadAllBytes(privateKeyPath));
        Assert.Equal(committedPublic, File.ReadAllBytes(publicKeyPath));
        Assert.Equal(
            Convert.FromBase64String(File.ReadAllText(publicKeyPath)),
            recoveredStore.GetPublicKey().ToArray());
        AssertNoRecoveryArtifacts(privateKeyPath, publicKeyPath);
    }

    [Fact]
    public void FailureAfterPublicCommitReturnsTheConfirmedNewPair()
    {
        using var directory = new TemporaryDirectory("nofarma-qa-post-public-failure");
        string privateKeyPath = Path.Combine(directory.Path, "qa-signing-key.bin");
        string publicKeyPath = Path.Combine(directory.Path, "qa-public.spki.b64");
        var protector = new TestProtector();
        var initialStore = new QaKeyStore(privateKeyPath, protector);
        initialStore.Provision(publicKeyPath, rotate: false, TextReader.Null);
        byte[] previousPrivate = File.ReadAllBytes(privateKeyPath);
        AssertNoRecoveryArtifacts(privateKeyPath, publicKeyPath);
        var failingStore = new QaKeyStore(
            privateKeyPath,
            protector,
            new FailAfterPublicCommit());

        QaKeyProvisioningResult result = failingStore.Provision(
            publicKeyPath,
            rotate: true,
            new StringReader("ROTATE-QA-KEY"));
        byte[] committedPrivate = File.ReadAllBytes(privateKeyPath);
        byte[] committedPublic = File.ReadAllBytes(publicKeyPath);

        Assert.NotEqual(previousPrivate, committedPrivate);
        Assert.Equal(
            result.PublicKey.ToArray(),
            Convert.FromBase64String(File.ReadAllText(publicKeyPath)));
        var repeatedStore = new QaKeyStore(privateKeyPath, protector);
        repeatedStore.Provision(publicKeyPath, rotate: false, TextReader.Null);
        Assert.Equal(committedPrivate, File.ReadAllBytes(privateKeyPath));
        Assert.Equal(committedPublic, File.ReadAllBytes(publicKeyPath));
        AssertNoRecoveryArtifacts(privateKeyPath, publicKeyPath);
    }

    [Fact]
    public async Task SecondOperationWaitsUntilTheRotationReleasesTheKeyLock()
    {
        using var directory = new TemporaryDirectory("nofarma-qa-key-lock");
        string privateKeyPath = Path.Combine(directory.Path, "qa-signing-key.bin");
        string publicKeyPath = Path.Combine(directory.Path, "qa-public.spki.b64");
        var protector = new TestProtector();
        var initialStore = new QaKeyStore(privateKeyPath, protector);
        initialStore.Provision(publicKeyPath, rotate: false, TextReader.Null);
        using var rotationPaused = new ManualResetEventSlim();
        using var releaseRotation = new ManualResetEventSlim();
        using var sharedLock = new BlockingKeyStoreLock();
        var firstStore = new QaKeyStore(
            privateKeyPath,
            protector,
            new PauseAfterPrivateCommit(rotationPaused, releaseRotation),
            keyStoreLock: sharedLock);
        var secondStore = new QaKeyStore(
            privateKeyPath,
            protector,
            keyStoreLock: sharedLock);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Task first = Task.Run(() => firstStore.Provision(
            publicKeyPath,
            rotate: true,
            new StringReader("ROTATE-QA-KEY")), cancellationToken);
        Assert.True(rotationPaused.Wait(TimeSpan.FromSeconds(5), cancellationToken));
        Task second = Task.Run(() =>
        {
            _ = secondStore.GetPublicKey();
        }, cancellationToken);
        Assert.True(sharedLock.SecondAcquireAttempted.Wait(
            TimeSpan.FromSeconds(5),
            cancellationToken));
        Assert.False(sharedLock.SecondAcquireEntered.Wait(
            TimeSpan.FromMilliseconds(250),
            cancellationToken));

        releaseRotation.Set();
        await Task.WhenAll(first, second).WaitAsync(
            TimeSpan.FromSeconds(10),
            cancellationToken);

        Assert.True(sharedLock.SecondAcquireEntered.IsSet);
    }

    [Fact]
    public async Task RotationWaitsUntilSigningAndOutputComplete()
    {
        using var directory = new TemporaryDirectory("nofarma-qa-signing-lock");
        string privateKeyPath = Path.Combine(directory.Path, "qa-signing-key.bin");
        string publicKeyPath = Path.Combine(directory.Path, "qa-public.spki.b64");
        string licencePath = Path.Combine(directory.Path, "issued.nofarma-license");
        var protector = new TestProtector();
        var initialStore = new QaKeyStore(privateKeyPath, protector);
        initialStore.Provision(publicKeyPath, rotate: false, TextReader.Null);
        using var signingEntered = new ManualResetEventSlim();
        using var finishSigning = new ManualResetEventSlim();
        using var sharedLock = new BlockingKeyStoreLock();
        var signingStore = new QaKeyStore(
            privateKeyPath,
            protector,
            keyStoreLock: sharedLock);
        var rotationStore = new QaKeyStore(
            privateKeyPath,
            protector,
            keyStoreLock: sharedLock);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Task signing = Task.Run(() => signingStore.UseSigningKey(key =>
        {
            signingEntered.Set();
            if (!finishSigning.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("The test signing operation was not released.");
            }

            var issuer = new QaLicenseIssuer(
                key,
                new FixedTimeProvider(IssuedAt),
                () => LicenseId);
            issuer.IssueToFile(
                ActivationRequest(LicenseBuildChannel.Qa),
                LicensePlan.Monthly,
                From,
                Until,
                licencePath);
            Assert.False(sharedLock.SecondAcquireEntered.IsSet);
        }), cancellationToken);
        Assert.True(signingEntered.Wait(TimeSpan.FromSeconds(5), cancellationToken));
        Task rotation = Task.Run(() => rotationStore.Provision(
            publicKeyPath,
            rotate: true,
            new StringReader("ROTATE-QA-KEY")), cancellationToken);
        Assert.True(sharedLock.SecondAcquireAttempted.Wait(
            TimeSpan.FromSeconds(5),
            cancellationToken));
        Assert.False(sharedLock.SecondAcquireEntered.Wait(
            TimeSpan.FromMilliseconds(250),
            cancellationToken));

        finishSigning.Set();
        await Task.WhenAll(signing, rotation).WaitAsync(
            TimeSpan.FromSeconds(10),
            cancellationToken);

        Assert.True(File.Exists(licencePath));
        Assert.NotEmpty(File.ReadAllBytes(licencePath));
        Assert.True(sharedLock.SecondAcquireEntered.IsSet);
    }

    [Fact]
    public void ReentrantPublicKeyStoreOperationFailsClosed()
    {
        using var directory = new TemporaryDirectory("nofarma-qa-key-reentrant");
        string privateKeyPath = Path.Combine(directory.Path, "qa-signing-key.bin");
        string publicKeyPath = Path.Combine(directory.Path, "qa-public.spki.b64");
        var protector = new TestProtector();
        var initialStore = new QaKeyStore(privateKeyPath, protector);
        initialStore.Provision(publicKeyPath, rotate: false, TextReader.Null);
        var reentrantFault = new ReenterAfterPrivateCommit();
        var store = new QaKeyStore(privateKeyPath, protector, reentrantFault);
        reentrantFault.Reenter = () =>
        {
            _ = store.GetPublicKey();
        };

        QaIssuerException error = Assert.Throws<QaIssuerException>(() => store.Provision(
            publicKeyPath,
            rotate: true,
            new StringReader("ROTATE-QA-KEY")));

        Assert.Equal("QA_ROTATION_FAILED", error.Code);
        QaIssuerException reentrancy = Assert.IsType<QaIssuerException>(
            error.InnerException);
        Assert.Equal("QA_KEY_LOCK_REENTRANCY", reentrancy.Code);
    }

    [Fact]
    public void TransientInvisibleManifestCollisionIsRetried()
    {
        using var directory = new TemporaryDirectory(
            "nofarma-qa-manifest-transient");
        string privateKeyPath = Path.Combine(directory.Path, "qa-signing-key.bin");
        string publicKeyPath = Path.Combine(directory.Path, "qa-public.spki.b64");
        var protector = new TestProtector();
        var initialStore = new QaKeyStore(privateKeyPath, protector);
        initialStore.Provision(publicKeyPath, rotate: false, TextReader.Null);
        byte[] previousPublicKey = initialStore.GetPublicKey().ToArray();
        var fault = new ManifestTemporaryCreateFault(
            failures: 1,
            ErrorFileExistsHResult,
            createVisibleArtifact: false);
        var store = new QaKeyStore(privateKeyPath, protector, fault);

        store.Provision(
            publicKeyPath,
            rotate: true,
            new StringReader("ROTATE-QA-KEY"));

        Assert.Equal(2, fault.Attempts);
        Assert.False(CryptographicOperations.FixedTimeEquals(
            previousPublicKey,
            store.GetPublicKey().Span));
        AssertNoRecoveryArtifacts(privateKeyPath, publicKeyPath);
    }

    [Fact]
    public void PersistentInvisibleManifestCollisionFailsRecoveryRequired()
    {
        using var directory = new TemporaryDirectory(
            "nofarma-qa-manifest-persistent");
        string privateKeyPath = Path.Combine(directory.Path, "qa-signing-key.bin");
        string publicKeyPath = Path.Combine(directory.Path, "qa-public.spki.b64");
        var protector = new TestProtector();
        var initialStore = new QaKeyStore(privateKeyPath, protector);
        initialStore.Provision(publicKeyPath, rotate: false, TextReader.Null);
        var fault = new ManifestTemporaryCreateFault(
            failures: int.MaxValue,
            ErrorAlreadyExistsHResult,
            createVisibleArtifact: false);
        var store = new QaKeyStore(privateKeyPath, protector, fault);

        QaIssuerException error = Assert.Throws<QaIssuerException>(() =>
            store.Provision(
                publicKeyPath,
                rotate: true,
                new StringReader("ROTATE-QA-KEY")));

        Assert.Equal("QA_ROTATION_RECOVERY_REQUIRED", error.Code);
        Assert.Equal(20, fault.Attempts);
        Assert.Same(fault.LastException, error.InnerException);
    }

    [Fact]
    public void ObservableManifestCollisionPreservesArtifactAndFailsClosed()
    {
        using var directory = new TemporaryDirectory(
            "nofarma-qa-manifest-observable");
        string privateKeyPath = Path.Combine(directory.Path, "qa-signing-key.bin");
        string publicKeyPath = Path.Combine(directory.Path, "qa-public.spki.b64");
        var protector = new TestProtector();
        var initialStore = new QaKeyStore(privateKeyPath, protector);
        initialStore.Provision(publicKeyPath, rotate: false, TextReader.Null);
        var fault = new ManifestTemporaryCreateFault(
            failures: 1,
            ErrorFileExistsHResult,
            createVisibleArtifact: true);
        var store = new QaKeyStore(privateKeyPath, protector, fault);

        QaIssuerException error = Assert.Throws<QaIssuerException>(() =>
            store.Provision(
                publicKeyPath,
                rotate: true,
                new StringReader("ROTATE-QA-KEY")));

        Assert.Equal("QA_ROTATION_RECOVERY_REQUIRED", error.Code);
        Assert.Equal(1, fault.Attempts);
        Assert.Same(fault.LastException, error.InnerException);
        Assert.Equal(
            ManifestTemporaryCreateFault.Sentinel,
            File.ReadAllText(fault.ManifestTemporaryPath!));
    }

    [Theory]
    [InlineData(AccessDeniedHResult)]
    [InlineData(DiskFullHResult)]
    public void NonCollisionManifestIoFailureIsNotRetried(int hResult)
    {
        using var directory = new TemporaryDirectory(
            "nofarma-qa-manifest-io-failure");
        string privateKeyPath = Path.Combine(directory.Path, "qa-signing-key.bin");
        string publicKeyPath = Path.Combine(directory.Path, "qa-public.spki.b64");
        var protector = new TestProtector();
        var initialStore = new QaKeyStore(privateKeyPath, protector);
        initialStore.Provision(publicKeyPath, rotate: false, TextReader.Null);
        var fault = new ManifestTemporaryCreateFault(
            failures: 1,
            hResult,
            createVisibleArtifact: false);
        var store = new QaKeyStore(privateKeyPath, protector, fault);

        QaIssuerException error = Assert.Throws<QaIssuerException>(() =>
            store.Provision(
                publicKeyPath,
                rotate: true,
                new StringReader("ROTATE-QA-KEY")));

        Assert.Equal("QA_ROTATION_FAILED", error.Code);
        Assert.Equal(1, fault.Attempts);
        IOException failure = Assert.IsType<IOException>(error.InnerException);
        Assert.Equal(hResult, failure.HResult);
    }

    [Fact]
    public void RecoveryFailurePreservesTheOwnedPrivateBackup()
    {
        using var directory = new TemporaryDirectory("nofarma-qa-recovery-backup");
        string privateKeyPath = Path.Combine(directory.Path, "qa-signing-key.bin");
        string publicKeyPath = Path.Combine(directory.Path, "qa-public.spki.b64");
        var protector = new TestProtector();
        var initialStore = new QaKeyStore(privateKeyPath, protector);
        initialStore.Provision(publicKeyPath, rotate: false, TextReader.Null);
        byte[] originalProtectedKey = File.ReadAllBytes(privateKeyPath);
        var failingStore = new QaKeyStore(
            privateKeyPath,
            protector,
            new FailBeforePublicCommitAndRollback());

        QaIssuerException error = Assert.Throws<QaIssuerException>(() =>
            failingStore.Provision(
                publicKeyPath,
                rotate: true,
                new StringReader("ROTATE-QA-KEY")));

        Assert.Equal("QA_ROTATION_RECOVERY_REQUIRED", error.Code);
        string backupPath = string.Concat(privateKeyPath, ".rotation.bak");
        Assert.True(
            File.Exists(backupPath),
            string.Join(", ", Directory.EnumerateFiles(directory.Path)));
        Assert.Equal(originalProtectedKey, File.ReadAllBytes(backupPath));
    }

    [Fact]
    public void ProvisionChecksPrivatePathSecurityBeforeCreatingAKey()
    {
        using var directory = new TemporaryDirectory("nofarma-qa-path-contract");
        string privateKeyPath = Path.Combine(directory.Path, "qa-signing-key.bin");
        string publicKeyPath = Path.Combine(directory.Path, "qa-public.spki.b64");
        var pathSecurity = new RejectPrivatePathSecurity(privateKeyPath);
        var store = new QaKeyStore(
            privateKeyPath,
            new TestProtector(),
            pathSecurity: pathSecurity);

        QaIssuerException error = Assert.Throws<QaIssuerException>(() =>
            store.Provision(publicKeyPath, rotate: false, TextReader.Null));

        Assert.Equal("QA_PATH_REPARSE_POINT", error.Code);
        Assert.True(pathSecurity.PrivatePathChecked);
        Assert.False(File.Exists(privateKeyPath));
        Assert.False(File.Exists(publicKeyPath));
    }

    [Fact]
    public void ProvisionRestrictsTheProtectedPrivateKeyAcl()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        AssertProtectedPrivateKeyAcl();
    }

    [Fact]
    public void RotationPreservesBackupWhenCleanupPathCannotBeValidated()
    {
        using var directory = new TemporaryDirectory("nofarma-qa-cleanup-path");
        string privateKeyPath = Path.Combine(directory.Path, "qa-signing-key.bin");
        string publicKeyPath = Path.Combine(directory.Path, "qa-public.spki.b64");
        var protector = new TestProtector();
        var initialStore = new QaKeyStore(privateKeyPath, protector);
        initialStore.Provision(publicKeyPath, rotate: false, TextReader.Null);
        var guardedStore = new QaKeyStore(
            privateKeyPath,
            protector,
            pathSecurity: new RejectBackupCleanupPathSecurity());

        QaIssuerException error = Assert.Throws<QaIssuerException>(() =>
            guardedStore.Provision(
                publicKeyPath,
                rotate: true,
                new StringReader("ROTATE-QA-KEY")));

        Assert.Equal("QA_ROTATION_RECOVERY_REQUIRED", error.Code);
        Assert.NotEmpty(Directory.EnumerateFiles(directory.Path, "*.bak"));
    }

    [SupportedOSPlatform("windows")]
    private static void AssertProtectedPrivateKeyAcl()
    {
        using var directory = new TemporaryDirectory("nofarma-qa-private-acl");
        string privateDirectory = Path.Combine(directory.Path, "issuer");
        string privateKeyPath = Path.Combine(privateDirectory, "qa-signing-key.bin");
        string publicKeyPath = Path.Combine(directory.Path, "qa-public.spki.b64");
        var store = new QaKeyStore(privateKeyPath, new TestProtector());

        store.Provision(publicKeyPath, rotate: false, TextReader.Null);

        FileSecurity security = new FileInfo(privateKeyPath).GetAccessControl();
        Assert.True(security.AreAccessRulesProtected);
        var broadPrincipals = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            new SecurityIdentifier(WellKnownSidType.WorldSid, null).Value,
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null).Value,
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null).Value
        };
        AuthorizationRuleCollection rules = security.GetAccessRules(
            includeExplicit: true,
            includeInherited: true,
            typeof(SecurityIdentifier));
        Assert.DoesNotContain(
            rules.Cast<FileSystemAccessRule>(),
            rule => rule.AccessControlType == AccessControlType.Allow
                && broadPrincipals.Contains(rule.IdentityReference.Value));
    }

    [Fact]
    public void TemporaryDirectoryCleanupRethrowsPersistentIoFailure()
    {
        int attempts = 0;

        IOException error = Assert.Throws<IOException>(() =>
            TemporaryDirectory.DeleteWithRetry(
                () =>
                {
                    attempts++;
                    throw new IOException("Persistent cleanup failure.");
                },
                _ => { }));

        Assert.Equal(20, attempts);
        Assert.Equal("Persistent cleanup failure.", error.Message);
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
        Assert.Equal(
            store.GetPublicKey().ToArray(),
            publicKey.ExportSubjectPublicKeyInfo());
    }

    [Fact]
    public void KeyStoreClearsExportedPrivateParametersAfterValidation()
    {
        using var directory = new TemporaryDirectory("nofarma-qa-zero-store");
        string privateKeyPath = Path.Combine(directory.Path, "qa-signing-key.bin");
        string publicKeyPath = Path.Combine(directory.Path, "qa-public.spki.b64");
        var exporter = new RecordingPrivateParametersExporter();
        var store = new QaKeyStore(
            privateKeyPath,
            new TestProtector(),
            parametersExporter: exporter);
        store.Provision(publicKeyPath, rotate: false, TextReader.Null);

        store.UseSigningKey(_ => { });

        Assert.NotNull(exporter.ExportedPrivateBytes);
        Assert.All(exporter.ExportedPrivateBytes, value => Assert.Equal(0, value));
    }

    [Fact]
    public void IssuerClearsExportedPrivateParametersAfterValidation()
    {
        using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var exporter = new RecordingPrivateParametersExporter();

        _ = new QaLicenseIssuer(
            signingKey,
            new FixedTimeProvider(IssuedAt),
            () => LicenseId,
            exporter);

        Assert.NotNull(exporter.ExportedPrivateBytes);
        Assert.All(exporter.ExportedPrivateBytes, value => Assert.Equal(0, value));
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
    public void ValidatedCommercialCopyDoesNotChangeWhenTheSourceIsReplaced()
    {
        using var directory = new TemporaryDirectory("nofarma-commercial-snapshot");
        using ECDsa qaKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa commercialKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string qaPath = Path.Combine(directory.Path, "qa.spki.b64");
        string commercialPath = Path.Combine(directory.Path, "commercial.spki.b64");
        string validatedPath = Path.Combine(directory.Path, "validated.spki.b64");
        string qaBase64 = Convert.ToBase64String(qaKey.ExportSubjectPublicKeyInfo());
        byte[] expectedCommercial = commercialKey.ExportSubjectPublicKeyInfo();
        File.WriteAllText(qaPath, qaBase64);
        File.WriteAllText(commercialPath, Convert.ToBase64String(expectedCommercial));

        LicenseChannelKeyValidation result =
            QaPublicKeyValidator.ValidateCommercialToFile(
                qaPath,
                commercialPath,
                validatedPath);
        File.WriteAllText(commercialPath, qaBase64);

        Assert.True(result.IsValid);
        Assert.Equal(expectedCommercial, Convert.FromBase64String(
            File.ReadAllText(validatedPath)));
        Assert.True(QaPublicKeyValidator.ValidateCommercial(
            qaPath,
            validatedPath).IsValid);
        if (OperatingSystem.IsWindows())
        {
            Assert.True(new FileInfo(validatedPath)
                .GetAccessControl()
                .AreAccessRulesProtected);
        }
    }

    [Fact]
    public void ValidatedCommercialCopyPreservesAnExistingVictim()
    {
        using var directory = new TemporaryDirectory("nofarma-commercial-victim");
        using ECDsa qaKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa commercialKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string qaPath = Path.Combine(directory.Path, "qa.spki.b64");
        string commercialPath = Path.Combine(directory.Path, "commercial.spki.b64");
        string victimPath = Path.Combine(directory.Path, "victim.bin");
        File.WriteAllText(qaPath, Convert.ToBase64String(
            qaKey.ExportSubjectPublicKeyInfo()));
        File.WriteAllText(commercialPath, Convert.ToBase64String(
            commercialKey.ExportSubjectPublicKeyInfo()));
        byte[] victim = Encoding.UTF8.GetBytes("preserve-existing-victim");
        File.WriteAllBytes(victimPath, victim);

        LicenseChannelKeyValidation result =
            QaPublicKeyValidator.ValidateCommercialToFile(
                qaPath,
                commercialPath,
                victimPath);

        Assert.False(result.IsValid);
        Assert.Equal(victim, File.ReadAllBytes(victimPath));
    }

    [Fact]
    public void ValidatedCommercialCopyCannotReplaceTheProtectedPrivateKey()
    {
        using var directory = new TemporaryDirectory("nofarma-commercial-private");
        using ECDsa commercialKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string privateKeyPath = Path.Combine(directory.Path, "qa-signing-key.bin");
        string qaPath = Path.Combine(directory.Path, "qa.spki.b64");
        string commercialPath = Path.Combine(directory.Path, "commercial.spki.b64");
        var store = new QaKeyStore(privateKeyPath, new TestProtector());
        store.Provision(qaPath, rotate: false, TextReader.Null);
        byte[] protectedPrivateKey = File.ReadAllBytes(privateKeyPath);
        File.WriteAllText(commercialPath, Convert.ToBase64String(
            commercialKey.ExportSubjectPublicKeyInfo()));

        LicenseChannelKeyValidation result =
            QaPublicKeyValidator.ValidateCommercialToFile(
                qaPath,
                commercialPath,
                privateKeyPath);

        Assert.False(result.IsValid);
        Assert.Equal(protectedPrivateKey, File.ReadAllBytes(privateKeyPath));
    }

    [Fact]
    public void PublicKeyGateContainsPlatformFailureAsQaValidationCode()
    {
        using var directory = new TemporaryDirectory("nofarma-key-platform-failure");
        string qaPath = Path.Combine(directory.Path, "qa.spki.b64");
        string commercialPath = Path.Combine(directory.Path, "commercial.spki.b64");
        File.WriteAllText(qaPath, Convert.ToBase64String([1]));
        File.WriteAllText(commercialPath, Convert.ToBase64String([2]));

        LicenseChannelKeyValidation result = QaPublicKeyValidator.ValidateCommercial(
            qaPath,
            commercialPath,
            new AlwaysFailingPublicKeyDecoder(
                new PlatformNotSupportedException("platform-detail")));

        Assert.Equal("NFLC003", result.Code);
        Assert.DoesNotContain("platform-detail", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicKeyGateContainsEcPointFailureAsCommercialValidationCode()
    {
        using var directory = new TemporaryDirectory("nofarma-key-point-failure");
        using ECDsa qaKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string qaPath = Path.Combine(directory.Path, "qa.spki.b64");
        string commercialPath = Path.Combine(directory.Path, "commercial.spki.b64");
        File.WriteAllText(
            qaPath,
            Convert.ToBase64String(qaKey.ExportSubjectPublicKeyInfo()));
        File.WriteAllText(commercialPath, Convert.ToBase64String([2]));

        LicenseChannelKeyValidation result = QaPublicKeyValidator.ValidateCommercial(
            qaPath,
            commercialPath,
            new FailAfterOnePublicKeyDecoder(
                new CryptographicException("point-detail")));

        Assert.Equal("NFLC004", result.Code);
        Assert.DoesNotContain("point-detail", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryDoesNotContainPrivateLicensingMaterial()
    {
        string repoRoot = FindRepoRoot();
        string qaPublicKeyPath = Path.Combine(
            repoRoot,
            "build",
            "keys",
            "nofarma-qa-public.spki.b64");
        string[] trackedPaths = TrackedPaths(repoRoot);
        string[] forbiddenNameFragments =
        [
            "private",
            "qa-signing-key"
        ];
        var forbiddenExtensions = new HashSet<string>(
            [".p8", ".pfx", ".p12", ".pem", ".key", ".nofarma-license", ".nofarma-request"],
            StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            trackedPaths,
            path => forbiddenNameFragments.Any(fragment =>
                    path.Contains(
                        fragment,
                        StringComparison.OrdinalIgnoreCase))
                || forbiddenExtensions.Contains(Path.GetExtension(path)));

        Assert.DoesNotContain(
            trackedPaths,
            path => ContainsPrivateKeyMaterial(Path.Combine(repoRoot, path)));
        Assert.True(File.Exists(qaPublicKeyPath));
        string publicText = File.ReadAllText(qaPublicKeyPath);
        Assert.DoesNotContain("PRIVATE KEY", publicText, StringComparison.Ordinal);
        byte[] publicSpki = Convert.FromBase64String(publicText);
        using ECDsa publicKey = ECDsa.Create();
        publicKey.ImportSubjectPublicKeyInfo(publicSpki, out int bytesRead);
        Assert.Equal(publicSpki.Length, bytesRead);
        Assert.Equal(256, publicKey.KeySize);
    }

    private static string[] TrackedPaths(string repoRoot)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("ls-files");
        startInfo.ArgumentList.Add("-z");
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not enumerate tracked files.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool ContainsPrivateKeyMaterial(string path)
    {
        byte[] contents = File.ReadAllBytes(path);
        try
        {
            string text = Encoding.UTF8.GetString(contents);
            string[] privatePemMarkers =
            [
                string.Concat("-----BEGIN ", "PRIVATE KEY-----"),
                string.Concat("-----BEGIN EC ", "PRIVATE KEY-----"),
                string.Concat("-----BEGIN RSA ", "PRIVATE KEY-----")
            ];
            if (privatePemMarkers.Any(marker =>
                    text.Contains(marker, StringComparison.Ordinal)))
            {
                return true;
            }

            if (CanImportPrivateKey(contents))
            {
                return true;
            }

            byte[] decoded;
            try
            {
                decoded = Convert.FromBase64String(text);
            }
            catch (FormatException)
            {
                return false;
            }

            try
            {
                return CanImportPrivateKey(decoded);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(decoded);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contents);
        }
    }

    private static bool CanImportPrivateKey(ReadOnlySpan<byte> candidate)
    {
        using ECDsa ecdsaPkcs8 = ECDsa.Create();
        using ECDsa ecdsaSec1 = ECDsa.Create();
        using RSA rsaPkcs8 = RSA.Create();
        using RSA rsaPkcs1 = RSA.Create();
        return CanImport(candidate, ecdsaPkcs8.ImportPkcs8PrivateKey)
            || CanImport(candidate, ecdsaSec1.ImportECPrivateKey)
            || CanImport(candidate, rsaPkcs8.ImportPkcs8PrivateKey)
            || CanImport(candidate, rsaPkcs1.ImportRSAPrivateKey);
    }

    private static bool CanImport(
        ReadOnlySpan<byte> candidate,
        PrivateKeyImporter importer)
    {
        try
        {
            importer(candidate, out int bytesRead);
            return bytesRead == candidate.Length;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private delegate void PrivateKeyImporter(
        ReadOnlySpan<byte> source,
        out int bytesRead);

    private static ProcessResult BuildCommercial(
        string repoRoot,
        string qaPublicKeyPath,
        string commercialPublicKeyPath,
        string issuerProjectPath)
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
        startInfo.ArgumentList.Add("-p:NofarmaLicenseChannel=Commercial");
        startInfo.ArgumentList.Add($"-p:NofarmaQaPublicKeyPath={qaPublicKeyPath}");
        startInfo.ArgumentList.Add($"-p:NofarmaCommercialPublicKeyPath={commercialPublicKeyPath}");
        startInfo.ArgumentList.Add($"-p:NofarmaQaIssuerProjectPath={issuerProjectPath}");

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the Commercial build.");
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(
            process.ExitCode,
            string.Concat(standardOutput, Environment.NewLine, standardError));
    }

    private static void CopyCommercialBuildTree(string sourceRoot, string targetRoot)
    {
        foreach (string rootFile in new[]
                 {
                     ".editorconfig",
                     "Directory.Build.props",
                     "Directory.Packages.props",
                     "global.json"
                 })
        {
            File.Copy(
                Path.Combine(sourceRoot, rootFile),
                Path.Combine(targetRoot, rootFile));
        }

        foreach (string relativeDirectory in new[]
                 {
                     Path.Combine("src", "Nofarma.Application"),
                     Path.Combine("src", "Nofarma.Contracts"),
                     Path.Combine("src", "Nofarma.Desktop"),
                     Path.Combine("src", "Nofarma.Domain"),
                     Path.Combine("src", "Nofarma.Infrastructure"),
                     Path.Combine("tools", "Nofarma.Licensing.Qa")
                 })
        {
            CopySourceDirectory(
                Path.Combine(sourceRoot, relativeDirectory),
                Path.Combine(targetRoot, relativeDirectory));
        }
    }

    private static void CopySourceDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (string child in Directory.EnumerateDirectories(source))
        {
            string name = Path.GetFileName(child);
            if (name is "bin" or "obj")
            {
                continue;
            }

            CopySourceDirectory(child, Path.Combine(target, name));
        }
    }

    private static string CreateFakeIssuer(string directory)
    {
        string projectPath = Path.Combine(directory, "FakeIssuer.csproj");
        File.WriteAllText(
            projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>" +
            "<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>" +
            "</PropertyGroup></Project>");
        File.WriteAllText(
            Path.Combine(directory, "Program.cs"),
            "return 0;");
        return projectPath;
    }

    private static string Tail(string value, int maximumCharacters) =>
        value.Length <= maximumCharacters
            ? value
            : value[^maximumCharacters..];

    private static void AssertNoRecoveryArtifacts(
        string privateKeyPath,
        string publicKeyPath)
    {
        Assert.False(File.Exists(string.Concat(privateKeyPath, ".rotation.json")));
        Assert.False(File.Exists(string.Concat(privateKeyPath, ".rotation.bak")));
        Assert.False(File.Exists(string.Concat(publicKeyPath, ".rotation.bak")));
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

    private sealed class FailBeforePublicCommit : IQaKeyProvisioningFaultInjector
    {
        public void BeforePublicCommit() =>
            throw new IOException("Injected public commit failure.");

        public void BeforeRollback()
        {
        }
    }

    private sealed class FailBeforePublicCommitAndRollback
        : IQaKeyProvisioningFaultInjector
    {
        public void BeforePublicCommit() =>
            throw new IOException("Injected public commit failure.");

        public void BeforeRollback() =>
            throw new IOException("Injected rollback failure.");
    }

    private sealed class InterruptAfterPrivateCommit
        : IQaKeyProvisioningFaultInjector
    {
        public bool SimulatesProcessTermination => true;

        public void BeforePublicCommit()
        {
        }

        public void BeforeRollback()
        {
        }

        public void AfterPrivateCommit() =>
            throw new SimulatedProcessTermination();
    }

    private sealed class InterruptAfterPublicCommit
        : IQaKeyProvisioningFaultInjector
    {
        public bool SimulatesProcessTermination => true;

        public void BeforePublicCommit()
        {
        }

        public void BeforeRollback()
        {
        }

        public void AfterPublicCommit() =>
            throw new SimulatedProcessTermination();
    }

    private sealed class SimulatedProcessTermination : Exception;

    private sealed class FailAfterPublicCommit : IQaKeyProvisioningFaultInjector
    {
        public void BeforePublicCommit()
        {
        }

        public void BeforeRollback()
        {
        }

        public void AfterPublicCommit() =>
            throw new IOException("Injected post-public commit failure.");
    }

    private sealed class PauseAfterPrivateCommit(
        ManualResetEventSlim entered,
        ManualResetEventSlim release) : IQaKeyProvisioningFaultInjector
    {
        public void BeforePublicCommit()
        {
        }

        public void BeforeRollback()
        {
        }

        public void AfterPrivateCommit()
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("The test rotation was not released.");
            }
        }
    }

    private sealed class ReenterAfterPrivateCommit : IQaKeyProvisioningFaultInjector
    {
        public Action Reenter { get; set; } = () => { };

        public void BeforePublicCommit()
        {
        }

        public void BeforeRollback()
        {
        }

        public void AfterPrivateCommit() => Reenter();
    }

    private sealed class ManifestTemporaryCreateFault(
        int failures,
        int hResult,
        bool createVisibleArtifact) : IQaKeyProvisioningFaultInjector
    {
        public const string Sentinel = "preserve-manifest-temporary";

        public int Attempts { get; private set; }

        public IOException? LastException { get; private set; }

        public string? ManifestTemporaryPath { get; private set; }

        public void BeforeManifestTemporaryCreate(string path)
        {
            Attempts++;
            ManifestTemporaryPath = path;
            if (Attempts > failures)
            {
                return;
            }

            if (createVisibleArtifact)
            {
                File.WriteAllText(path, Sentinel);
            }

            LastException = new IOException(
                "Injected exclusive manifest creation collision.",
                hResult);
            throw LastException;
        }

        public void BeforePublicCommit()
        {
        }

        public void BeforeRollback()
        {
        }
    }

    private sealed class BlockingKeyStoreLock : IQaKeyStoreLock, IDisposable
    {
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private int _acquisitions;

        public ManualResetEventSlim SecondAcquireAttempted { get; } = new();

        public ManualResetEventSlim SecondAcquireEntered { get; } = new();

        public IDisposable Acquire()
        {
            int acquisition = Interlocked.Increment(ref _acquisitions);
            if (acquisition == 2)
            {
                SecondAcquireAttempted.Set();
            }

            _semaphore.Wait();
            if (acquisition == 2)
            {
                SecondAcquireEntered.Set();
            }

            return new CallbackDisposable(() => _semaphore.Release());
        }

        public void Dispose()
        {
            SecondAcquireAttempted.Dispose();
            SecondAcquireEntered.Dispose();
            _semaphore.Dispose();
        }
    }

    private sealed class CallbackDisposable(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    private sealed class RecordingPrivateParametersExporter
        : IQaPrivateKeyParametersExporter
    {
        public byte[]? ExportedPrivateBytes { get; private set; }

        public ECParameters Export(ECDsa key)
        {
            ECParameters parameters = key.ExportParameters(includePrivateParameters: true);
            ExportedPrivateBytes = parameters.D;
            return parameters;
        }
    }

    private sealed class RejectPrivatePathSecurity(string privateKeyPath)
        : IQaPathSecurity
    {
        public bool PrivatePathChecked { get; private set; }

        public void EnsureSafePath(string path)
        {
            if (string.Equals(path, privateKeyPath, StringComparison.OrdinalIgnoreCase))
            {
                PrivatePathChecked = true;
                throw new QaIssuerException(
                    "QA_PATH_REPARSE_POINT",
                    "Injected unsafe path.");
            }
        }

        public void ProtectPrivateFile(string path)
        {
        }
    }

    private sealed class RejectBackupCleanupPathSecurity : IQaPathSecurity
    {
        private int _backupChecks;

        public void EnsureSafePath(string path)
        {
            if (path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)
                && ++_backupChecks > 2)
            {
                throw new QaIssuerException(
                    "QA_PATH_REPARSE_POINT",
                    "Injected unsafe cleanup path.");
            }
        }

        public void ProtectPrivateFile(string path)
        {
        }
    }

    private sealed class AlwaysFailingPublicKeyDecoder(Exception exception)
        : IQaPublicKeyDecoder
    {
        public QaDecodedPublicKey Decode(ReadOnlyMemory<byte> subjectPublicKey) =>
            throw exception;
    }

    private sealed class FailAfterOnePublicKeyDecoder(Exception exception)
        : IQaPublicKeyDecoder
    {
        private int _calls;

        public QaDecodedPublicKey Decode(ReadOnlyMemory<byte> subjectPublicKey)
        {
            if (_calls++ > 0)
            {
                throw exception;
            }

            using ECDsa key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(subjectPublicKey.Span, out int bytesRead);
            ECParameters parameters = key.ExportParameters(includePrivateParameters: false);
            return new QaDecodedPublicKey(
                bytesRead,
                key.KeySize,
                parameters.Curve.Oid.Value,
                parameters.Q.X ?? Array.Empty<byte>(),
                parameters.Q.Y ?? Array.Empty<byte>(),
                key.ExportSubjectPublicKeyInfo());
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string _prefix;

        public TemporaryDirectory(string prefix)
        {
            _prefix = prefix;
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"{prefix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            string fullDirectory = System.IO.Path.GetFullPath(Path);
            string fullTemp = System.IO.Path.GetFullPath(
                    System.IO.Path.GetTempPath())
                .TrimEnd(
                    System.IO.Path.DirectorySeparatorChar,
                    System.IO.Path.AltDirectorySeparatorChar)
                + System.IO.Path.DirectorySeparatorChar;
            if (!fullDirectory.StartsWith(
                    fullTemp,
                    StringComparison.OrdinalIgnoreCase)
                || !System.IO.Path.GetFileName(fullDirectory).StartsWith(
                    _prefix,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The QA issuer test directory is not safe to remove.");
            }

            DeleteWithRetry(() =>
            {
                if (Directory.Exists(fullDirectory))
                {
                    Directory.Delete(fullDirectory, recursive: true);
                }
            });
        }

        internal static void DeleteWithRetry(
            Action delete,
            Action<TimeSpan>? delay = null)
        {
            ArgumentNullException.ThrowIfNull(delete);
            delay ??= Thread.Sleep;
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    delete();
                    return;
                }
                catch (IOException) when (attempt < 19)
                {
                    delay(TimeSpan.FromMilliseconds(50));
                }
            }
        }
    }
}
