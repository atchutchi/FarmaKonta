using System.Runtime.Versioning;
using System.Security.Cryptography;
using Nofarma.Application.Licensing;
using Nofarma.Domain.Common;
using Nofarma.Infrastructure.Licensing;
using Nofarma.UnitTests.TestSupport.Licensing;

namespace Nofarma.UnitTests.Infrastructure.Licensing;

[SupportedOSPlatform("windows")]
public sealed class WindowsLicenseClockCheckpointTests : IDisposable
{
    private static readonly LicenseClockBinding Binding = new(
        LicenseTestData.PharmacyId,
        LicenseTestData.DeviceId,
        "QA",
        "SHA256:DEVICE-TEST");

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"nofarma-licence-clock-{Guid.NewGuid():N}");

    [Fact]
    public void MissingCheckpointBlocksWithoutCreatingProtectedState()
    {
        var checkpoint = CreateCheckpoint();

        LicenseClockCheck result = checkpoint.CheckAndAdvance(
            Binding,
            LicenseTestData.Instant("2026-08-10T12:00:00Z"));

        Assert.True(result.RollbackDetected);
        Assert.False(File.Exists(Path.Combine(_directory, "license-clock.bin")));
        Assert.False(File.Exists(Path.Combine(_directory, "license-clock.backup.bin")));
    }

    [Theory]
    [InlineData(-299, false)]
    [InlineData(-300, false)]
    [InlineData(-301, true)]
    public void DetectsRollbackBeyondFiveMinutes(int seconds, bool expected)
    {
        var checkpoint = CreateInitializedCheckpoint();

        LicenseClockCheck result = checkpoint.CheckAndAdvance(
            Binding,
            LicenseTestData.Instant("2026-08-10T12:00:00Z").PlusSeconds(seconds));

        Assert.Equal(expected, result.RollbackDetected);
    }

    [Fact]
    public void ToleratedRollbackDoesNotMoveCheckpointBackwards()
    {
        var checkpoint = CreateInitializedCheckpoint();
        checkpoint.CheckAndAdvance(
            Binding,
            LicenseTestData.Instant("2026-08-10T11:56:00Z"));

        LicenseClockCheck result = checkpoint.CheckAndAdvance(
            Binding,
            LicenseTestData.Instant("2026-08-10T11:54:59Z"));

        Assert.True(result.RollbackDetected);
    }

    [Fact]
    public void AdvancedCheckpointIsProtectedAndPersistsAcrossInstances()
    {
        var protector = new FakeProtector("machine-a");
        var first = new WindowsLicenseClockCheckpoint(_directory, protector);
        first.Initialize(Binding, LicenseTestData.Instant("2026-08-10T12:00:00Z"));
        first.CheckAndAdvance(
            Binding,
            LicenseTestData.Instant("2026-08-10T13:00:00Z"));

        var second = new WindowsLicenseClockCheckpoint(_directory, protector);
        LicenseClockCheck result = second.CheckAndAdvance(
            Binding,
            LicenseTestData.Instant("2026-08-10T12:54:59Z"));

        Assert.True(result.RollbackDetected);
        Assert.True(File.Exists(Path.Combine(_directory, "license-clock.bin")));
        Assert.True(File.Exists(Path.Combine(_directory, "license-clock.backup.bin")));
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void DifferentBindingCannotReadCheckpoint(int changedField)
    {
        var checkpoint = CreateInitializedCheckpoint();
        LicenseClockBinding different = DifferentBinding(changedField);

        Assert.Throws<CryptographicException>(() =>
            checkpoint.CheckAndAdvance(
                different,
                LicenseTestData.Instant("2026-08-10T12:01:00Z")));
    }

    [Fact]
    public void ExistingCheckpointCannotBeInitializedForDifferentBinding()
    {
        var checkpoint = CreateInitializedCheckpoint();

        Assert.Throws<CryptographicException>(() => checkpoint.Initialize(
            DifferentBinding(changedField: 1),
            LicenseTestData.Instant("2026-08-10T12:01:00Z")));
    }

    [Fact]
    public void ReplayingOnlyPrimaryBlobCannotReducePersistedMaximum()
    {
        var checkpoint = CreateInitializedCheckpoint();
        string primaryPath = Path.Combine(_directory, "license-clock.bin");
        byte[] oldPrimary = File.ReadAllBytes(primaryPath);
        checkpoint.CheckAndAdvance(
            Binding,
            LicenseTestData.Instant("2026-08-10T14:00:00Z"));
        File.WriteAllBytes(primaryPath, oldPrimary);

        LicenseClockCheck result = checkpoint.CheckAndAdvance(
            Binding,
            LicenseTestData.Instant("2026-08-10T13:54:59Z"));

        Assert.True(result.RollbackDetected);
    }

    [Fact]
    public async Task ConcurrentInstancesCannotReplaceMaximumWithOlderInstant()
    {
        var shared = new FakeProtector("machine-a");
        new WindowsLicenseClockCheckpoint(_directory, shared).Initialize(
            Binding,
            LicenseTestData.Instant("2026-08-10T12:00:00Z"));
        using var lowerProtectEntered = new ManualResetEventSlim();
        using var releaseLowerProtect = new ManualResetEventSlim();
        var lower = new WindowsLicenseClockCheckpoint(
            _directory,
            new BlockingProtectProtector(shared, lowerProtectEntered, releaseLowerProtect));
        var higher = new WindowsLicenseClockCheckpoint(_directory, shared);

        Task lowerTask = Task.Run(() => lower.CheckAndAdvance(
            Binding,
            LicenseTestData.Instant("2026-08-10T13:00:00Z")));
        Assert.True(lowerProtectEntered.Wait(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken));
        Task higherTask = Task.Run(() => higher.CheckAndAdvance(
            Binding,
            LicenseTestData.Instant("2026-08-10T14:00:00Z")));
        await Task.WhenAny(
            higherTask,
            Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
        releaseLowerProtect.Set();
        await Task.WhenAll(lowerTask, higherTask);

        LicenseClockCheck result = new WindowsLicenseClockCheckpoint(_directory, shared)
            .CheckAndAdvance(
                Binding,
                LicenseTestData.Instant("2026-08-10T13:54:59Z"));
        Assert.True(result.RollbackDetected);
    }

    [Fact]
    public void DeletedPrimaryCheckpointBlocksAndIsNotRecreated()
    {
        var checkpoint = CreateInitializedCheckpoint();
        string primaryPath = Path.Combine(_directory, "license-clock.bin");
        File.Delete(primaryPath);

        LicenseClockCheck result = checkpoint.CheckAndAdvance(
            Binding,
            LicenseTestData.Instant("2026-08-10T12:01:00Z"));

        Assert.True(result.RollbackDetected);
        Assert.False(File.Exists(primaryPath));
    }

    [Fact]
    public void OversizedCheckpointIsRejectedBeforeUnprotecting()
    {
        var inner = new FakeProtector("machine-a");
        new WindowsLicenseClockCheckpoint(_directory, inner).Initialize(
            Binding,
            LicenseTestData.Instant("2026-08-10T12:00:00Z"));
        File.WriteAllBytes(
            Path.Combine(_directory, "license-clock.bin"),
            new byte[64 * 1024]);
        var protector = new CountingProtector(inner);
        var checkpoint = new WindowsLicenseClockCheckpoint(_directory, protector);

        Assert.Throws<CryptographicException>(() =>
            checkpoint.CheckAndAdvance(
                Binding,
                LicenseTestData.Instant("2026-08-10T12:01:00Z")));
        Assert.Equal(0, protector.UnprotectCalls);
    }

    [Fact]
    public void AccessRemovesOnlyValidatedOrphansForCheckpointTargets()
    {
        Directory.CreateDirectory(_directory);
        string owned = Path.Combine(_directory, ".license-clock.bin.abcd.tmp");
        string unrelated = Path.Combine(_directory, ".other-secret.bin.abcd.tmp");
        File.WriteAllBytes(owned, [1]);
        File.WriteAllBytes(unrelated, [2]);
        var checkpoint = CreateCheckpoint();

        checkpoint.Initialize(
            Binding,
            LicenseTestData.Instant("2026-08-10T12:00:00Z"));

        Assert.False(File.Exists(owned));
        Assert.True(File.Exists(unrelated));
    }

    [Fact]
    public void DefaultDpapiProtectorRoundTripsOnWindows()
    {
        var first = new WindowsLicenseClockCheckpoint(_directory);
        first.Initialize(Binding, LicenseTestData.Instant("2026-08-10T12:00:00Z"));

        LicenseClockCheck result = new WindowsLicenseClockCheckpoint(_directory)
            .CheckAndAdvance(
                Binding,
                LicenseTestData.Instant("2026-08-10T12:01:00Z"));

        Assert.False(result.RollbackDetected);
    }

    [Fact]
    public void CorruptedCheckpointIsNotSilentlyReset()
    {
        var checkpoint = CreateInitializedCheckpoint();
        string path = Path.Combine(_directory, "license-clock.bin");
        File.WriteAllBytes(path, [0x01, 0x02, 0x03]);

        Assert.Throws<CryptographicException>(() =>
            checkpoint.CheckAndAdvance(
                Binding,
                LicenseTestData.Instant("2026-08-10T12:01:00Z")));
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

    private WindowsLicenseClockCheckpoint CreateInitializedCheckpoint()
    {
        WindowsLicenseClockCheckpoint checkpoint = CreateCheckpoint();
        checkpoint.Initialize(Binding, LicenseTestData.Instant("2026-08-10T12:00:00Z"));
        return checkpoint;
    }

    private static LicenseClockBinding DifferentBinding(int changedField) => changedField switch
    {
        0 => Binding with
        {
            PharmacyId = new EntityId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"))
        },
        1 => Binding with
        {
            DeviceId = new EntityId(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"))
        },
        2 => Binding with { Channel = "Commercial" },
        3 => Binding with { DeviceKeyThumbprint = "SHA256:OTHER-DEVICE" },
        _ => throw new ArgumentOutOfRangeException(nameof(changedField))
    };
}

internal sealed class BlockingProtectProtector(
    ILocalDataProtector inner,
    ManualResetEventSlim entered,
    ManualResetEventSlim release) : ILocalDataProtector
{
    public byte[] Protect(ReadOnlySpan<byte> clear, ReadOnlySpan<byte> entropy)
    {
        entered.Set();
        if (!release.Wait(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException("The coordinated checkpoint test timed out.");
        }

        return inner.Protect(clear, entropy);
    }

    public byte[] Unprotect(ReadOnlySpan<byte> encrypted, ReadOnlySpan<byte> entropy) =>
        inner.Unprotect(encrypted, entropy);
}

internal static class UtcInstantTestExtensions
{
    internal static UtcInstant PlusSeconds(this UtcInstant instant, int seconds) =>
        UtcInstant.From(instant.Value.AddSeconds(seconds));
}
