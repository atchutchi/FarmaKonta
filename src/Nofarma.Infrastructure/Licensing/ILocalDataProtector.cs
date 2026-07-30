using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Nofarma.Infrastructure.Licensing;

public interface ILocalDataProtector
{
    byte[] Protect(ReadOnlySpan<byte> clear, ReadOnlySpan<byte> entropy);

    byte[] Unprotect(ReadOnlySpan<byte> encrypted, ReadOnlySpan<byte> entropy);
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsCurrentUserDataProtector : ILocalDataProtector
{
    public byte[] Protect(ReadOnlySpan<byte> clear, ReadOnlySpan<byte> entropy)
    {
        byte[] clearCopy = clear.ToArray();
        byte[] entropyCopy = entropy.ToArray();
        try
        {
            return ProtectedData.Protect(
                clearCopy,
                entropyCopy,
                DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearCopy);
            CryptographicOperations.ZeroMemory(entropyCopy);
        }
    }

    public byte[] Unprotect(ReadOnlySpan<byte> encrypted, ReadOnlySpan<byte> entropy)
    {
        byte[] encryptedCopy = encrypted.ToArray();
        byte[] entropyCopy = entropy.ToArray();
        try
        {
            return ProtectedData.Unprotect(
                encryptedCopy,
                entropyCopy,
                DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptedCopy);
            CryptographicOperations.ZeroMemory(entropyCopy);
        }
    }
}

internal static class ProtectedFile
{
    internal static byte[] ReadBounded(
        string targetPath,
        int maximumBytes,
        string invalidMessage)
    {
        using var stream = new FileStream(
            targetPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        if (stream.Length < 1 || stream.Length > maximumBytes)
        {
            throw new CryptographicException(invalidMessage);
        }

        byte[] contents = new byte[checked((int)stream.Length)];
        stream.ReadExactly(contents);
        if (stream.ReadByte() >= 0)
        {
            CryptographicOperations.ZeroMemory(contents);
            throw new CryptographicException(invalidMessage);
        }

        return contents;
    }

    internal static void CleanupValidatedTemporaries(string targetPath)
    {
        string fullTargetPath = Path.GetFullPath(targetPath);
        string? directory = Path.GetDirectoryName(fullTargetPath);
        if (directory is null || !Directory.Exists(directory))
        {
            return;
        }

        string expectedPrefix = $".{Path.GetFileName(fullTargetPath)}.";
        foreach (string candidate in Directory.EnumerateFiles(
                     directory,
                     $"{expectedPrefix}*.tmp",
                     SearchOption.TopDirectoryOnly))
        {
            DeleteValidatedTemporary(candidate, directory, fullTargetPath);
        }
    }

    internal static bool WriteNew(string targetPath, ReadOnlySpan<byte> contents)
    {
        string directory = GetOrCreateDirectory(targetPath);
        string temporaryPath = CreateTemporaryPath(directory, targetPath);

        try
        {
            WriteTemporary(temporaryPath, contents);
            try
            {
                File.Move(temporaryPath, targetPath, overwrite: false);
                return true;
            }
            catch (IOException) when (File.Exists(targetPath))
            {
                return false;
            }
        }
        finally
        {
            DeleteValidatedTemporary(temporaryPath, directory, targetPath);
        }
    }

    internal static void Replace(string targetPath, ReadOnlySpan<byte> contents)
    {
        string directory = GetOrCreateDirectory(targetPath);
        string temporaryPath = CreateTemporaryPath(directory, targetPath);

        try
        {
            WriteTemporary(temporaryPath, contents);
            File.Replace(temporaryPath, targetPath, destinationBackupFileName: null);
        }
        finally
        {
            DeleteValidatedTemporary(temporaryPath, directory, targetPath);
        }
    }

    private static string GetOrCreateDirectory(string targetPath)
    {
        string fullTargetPath = Path.GetFullPath(targetPath);
        string? directory = Path.GetDirectoryName(fullTargetPath);
        if (directory is null)
        {
            throw new InvalidOperationException("The protected file path is invalid.");
        }

        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string CreateTemporaryPath(string directory, string targetPath) =>
        Path.Combine(
            directory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

    private static void WriteTemporary(string temporaryPath, ReadOnlySpan<byte> contents)
    {
        using var stream = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        stream.Write(contents);
        stream.Flush(flushToDisk: true);
    }

    private static void DeleteValidatedTemporary(
        string temporaryPath,
        string directory,
        string targetPath)
    {
        string fullTemporaryPath = Path.GetFullPath(temporaryPath);
        string fullDirectory = Path.GetFullPath(directory);
        string? temporaryDirectory = Path.GetDirectoryName(fullTemporaryPath);
        string expectedPrefix = $".{Path.GetFileName(targetPath)}.";
        string temporaryName = Path.GetFileName(fullTemporaryPath);

        bool isOwnedTemporary = string.Equals(
                temporaryDirectory,
                fullDirectory,
                StringComparison.OrdinalIgnoreCase)
            && temporaryName.StartsWith(expectedPrefix, StringComparison.Ordinal)
            && temporaryName.EndsWith(".tmp", StringComparison.Ordinal);

        if (isOwnedTemporary && File.Exists(fullTemporaryPath))
        {
            File.Delete(fullTemporaryPath);
        }
    }
}
