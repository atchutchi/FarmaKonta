using System.Security.Cryptography;
using System.Runtime.Versioning;
using Nofarma.Infrastructure.Licensing;
using Nofarma.UnitTests.TestSupport.Licensing;

namespace Nofarma.UnitTests.Infrastructure.Licensing;

[SupportedOSPlatform("windows")]
public sealed class WindowsLicenseClockCheckpointTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"nofarma-licence-clock-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(-299, false)]
    [InlineData(-300, false)]
    [InlineData(-301, true)]
    public void DetectsRollbackBeyondFiveMinutes(int seconds, bool expected)
    {
        var checkpoint = CreateCheckpoint();
        checkpoint.CheckAndAdvance(LicenseTestData.Instant("2026-08-10T12:00:00Z"));

        var result = checkpoint.CheckAndAdvance(
            LicenseTestData.Instant("2026-08-10T12:00:00Z").PlusSeconds(seconds));

        Assert.Equal(expected, result.RollbackDetected);
    }

    [Fact]
    public void ToleratedRollbackDoesNotMoveCheckpointBackwards()
    {
        var checkpoint = CreateCheckpoint();
        checkpoint.CheckAndAdvance(LicenseTestData.Instant("2026-08-10T12:00:00Z"));
        checkpoint.CheckAndAdvance(LicenseTestData.Instant("2026-08-10T11:56:00Z"));

        var result = checkpoint.CheckAndAdvance(
            LicenseTestData.Instant("2026-08-10T11:54:59Z"));

        Assert.True(result.RollbackDetected);
    }

    [Fact]
    public void AdvancedCheckpointIsProtectedAndPersistsAcrossInstances()
    {
        var protector = new FakeProtector("machine-a");
        var first = new WindowsLicenseClockCheckpoint(_directory, protector);
        first.CheckAndAdvance(LicenseTestData.Instant("2026-08-10T12:00:00Z"));
        first.CheckAndAdvance(LicenseTestData.Instant("2026-08-10T13:00:00Z"));

        var second = new WindowsLicenseClockCheckpoint(_directory, protector);
        var result = second.CheckAndAdvance(
            LicenseTestData.Instant("2026-08-10T12:54:59Z"));

        Assert.True(result.RollbackDetected);
        Assert.True(File.Exists(Path.Combine(_directory, "license-clock.bin")));
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void CopiedCheckpointCannotResolveOnAnotherProtector()
    {
        new WindowsLicenseClockCheckpoint(_directory, new FakeProtector("machine-a"))
            .CheckAndAdvance(LicenseTestData.Instant("2026-08-10T12:00:00Z"));
        var copied = new WindowsLicenseClockCheckpoint(
            _directory,
            new FakeProtector("machine-b"));

        Assert.Throws<CryptographicException>(() =>
            copied.CheckAndAdvance(LicenseTestData.Instant("2026-08-10T12:01:00Z")));
    }

    [Fact]
    public void CorruptedCheckpointIsNotSilentlyReset()
    {
        var checkpoint = CreateCheckpoint();
        checkpoint.CheckAndAdvance(LicenseTestData.Instant("2026-08-10T12:00:00Z"));
        string path = Path.Combine(_directory, "license-clock.bin");
        File.WriteAllBytes(path, [0x01, 0x02, 0x03]);

        Assert.Throws<CryptographicException>(() =>
            checkpoint.CheckAndAdvance(LicenseTestData.Instant("2026-08-10T12:01:00Z")));
        Assert.Equal([0x01, 0x02, 0x03], File.ReadAllBytes(path));
    }

    public void Dispose()
    {
        WindowsDeviceLicenseIdentityStoreTests.DeleteOwnedTemporaryDirectory(
            _directory,
            "nofarma-licence-clock-");
    }

    private WindowsLicenseClockCheckpoint CreateCheckpoint() =>
        new(_directory, new FakeProtector("machine-a"));
}

internal static class UtcInstantTestExtensions
{
    internal static Nofarma.Domain.Common.UtcInstant PlusSeconds(
        this Nofarma.Domain.Common.UtcInstant instant,
        int seconds) =>
        Nofarma.Domain.Common.UtcInstant.From(instant.Value.AddSeconds(seconds));
}
