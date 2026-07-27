using Nofarma.Application.Identity;
using Nofarma.Infrastructure.Security;

namespace Nofarma.UnitTests.Infrastructure.Security;

public sealed class Pbkdf2CredentialHasherTests
{
    private static readonly byte[] Pepper =
        Convert.FromHexString("00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF");

    [Fact]
    public void SameCredentialProducesDifferentSaltAndHash()
    {
        var hasher = new Pbkdf2CredentialHasher(
            new FixedPepperStore(Pepper),
            workFactor: 10_000);

        CredentialHash first = hasher.Hash("Uma palavra-passe forte 2026!");
        CredentialHash second = hasher.Hash("Uma palavra-passe forte 2026!");

        Assert.NotEqual(first.Salt, second.Salt);
        Assert.NotEqual(first.Hash, second.Hash);
    }

    [Fact]
    public void CorrectCredentialVerifiesAndWrongCredentialFails()
    {
        var hasher = new Pbkdf2CredentialHasher(
            new FixedPepperStore(Pepper),
            workFactor: 10_000);
        CredentialHash stored = hasher.Hash("Uma palavra-passe forte 2026!");

        Assert.True(hasher.Verify("Uma palavra-passe forte 2026!", stored));
        Assert.False(hasher.Verify("Credencial errada", stored));
    }

    [Fact]
    public void OlderWorkFactorNeedsRehash()
    {
        var hasher = new Pbkdf2CredentialHasher(
            new FixedPepperStore(Pepper),
            workFactor: 10_000);
        CredentialHash current = hasher.Hash("Uma palavra-passe forte 2026!");
        var older = current with { WorkFactor = 9_999 };

        Assert.True(hasher.NeedsRehash(older));
        Assert.False(hasher.NeedsRehash(current));
    }

    [Fact]
    public void CredentialLongerThanLimitIsRejectedBeforeHashing()
    {
        var hasher = new Pbkdf2CredentialHasher(
            new FixedPepperStore(Pepper),
            workFactor: 10_000);

        Assert.Throws<ArgumentOutOfRangeException>(() => hasher.Hash(new string('a', 257)));
    }

    private sealed class FixedPepperStore(byte[] pepper) : ICredentialPepperStore
    {
        public byte[] GetOrCreate() => [.. pepper];
    }
}
