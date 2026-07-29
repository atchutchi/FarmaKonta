using System.Runtime.Versioning;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Licensing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Licensing;
using Nofarma.Infrastructure.Composition;
using Nofarma.Infrastructure.Licensing;
using Nofarma.Infrastructure.Persistence;

namespace Nofarma.IntegrationTests.Licensing;

[SupportedOSPlatform("windows")]
public sealed class LicenseOperationPolicyTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"nofarma-license-policy-{Guid.NewGuid():N}");

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task NormalCompositionResolvesAndFailsClosedWithoutAConfiguredInstallation()
    {
        string databasePath = Path.Combine(_directory, "nofarma.db");
        using ServiceProvider provider = new ServiceCollection()
            .AddNofarmaLocalIdentity(databasePath, Path.Combine(_directory, "secrets"))
            .BuildServiceProvider(validateScopes: true);
        await using (var database = new NofarmaDbContext(
            provider.GetRequiredService<DbContextOptions<NofarmaDbContext>>()))
        {
            await database.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        ILicensedOperationPolicy policy = provider.GetRequiredService<ILicensedOperationPolicy>();
        var result = await policy.CanCreateAsync(TestContext.Current.CancellationToken);

        Assert.False(result.IsAllowed);
        Assert.Equal("INSTALLATION_REQUIRED", result.Code);
    }

    [Fact]
    public async Task ValidQaSignatureWithRealPersistenceIdentityAndCheckpointAllowsOperations()
    {
        await using LicenseDatabase database = await LicenseDatabase.CreateAsync();
        using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string secretsDirectory = Path.Combine(
            Path.GetDirectoryName(database.DatabasePath)!,
            "policy-secrets");
        var identityStore = new WindowsDeviceLicenseIdentityStore(
            secretsDirectory,
            channel: LicenseBuildChannel.Qa);
        var context = new LicenseContext(
            new EntityId(database.PharmacyId),
            new EntityId(database.PharmacyId),
            new EntityId(database.DeviceId));
        DeviceLicenseIdentity identity = identityStore.GetOrCreate(
            context.PharmacyId,
            context.DeviceId);
        var registry = new TrustedLicenseKeyRegistry(
            LicenseBuildChannel.Qa,
            new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal)
            {
                ["qa-ephemeral"] = signingKey.ExportSubjectPublicKeyInfo()
            });
        var service = new LicenseService(
            database.Store,
            database.ContextStore,
            new EcdsaLicenseDocumentVerifier(registry),
            identityStore,
            new WindowsLicenseClockCheckpoint(
                secretsDirectory,
                channel: LicenseBuildChannel.Qa),
            new FixedClock());

        await service.ImportAsync(
            new LicenseImportRequest(CreateSignedDocument(
                signingKey,
                context,
                identity.PublicKeyThumbprint)),
            TestContext.Current.CancellationToken);

        var policy = new LicenseOperationPolicy(service);
        var result = await policy.CanCreateAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsAllowed);
        Assert.Null(result.Code);
    }

    private static byte[] CreateSignedDocument(
        ECDsa signingKey,
        LicenseContext context,
        string thumbprint)
    {
        var unsigned = new SignedLicenseEnvelope(
            EcdsaLicenseDocumentVerifier.CurrentSchemaVersion,
            LicenseBuildChannel.Qa,
            "qa-ephemeral",
            EcdsaLicenseDocumentVerifier.EcdsaP256Sha256P1363Algorithm,
            Guid.NewGuid(),
            context.PharmacyId.Value,
            context.EstablishmentId.Value,
            context.DeviceId.Value,
            thumbprint,
            LicensePlan.Monthly,
            1,
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 31, 23, 59, 59, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 7, 23, 59, 59, TimeSpan.Zero),
            Array.Empty<int>(),
            ReadOnlyMemory<byte>.Empty);
        byte[] signature = signingKey.SignData(
            CanonicalLicenseJson.SerializePayload(unsigned),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return CanonicalLicenseJson.SerializeEnvelope(unsigned with { Signature = signature });
    }

    private sealed class FixedClock : IUtcClock
    {
        public UtcInstant GetCurrentInstant() => UtcInstant.From(
            new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero));
    }

    public ValueTask DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}
