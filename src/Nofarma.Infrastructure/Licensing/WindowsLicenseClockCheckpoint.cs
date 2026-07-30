using System.Buffers.Binary;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Licensing;
using Nofarma.Domain.Common;

namespace Nofarma.Infrastructure.Licensing;

[SupportedOSPlatform("windows")]
public sealed class WindowsLicenseClockCheckpoint : ILicenseClockCheckpoint
{
    private const byte FormatVersion = 2;
    private const string PrimaryFileName = "license-clock.bin";
    private const string BackupFileName = "license-clock.backup.bin";
    private const int BindingHashLength = 32;
    private const int PayloadLength = 1 + BindingHashLength + sizeof(long);
    private const int MaximumProtectedPayloadBytes = 4 * 1024;
    private const int MaximumChannelBytes = 64;
    private const int MaximumThumbprintBytes = 256;
    private static readonly TimeSpan RollbackTolerance = TimeSpan.FromMinutes(5);

    private static readonly byte[] EntropyPrefix =
        Encoding.UTF8.GetBytes("Nofarma.Licensing.ClockCheckpoint.v2");

    private readonly string _primaryPath;
    private readonly string _backupPath;
    private readonly string _mutexName;
    private readonly ILocalDataProtector _protector;
    private readonly object _sync = new();

    public WindowsLicenseClockCheckpoint(
        string? baseDirectory = null,
        ILocalDataProtector? protector = null,
        LicenseBuildChannel channel = LicenseBuildChannel.Commercial)
    {
        string directory = baseDirectory ?? DefaultSecretsDirectory(channel);
        _primaryPath = Path.Combine(directory, PrimaryFileName);
        _backupPath = Path.Combine(directory, BackupFileName);
        _mutexName = CreateMutexName(_primaryPath);
        _protector = protector ?? new WindowsCurrentUserDataProtector();
    }

    public void Initialize(LicenseClockBinding binding, UtcInstant now)
    {
        WithInterprocessLock(() =>
        {
            CleanupTemporaries();
            bool primaryExists = File.Exists(_primaryPath);
            bool backupExists = File.Exists(_backupPath);
            if (!primaryExists && !backupExists)
            {
                WriteInitialPair(binding, now);
                return;
            }

            byte[] bindingHash = ComputeBindingHash(binding);
            try
            {
                UtcInstant maximum = now;
                if (primaryExists)
                {
                    maximum = Max(maximum, ReadExisting(_primaryPath, bindingHash));
                }

                if (backupExists)
                {
                    maximum = Max(maximum, ReadExisting(_backupPath, bindingHash));
                }

                if (!primaryExists)
                {
                    WriteNew(_primaryPath, bindingHash, maximum);
                }
                else
                {
                    Replace(_primaryPath, bindingHash, maximum);
                }

                if (!backupExists)
                {
                    WriteNew(_backupPath, bindingHash, maximum);
                }
                else
                {
                    Replace(_backupPath, bindingHash, maximum);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bindingHash);
            }
        });
    }

