using System.Reflection;
using Nofarma.Infrastructure.Licensing;

namespace Nofarma.Desktop.Services;

public sealed class DesktopLicenseConfiguration
{
    private const string PublicKeyResourceName = "Nofarma.Desktop.LicensingPublicKey";
    private readonly IReadOnlyList<KeyValuePair<string, ReadOnlyMemory<byte>>> _trustedPublicKeys;

    private DesktopLicenseConfiguration(
        LicenseBuildChannel channel,
        string applicationDirectoryName,
        IReadOnlyList<KeyValuePair<string, ReadOnlyMemory<byte>>> trustedPublicKeys)
    {
        Channel = channel;
        ApplicationDirectoryName = applicationDirectoryName;
        _trustedPublicKeys = trustedPublicKeys;
    }

    public LicenseBuildChannel Channel { get; }

    public string ApplicationDirectoryName { get; }

    public bool IsQa => Channel == LicenseBuildChannel.Qa;

    public IReadOnlyList<KeyValuePair<string, ReadOnlyMemory<byte>>> TrustedPublicKeys =>
        _trustedPublicKeys;

    public string BaseDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ABIPTOM",
        ApplicationDirectoryName);

    public string CredentialSecretsDirectory => Path.Combine(BaseDirectory, "secrets");

    public string LicensingSecretsDirectory => Path.Combine(
        BaseDirectory,
        "licensing-secrets");

    public static DesktopLicenseConfiguration LoadCurrent()
    {
#if NOFARMA_LICENSE_QA
        return Create(LicenseBuildChannel.Qa, ReadEmbeddedPublicKey());
#elif NOFARMA_LICENSE_COMMERCIAL
        return Create(LicenseBuildChannel.Commercial, ReadEmbeddedPublicKey());
#else
        return Create(LicenseBuildChannel.Unlicensed, null);
#endif
    }

    public static DesktopLicenseConfiguration Create(
        LicenseBuildChannel channel,
        string? publicKeyBase64)
    {
        if (!Enum.IsDefined(channel))
        {
            throw new ArgumentOutOfRangeException(nameof(channel));
        }

        string? normalizedKey = string.IsNullOrWhiteSpace(publicKeyBase64)
            ? null
            : publicKeyBase64.Trim();
        if (channel == LicenseBuildChannel.Unlicensed && normalizedKey is not null)
        {
            throw new InvalidOperationException(
                "An unlicensed build cannot contain trusted public keys.");
        }

        if (channel == LicenseBuildChannel.Commercial && normalizedKey is null)
        {
            throw new InvalidOperationException(
                "The commercial public key is not provisioned.");
        }

        string applicationDirectory = channel == LicenseBuildChannel.Qa
            ? "Nofarma-QA"
            : "Nofarma";
        if (normalizedKey is null)
        {
            return new DesktopLicenseConfiguration(
                channel,
                applicationDirectory,
                []);
        }

        byte[] publicKey;
        try
        {
            publicKey = Convert.FromBase64String(normalizedKey);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "The embedded licensing public key is invalid.",
                exception);
        }

        string keyId = channel == LicenseBuildChannel.Qa
            ? "qa-2026-01"
            : "commercial-2026-01";
        return new DesktopLicenseConfiguration(
            channel,
            applicationDirectory,
            [new KeyValuePair<string, ReadOnlyMemory<byte>>(keyId, publicKey)]);
    }

    private static string? ReadEmbeddedPublicKey()
    {
        using Stream? stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(PublicKeyResourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
