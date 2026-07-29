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
}
