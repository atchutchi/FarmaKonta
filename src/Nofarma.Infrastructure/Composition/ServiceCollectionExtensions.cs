using System.Runtime.Versioning;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Catalog;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Application.Identity.Authorization;
using Nofarma.Application.Identity.Setup;
using Nofarma.Application.Identity.Users;
using Nofarma.Application.Inventory;
using Nofarma.Application.Inventory.Import;
using Nofarma.Application.Licensing;
using Nofarma.Application.Purchasing;
using Nofarma.Application.Sales;
using Nofarma.Application.Supply;
using Nofarma.Infrastructure.Import;
using Nofarma.Infrastructure.Licensing;
using Nofarma.Infrastructure.Persistence;
using Nofarma.Infrastructure.Security;
using Nofarma.Infrastructure.Time;

namespace Nofarma.Infrastructure.Composition;

public static class ServiceCollectionExtensions
{
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddNofarmaLocalIdentity(
        this IServiceCollection services,
        string? databasePath = null,
        string? credentialSecretsDirectory = null,
        LicenseBuildChannel licenseChannel = LicenseBuildChannel.Unlicensed,
        IEnumerable<KeyValuePair<string, ReadOnlyMemory<byte>>>? trustedPublicKeys = null,
        string? licensingSecretsDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        databasePath ??= LocalDatabasePath.GetDefault();
        string licenseSecretsDirectory = licensingSecretsDirectory ?? Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(databasePath))!,
            "licensing-secrets");
        string connectionString = LocalDatabasePath.BuildConnectionString(databasePath);
        var options = new DbContextOptionsBuilder<NofarmaDbContext>()
            .UseSqlite(connectionString)
            .Options;

        services.AddSingleton(options);
        services.AddSingleton<ILocalIdentityStore, SqliteLocalIdentityStore>();
        services.AddSingleton<SqliteLocalAuthenticationStore>();
        services.AddSingleton<ILocalAuthenticationStore>(provider =>
            provider.GetRequiredService<SqliteLocalAuthenticationStore>());
        services.AddSingleton<ILocalRecoveryStore>(provider =>
            provider.GetRequiredService<SqliteLocalAuthenticationStore>());
        services.AddSingleton<ILocalProfileStore>(provider =>
            provider.GetRequiredService<SqliteLocalAuthenticationStore>());
        services.AddSingleton<ILocalUserAdministrationStore, SqliteUserAdministrationStore>();
        services.AddSingleton<ILocalApplicationInfoStore, SqliteLocalApplicationInfoStore>();
        services.AddSingleton<ICatalogStore, SqliteCatalogStore>();
        services.AddSingleton<IInventoryStore, SqliteInventoryStore>();
        services.AddSingleton<ISupplierStore, SqliteSupplierStore>();
        services.AddSingleton<IPurchaseStore, SqlitePurchaseStore>();
        services.AddSingleton<IInventoryImportStore, SqliteInventoryImportStore>();
        services.AddSingleton<ICashShiftStore, SqliteCashShiftStore>();
        services.AddSingleton<ISaleStore, SqliteSaleStore>();
        services.AddSingleton<IInventoryFileReader, InventoryFileReader>();
        services.AddSingleton<IInventoryImportErrorWriter, OpenXmlInventoryImportErrorWriter>();
        services.AddSingleton<ILicenseStore, SqliteLicenseStore>();
        services.AddSingleton<ILicenseContextStore, SqliteLicenseContextStore>();
        services.AddSingleton(new TrustedLicenseKeyRegistry(
            licenseChannel,
            trustedPublicKeys ?? Array.Empty<KeyValuePair<string, ReadOnlyMemory<byte>>>()));
        services.AddSingleton<ILicenseDocumentVerifier, EcdsaLicenseDocumentVerifier>();
        services.AddSingleton<IDeviceLicenseIdentityStore>(_ =>
            new WindowsDeviceLicenseIdentityStore(
                licenseSecretsDirectory,
                channel: licenseChannel));
        services.AddSingleton<ILicenseClockCheckpoint>(_ =>
            new WindowsLicenseClockCheckpoint(
                licenseSecretsDirectory,
                channel: licenseChannel));
        services.AddSingleton<ICredentialPepperStore>(
            new WindowsCredentialPepperStore(credentialSecretsDirectory));
        services.AddSingleton<ICredentialHasher, Pbkdf2CredentialHasher>();
        services.AddSingleton<IRecoveryCodeGenerator, SecureRecoveryCodeGenerator>();
        services.AddSingleton<IUtcClock, SystemUtcClock>();
        services.AddSingleton<LicenseService>();
        services.AddSingleton<ILicenseStatusProvider>(provider =>
            provider.GetRequiredService<LicenseService>());
        services.AddSingleton<ILicensedOperationPolicy, LicenseOperationPolicy>();
        services.AddSingleton<CurrentSession>();
        services.AddSingleton<AuthorizationService>();
        services.AddTransient<SetupService>();
        services.AddTransient<RecoveryService>();
        services.AddTransient<UserAdministrationService>();
        services.AddTransient<ProductService>();
        services.AddTransient<InventoryService>();
        services.AddTransient<InventoryQueryService>();
        services.AddTransient<SupplierService>();
        services.AddTransient<PurchaseService>();
        services.AddTransient<InventoryImportService>();
        services.AddTransient<CashShiftService>();
        services.AddTransient<SaleService>();
        services.AddSingleton(provider =>
        {
            ICredentialHasher hasher = provider.GetRequiredService<ICredentialHasher>();
            string timingSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            return new AuthenticationService(
                provider.GetRequiredService<ILocalAuthenticationStore>(),
                hasher,
                hasher.Hash(timingSecret),
                provider.GetRequiredService<CurrentSession>(),
                provider.GetRequiredService<IUtcClock>());
        });
        services.AddSingleton<LocalApplicationStartup>();
        return services;
    }
}
