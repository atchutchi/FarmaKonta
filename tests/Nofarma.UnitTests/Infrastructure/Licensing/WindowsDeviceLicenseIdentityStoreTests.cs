using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text;
using Nofarma.Domain.Common;
using Nofarma.Infrastructure.Licensing;
using Nofarma.UnitTests.TestSupport.Licensing;

namespace Nofarma.UnitTests.Infrastructure.Licensing;

[SupportedOSPlatform("windows")]
public sealed class WindowsDeviceLicenseIdentityStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"nofarma-device-licence-{Guid.NewGuid():N}");

    [Fact]
    public void CreatedIdentityIsStableAndPersistsOnlyProtectedMaterial()
    {
        var protector = new FakeProtector("machine-a");
        var firstStore = new WindowsDeviceLicenseIdentityStore(_directory, protector);
        var secondStore = new WindowsDeviceLicenseIdentityStore(_directory, protector);

        var first = firstStore.GetOrCreate(LicenseTestData.PharmacyId, LicenseTestData.DeviceId);
        var second = secondStore.GetOrCreate(LicenseTestData.PharmacyId, LicenseTestData.DeviceId);

        Assert.Equal(first, second);
        Assert.StartsWith("SHA256:", first.PublicKeyThumbprint);
        Assert.NotEmpty(first.PublicKeyThumbprint);
        Assert.True(File.Exists(Path.Combine(_directory, "device-license-key.bin")));
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void CopiedProtectedBlobCannotResolveOnAnotherProtector()
    {
        var first = new WindowsDeviceLicenseIdentityStore(
            _directory,
            new FakeProtector("machine-a"));
        var identity = first.GetOrCreate(LicenseTestData.PharmacyId, LicenseTestData.DeviceId);
        var copied = new WindowsDeviceLicenseIdentityStore(
            _directory,
            new FakeProtector("machine-b"));

        Assert.Throws<CryptographicException>(() =>
            copied.GetOrCreate(LicenseTestData.PharmacyId, LicenseTestData.DeviceId));
        Assert.NotEmpty(identity.PublicKeyThumbprint);
    }

    [Fact]
    public void CorruptedExistingIdentityIsNotSilentlyRegenerated()
    {
        var protector = new FakeProtector("machine-a");
        var store = new WindowsDeviceLicenseIdentityStore(_directory, protector);
        var original = store.GetOrCreate(LicenseTestData.PharmacyId, LicenseTestData.DeviceId);
        string path = Path.Combine(_directory, "device-license-key.bin");
        File.WriteAllBytes(path, [0x01, 0x02, 0x03]);

        Assert.Throws<CryptographicException>(() =>
            store.GetOrCreate(LicenseTestData.PharmacyId, LicenseTestData.DeviceId));
        Assert.Equal([0x01, 0x02, 0x03], File.ReadAllBytes(path));
        Assert.NotEmpty(original.PublicKeyThumbprint);
    }

    [Fact]
    public void ExistingIdentityCannotBeReboundToAnotherDevice()
    {
        var protector = new FakeProtector("machine-a");
        var store = new WindowsDeviceLicenseIdentityStore(_directory, protector);
        store.GetOrCreate(LicenseTestData.PharmacyId, LicenseTestData.DeviceId);
        var otherDeviceId = new EntityId(Guid.Parse("55555555-5555-5555-5555-555555555555"));

        Assert.Throws<CryptographicException>(() =>
            store.GetOrCreate(LicenseTestData.PharmacyId, otherDeviceId));
    }

    public void Dispose()
    {
        DeleteOwnedTemporaryDirectory(_directory, "nofarma-device-licence-");
    }

    internal static void DeleteOwnedTemporaryDirectory(string directory, string expectedPrefix)
    {
        string fullDirectory = Path.GetFullPath(directory);
        string fullTemp = Path.GetFullPath(Path.GetTempPath());
        string name = Path.GetFileName(fullDirectory);

        if (Directory.Exists(fullDirectory)
            && fullDirectory.StartsWith(fullTemp, StringComparison.OrdinalIgnoreCase)
            && name.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            Directory.Delete(fullDirectory, recursive: true);
        }
    }
}

internal sealed class FakeProtector(string machineId) : ILocalDataProtector
{
    private readonly byte[] _machinePrefix = Encoding.UTF8.GetBytes(machineId + ":");

    public byte[] Protect(ReadOnlySpan<byte> clear, ReadOnlySpan<byte> entropy)
    {
        byte[] result = new byte[_machinePrefix.Length + entropy.Length + clear.Length];
        _machinePrefix.CopyTo(result, 0);
        entropy.CopyTo(result.AsSpan(_machinePrefix.Length));
        clear.CopyTo(result.AsSpan(_machinePrefix.Length + entropy.Length));
        return result;
    }

    public byte[] Unprotect(ReadOnlySpan<byte> encrypted, ReadOnlySpan<byte> entropy)
    {
        int clearOffset = _machinePrefix.Length + entropy.Length;
        if (encrypted.Length < clearOffset
            || !encrypted[.._machinePrefix.Length].SequenceEqual(_machinePrefix)
            || !encrypted.Slice(_machinePrefix.Length, entropy.Length).SequenceEqual(entropy))
        {
            throw new CryptographicException("Protected data belongs to another protector.");
        }

        return encrypted[clearOffset..].ToArray();
    }
}