    public LicenseClockCheck CheckAndAdvance(LicenseClockBinding binding, UtcInstant now) =>
        WithInterprocessLock(() =>
        {
            CleanupTemporaries();
            if (!File.Exists(_primaryPath) || !File.Exists(_backupPath))
            {
                return new LicenseClockCheck(true);
            }

            byte[] bindingHash = ComputeBindingHash(binding);
            try
            {
                UtcInstant primary = ReadExisting(_primaryPath, bindingHash);
                UtcInstant backup = ReadExisting(_backupPath, bindingHash);
                UtcInstant maximum = Max(primary, backup);
                bool rollbackDetected = now.Value < maximum.Value - RollbackTolerance;

                if (now.Value > maximum.Value)
                {
                    Replace(_primaryPath, bindingHash, now);
                    Replace(_backupPath, bindingHash, now);
                }
                else if (primary != backup)
                {
                    if (primary.Value < backup.Value)
                    {
                        Replace(_primaryPath, bindingHash, maximum);
                    }
                    else
                    {
                        Replace(_backupPath, bindingHash, maximum);
                    }
                }

                return new LicenseClockCheck(rollbackDetected);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bindingHash);
            }
        });

    private void WriteInitialPair(LicenseClockBinding binding, UtcInstant now)
    {
        byte[] bindingHash = ComputeBindingHash(binding);
        try
        {
            WriteNew(_primaryPath, bindingHash, now);
            WriteNew(_backupPath, bindingHash, now);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bindingHash);
        }
    }

    private void WriteNew(string path, ReadOnlySpan<byte> bindingHash, UtcInstant instant)
    {
        byte[] protectedPayload = Protect(bindingHash, instant);
        try
        {
            if (!ProtectedFile.WriteNew(path, protectedPayload))
            {
                throw new IOException("The licence clock checkpoint already exists.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedPayload);
        }
    }

    private void Replace(string path, ReadOnlySpan<byte> bindingHash, UtcInstant instant)
    {
        byte[] protectedPayload = Protect(bindingHash, instant);
        try
        {
            ProtectedFile.Replace(path, protectedPayload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedPayload);
        }
    }

    private byte[] Protect(ReadOnlySpan<byte> bindingHash, UtcInstant instant)
    {
        byte[] clear = Serialize(bindingHash, instant);
        byte[] entropy = DeriveEntropy(bindingHash);
        try
        {
            return _protector.Protect(clear, entropy);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            CryptographicOperations.ZeroMemory(entropy);
        }
    }

    private UtcInstant ReadExisting(string path, ReadOnlySpan<byte> expectedBindingHash)
    {
        byte[] protectedPayload = ProtectedFile.ReadBounded(
            path,
            MaximumProtectedPayloadBytes,
            "The protected licence clock checkpoint is too large or empty.");
        byte[] entropy = DeriveEntropy(expectedBindingHash);
        byte[] clear = [];
        try
        {
            clear = _protector.Unprotect(protectedPayload, entropy);
            if (clear.Length != PayloadLength || clear[0] != FormatVersion)
            {
                throw new CryptographicException("The licence clock checkpoint format is invalid.");
            }

            ReadOnlySpan<byte> actualBindingHash = clear.AsSpan(1, BindingHashLength);
            if (!CryptographicOperations.FixedTimeEquals(actualBindingHash, expectedBindingHash))
            {
                throw new CryptographicException("The licence clock checkpoint binding is invalid.");
            }

            long utcTicks = BinaryPrimitives.ReadInt64LittleEndian(
                clear.AsSpan(1 + BindingHashLength));
            return UtcInstant.From(new DateTimeOffset(utcTicks, TimeSpan.Zero));
        }
        catch (CryptographicException)
        {
            throw;
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new CryptographicException(
                "The licence clock checkpoint value is invalid.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedPayload);
            CryptographicOperations.ZeroMemory(entropy);
            CryptographicOperations.ZeroMemory(clear);
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
                throw new IOException("Timed out waiting for the licence clock checkpoint lock.");
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

    private void WithInterprocessLock(Action action) =>
        WithInterprocessLock(() =>
        {
            action();
            return true;
        });

    private void CleanupTemporaries()
    {
        ProtectedFile.CleanupValidatedTemporaries(_primaryPath);
        ProtectedFile.CleanupValidatedTemporaries(_backupPath);
    }

    private static byte[] Serialize(ReadOnlySpan<byte> bindingHash, UtcInstant instant)
    {
        byte[] payload = new byte[PayloadLength];
        payload[0] = FormatVersion;
        bindingHash.CopyTo(payload.AsSpan(1, BindingHashLength));
        BinaryPrimitives.WriteInt64LittleEndian(
            payload.AsSpan(1 + BindingHashLength),
            instant.Value.UtcTicks);
        return payload;
    }

    private static byte[] ComputeBindingHash(LicenseClockBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        byte[] channel = GetBoundedUtf8(
            binding.Channel,
            MaximumChannelBytes,
            nameof(binding.Channel));
        byte[] thumbprint = GetBoundedUtf8(
            binding.DeviceKeyThumbprint,
            MaximumThumbprintBytes,
            nameof(binding.DeviceKeyThumbprint));
        byte[] bindingBytes = new byte[16 + 16 + sizeof(int) + channel.Length + sizeof(int) + thumbprint.Length];
        try
        {
            binding.PharmacyId.Value.TryWriteBytes(bindingBytes.AsSpan(0, 16));
            binding.DeviceId.Value.TryWriteBytes(bindingBytes.AsSpan(16, 16));
            BinaryPrimitives.WriteInt32LittleEndian(bindingBytes.AsSpan(32, sizeof(int)), channel.Length);
            channel.CopyTo(bindingBytes, 36);
            int thumbprintLengthOffset = 36 + channel.Length;
            BinaryPrimitives.WriteInt32LittleEndian(
                bindingBytes.AsSpan(thumbprintLengthOffset, sizeof(int)),
                thumbprint.Length);
            thumbprint.CopyTo(bindingBytes, thumbprintLengthOffset + sizeof(int));
            return SHA256.HashData(bindingBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(channel);
            CryptographicOperations.ZeroMemory(thumbprint);
            CryptographicOperations.ZeroMemory(bindingBytes);
        }
    }

    private static byte[] DeriveEntropy(ReadOnlySpan<byte> bindingHash)
    {
        byte[] material = new byte[EntropyPrefix.Length + bindingHash.Length];
        try
        {
            EntropyPrefix.CopyTo(material, 0);
            bindingHash.CopyTo(material.AsSpan(EntropyPrefix.Length));
            return SHA256.HashData(material);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }
    }

    private static byte[] GetBoundedUtf8(string value, int maximumBytes, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > maximumBytes)
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return bytes;
    }

    private static string CreateMutexName(string path)
    {
        byte[] normalizedPath = Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant());
        try
        {
            return $"Local\\Nofarma-LicenceClock-{Convert.ToHexString(SHA256.HashData(normalizedPath))}";
        }
        finally
        {
            CryptographicOperations.ZeroMemory(normalizedPath);
        }
    }

    private static UtcInstant Max(UtcInstant left, UtcInstant right) =>
        left.Value >= right.Value ? left : right;

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
