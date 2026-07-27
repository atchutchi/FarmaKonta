using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Nofarma.Infrastructure.Security;

[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialPepperStore : ICredentialPepperStore
{
    private const int PepperSize = 32;
    private const string PepperFileName = "credential-pepper.bin";

    private static readonly byte[] OptionalEntropy =
        Encoding.UTF8.GetBytes("Nofarma.LocalIdentity.CredentialPepper.v1");

    private readonly string _pepperPath;

    public WindowsCredentialPepperStore(string? baseDirectory = null)
    {
        string directory = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ABIPTOM",
            "Nofarma",
            "secrets");

        _pepperPath = Path.Combine(directory, PepperFileName);
    }

    public byte[] GetOrCreate()
    {
        if (File.Exists(_pepperPath))
        {
            return Unprotect(File.ReadAllBytes(_pepperPath));
        }

        string? directory = Path.GetDirectoryName(_pepperPath);
        if (directory is null)
        {
            throw new InvalidOperationException("The credential secret path is invalid.");
        }

        Directory.CreateDirectory(directory);

        byte[] pepper = RandomNumberGenerator.GetBytes(PepperSize);
        byte[] protectedPepper = ProtectedData.Protect(
            pepper,
            OptionalEntropy,
            DataProtectionScope.CurrentUser);

        try
        {
            using var stream = new FileStream(
                _pepperPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            stream.Write(protectedPepper);

            return [.. pepper];
        }
        catch (IOException) when (File.Exists(_pepperPath))
        {
            return Unprotect(File.ReadAllBytes(_pepperPath));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pepper);
            CryptographicOperations.ZeroMemory(protectedPepper);
        }
    }

    private static byte[] Unprotect(byte[] protectedPepper)
    {
        try
        {
            byte[] pepper = ProtectedData.Unprotect(
                protectedPepper,
                OptionalEntropy,
                DataProtectionScope.CurrentUser);

            if (pepper.Length != PepperSize)
            {
                CryptographicOperations.ZeroMemory(pepper);
                throw new CryptographicException("The credential secret has an invalid length.");
            }

            return pepper;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedPepper);
        }
    }
}
