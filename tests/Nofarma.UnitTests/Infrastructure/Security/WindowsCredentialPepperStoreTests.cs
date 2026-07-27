using System.Runtime.Versioning;
using Nofarma.Infrastructure.Security;

namespace Nofarma.UnitTests.Infrastructure.Security;

[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialPepperStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"nofarma-pepper-{Guid.NewGuid():N}");

    [Fact]
    public void CreatedPepperIsStableAndNotStoredAsPlainBytes()
    {
        var firstStore = new WindowsCredentialPepperStore(_directory);
        byte[] first = firstStore.GetOrCreate();
        var secondStore = new WindowsCredentialPepperStore(_directory);
        byte[] second = secondStore.GetOrCreate();

        byte[] persisted = File.ReadAllBytes(
            Path.Combine(_directory, "credential-pepper.bin"));

        Assert.Equal(32, first.Length);
        Assert.Equal(first, second);
        Assert.DoesNotContain(Convert.ToHexString(first), Convert.ToHexString(persisted));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
