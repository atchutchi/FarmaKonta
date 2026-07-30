using System.Security.Cryptography;
using Nofarma.Desktop.Services;
using Nofarma.Infrastructure.Licensing;

namespace Nofarma.UnitTests.Desktop;

public sealed class DesktopLicenseConfigurationTests
{
    [Fact]
    public void UnlicensedUsesNormalLocalDirectoryAndContainsNoTrustedKey()
    {
        DesktopLicenseConfiguration configuration =
            DesktopLicenseConfiguration.Create(LicenseBuildChannel.Unlicensed, null);

        Assert.Equal("Nofarma", configuration.ApplicationDirectoryName);
        Assert.Empty(configuration.TrustedPublicKeys);
        Assert.False(configuration.IsQa);
    }

    [Fact]
    public void QaUsesSeparateDirectoryAndOnlyTheProvidedPublicKey()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] publicKey = key.ExportSubjectPublicKeyInfo();
        string encoded = Convert.ToBase64String(publicKey);

        DesktopLicenseConfiguration configuration =
            DesktopLicenseConfiguration.Create(LicenseBuildChannel.Qa, encoded);

        Assert.Equal("Nofarma-QA", configuration.ApplicationDirectoryName);
        Assert.True(configuration.IsQa);
        KeyValuePair<string, ReadOnlyMemory<byte>> trusted = Assert.Single(
            configuration.TrustedPublicKeys);
        Assert.Equal("qa-2026-01", trusted.Key);
        Assert.Equal(publicKey, trusted.Value.ToArray());
    }

    [Fact]
    public void CommercialNeverAcceptsMissingOrMalformedEmbeddedKeyAtRuntime()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DesktopLicenseConfiguration.Create(LicenseBuildChannel.Commercial, null));
        Assert.Throws<InvalidOperationException>(() =>
            DesktopLicenseConfiguration.Create(LicenseBuildChannel.Commercial, "not-base64"));
    }

    [Fact]
    public void UnlicensedRejectsAnyTrustedKeyMaterial()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string encoded = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

        Assert.Throws<InvalidOperationException>(() =>
            DesktopLicenseConfiguration.Create(LicenseBuildChannel.Unlicensed, encoded));
    }

    [Fact]
    public void CredentialSecretsFollowTheBuildChannelDirectoryWithoutBreakingCommercialCompatibility()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string encoded = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        DesktopLicenseConfiguration unlicensed =
            DesktopLicenseConfiguration.Create(LicenseBuildChannel.Unlicensed, null);
        DesktopLicenseConfiguration qa =
            DesktopLicenseConfiguration.Create(LicenseBuildChannel.Qa, encoded);
        DesktopLicenseConfiguration commercial =
            DesktopLicenseConfiguration.Create(LicenseBuildChannel.Commercial, encoded);

        Assert.Equal(
            Path.Combine(unlicensed.BaseDirectory, "secrets"),
            unlicensed.CredentialSecretsDirectory);
        Assert.Equal(
            Path.Combine(qa.BaseDirectory, "secrets"),
            qa.CredentialSecretsDirectory);
        Assert.Equal(
            Path.Combine(commercial.BaseDirectory, "secrets"),
            commercial.CredentialSecretsDirectory);
        Assert.NotEqual(
            unlicensed.CredentialSecretsDirectory,
            qa.CredentialSecretsDirectory);
        Assert.Equal(
            unlicensed.CredentialSecretsDirectory,
            commercial.CredentialSecretsDirectory);
        Assert.Equal(
            Path.Combine(qa.BaseDirectory, "licensing-secrets"),
            qa.LicensingSecretsDirectory);
        Assert.NotEqual(
            qa.CredentialSecretsDirectory,
            qa.LicensingSecretsDirectory);
    }
}
