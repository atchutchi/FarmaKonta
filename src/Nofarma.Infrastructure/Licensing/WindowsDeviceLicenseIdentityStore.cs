using System.Buffers.Binary;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Licensing;
using Nofarma.Domain.Common;

namespace Nofarma.Infrastructure.Licensing;

[SupportedOSPlatform("windows")]
public sealed class WindowsDeviceLicenseIdentityStore : IDeviceLicenseIdentityStore
{
    private const byte FormatVersion = 1;
    private const string FileName = "device-license-key.bin";
    private const string P256Oid = "1.2.840.10045.3.1.7";
    private const int MaximumProtectedPayloadBytes = 16 * 1024;

    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("Nofarma.Licensing.DeviceIdentity.v1");

    private readonly string _path;
    private readonly string _mutexName;
    private readonly ILocalDataProtector _protector;
    private readonly object _sync = new();

    public WindowsDeviceLicenseIdentityStore(
        string? baseDirectory = null,
        ILocalDataProtector? protector = null,
        LicenseBuildChannel channel = LicenseBuildChannel.Commercial)
    {
        _path = Path.Combine(
            baseDirectory ?? DefaultSecretsDirectory(channel),
            FileName);
        _mutexName = CreateMutexName(_path);
        _protector = protector ?? new WindowsCurrentUserDataProtector();
    }

    public DeviceLicenseIdentity GetOrCreate(EntityId pharmacyId, EntityId deviceId) =>
        WithInterprocessLock(() => GetOrCreateLocked(pharmacyId, deviceId));

    private DeviceLicenseIdentity GetOrCreateLocked(EntityId pharmacyId, EntityId deviceId)
    {
        ProtectedFile.CleanupValidatedTemporaries(_path);
        if (File.Exists(_path))
        {
            return ReadExisting(pharmacyId, deviceId);
        }

        byte[] clear = CreateIdentityPayload(pharmacyId, deviceId, out string thumbprint);
        byte[] protectedPayload = [];
        try
        {
            protectedPayload = _protector.Protect(clear, Entropy);
            if (!ProtectedFile.WriteNew(_path, protectedPayload))
            {
                return ReadExisting(pharmacyId, deviceId);
            }

            return new DeviceLicenseIdentity(pharmacyId, deviceId, thumbprint);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            CryptographicOperations.ZeroMemory(protectedPayload);
        }
    }

    private T WithInterprocessLock<T>(Func<T> action)
    {
        lock (_sync)
        {
            using var mutex = new Mutex(initiallyOwned: false, _mutexName);
            bool acquired;
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(30));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                throw new IOException("Timed out waiting for the device licence identity lock.");
            }

            try
            {
                return action();
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }
    }

    private DeviceLicenseIdentity ReadExisting(EntityId pharmacyId, EntityId deviceId)
    {
        byte[] protectedPayload = ProtectedFile.ReadBounded(
            _path,
            MaximumProtectedPayloadBytes,
            "The protected device licence identity is too large or empty.");
        byte[] clear = [];
        try
        {
            clear = _protector.Unprotect(protectedPayload, Entropy);
            return ParseIdentity(clear, pharmacyId, deviceId);
        }
        catch (CryptographicException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidOperationException
                or OverflowException)
        {
            throw new CryptographicException(
                "The device licence identity is invalid.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedPayload);
            CryptographicOperations.ZeroMemory(clear);
        }
    }

    private static byte[] CreateIdentityPayload(
        EntityId pharmacyId,
        EntityId deviceId,
        out string thumbprint)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] privateKey = key.ExportPkcs8PrivateKey();
        try
        {
            thumbprint = ComputeThumbprint(key);
            byte[] payload = new byte[1 + 16 + 16 + sizeof(int) + privateKey.Length];
            payload[0] = FormatVersion;
            pharmacyId.Value.TryWriteBytes(payload.AsSpan(1, 16));
            deviceId.Value.TryWriteBytes(payload.AsSpan(17, 16));
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(33, sizeof(int)), privateKey.Length);
            privateKey.CopyTo(payload, 37);
            return payload;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    private static DeviceLicenseIdentity ParseIdentity(
        ReadOnlySpan<byte> payload,
        EntityId expectedPharmacyId,
        EntityId expectedDeviceId)
    {
        const int headerLength = 1 + 16 + 16 + sizeof(int);
        if (payload.Length < headerLength || payload[0] != FormatVersion)
        {
            throw new CryptographicException("The device licence identity format is invalid.");
        }

        var pharmacyId = new EntityId(new Guid(payload.Slice(1, 16)));
        var deviceId = new EntityId(new Guid(payload.Slice(17, 16)));
        int privateKeyLength = BinaryPrimitives.ReadInt32LittleEndian(
            payload.Slice(33, sizeof(int)));
        if (privateKeyLength <= 0 || payload.Length != headerLength + privateKeyLength)
        {
            throw new CryptographicException("The device licence identity length is invalid.");
        }

        if (pharmacyId != expectedPharmacyId || deviceId != expectedDeviceId)
        {
            throw new CryptographicException(
                "The device licence identity belongs to another pharmacy or device.");
        }

        using ECDsa key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(payload[headerLength..], out int bytesRead);
        if (bytesRead != privateKeyLength
            || !string.Equals(
                key.ExportParameters(includePrivateParameters: false).Curve.Oid.Value,
                P256Oid,
                StringComparison.Ordinal))
        {
            throw new CryptographicException("The device licence key is not a valid P-256 key.");
        }

        return new DeviceLicenseIdentity(pharmacyId, deviceId, ComputeThumbprint(key));
    }

    private static string ComputeThumbprint(ECDsa key)
    {
        byte[] publicKey = key.ExportSubjectPublicKeyInfo();
        try
        {
            return $"SHA256:{Convert.ToHexString(SHA256.HashData(publicKey))}";
        }
        finally
        {
            CryptographicOperations.ZeroMemory(publicKey);
        }
    }

    private static string CreateMutexName(string path)
    {
        byte[] normalizedPath = Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant());
        try
        {
            return $"Local\\Nofarma-LicenceIdentity-{Convert.ToHexString(SHA256.HashData(normalizedPath))}";
        }
        finally
        {
            CryptographicOperations.ZeroMemory(normalizedPath);
        }
    }

    private static string DefaultSecretsDirectory(LicenseBuildChannel channel)
    {
        string applicationDirectory = channel switch
        {
            LicenseBuildChannel.Qa => "Nofarma-QA",
            LicenseBuildChannel.Commercial => "Nofarma",
            _ => throw new ArgumentOutOfRangeException(nameof(channel))
        };

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ABIPTOM",
            applicationDirectory,
            "secrets");
    }
}
