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
    private const byte FormatVersion = 1;
    private const string FileName = "license-clock.bin";
    private const int PayloadLength = 1 + sizeof(long);
    private static readonly TimeSpan RollbackTolerance = TimeSpan.FromMinutes(5);

    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("Nofarma.Licensing.ClockCheckpoint.v1");

    private readonly string _path;
    private readonly ILocalDataProtector _protector;
    private readonly object _sync = new();

    public WindowsLicenseClockCheckpoint(
        string? baseDirectory = null,
        ILocalDataProtector? protector = null,
        LicenseBuildChannel channel = LicenseBuildChannel.Commercial)
    {
        _path = Path.Combine(
            baseDirectory ?? DefaultSecretsDirectory(channel),
            FileName);
        _protector = protector ?? new WindowsCurrentUserDataProtector();
    }

    public LicenseClockCheck CheckAndAdvance(UtcInstant now)
    {
        lock (_sync)
        {
            if (!File.Exists(_path))
            {
                if (TryCreate(now))
                {
                    return new LicenseClockCheck(false);
                }
            }

            UtcInstant checkpoint = ReadExisting();
            bool rollbackDetected = now.Value < checkpoint.Value - RollbackTolerance;
            if (now.Value > checkpoint.Value)
            {
                Replace(now);
            }

            return new LicenseClockCheck(rollbackDetected);
        }
    }

    private bool TryCreate(UtcInstant now)
    {
        byte[] clear = Serialize(now);
        byte[] protectedPayload = [];
        try
        {
            protectedPayload = _protector.Protect(clear, Entropy);
            return ProtectedFile.WriteNew(_path, protectedPayload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            CryptographicOperations.ZeroMemory(protectedPayload);
        }
    }

    private void Replace(UtcInstant now)
    {
        byte[] clear = Serialize(now);
        byte[] protectedPayload = [];
        try
        {
            protectedPayload = _protector.Protect(clear, Entropy);
            ProtectedFile.Replace(_path, protectedPayload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            CryptographicOperations.ZeroMemory(protectedPayload);
        }
    }

    private UtcInstant ReadExisting()
    {
        byte[] protectedPayload = File.ReadAllBytes(_path);
        byte[] clear = [];
        try
        {
            clear = _protector.Unprotect(protectedPayload, Entropy);
            if (clear.Length != PayloadLength || clear[0] != FormatVersion)
            {
                throw new CryptographicException("The licence clock checkpoint format is invalid.");
            }

            long utcTicks = BinaryPrimitives.ReadInt64LittleEndian(clear.AsSpan(1));
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
            CryptographicOperations.ZeroMemory(clear);
        }
    }

    private static byte[] Serialize(UtcInstant instant)
    {
        byte[] payload = new byte[PayloadLength];
        payload[0] = FormatVersion;
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(1), instant.Value.UtcTicks);
        return payload;
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
