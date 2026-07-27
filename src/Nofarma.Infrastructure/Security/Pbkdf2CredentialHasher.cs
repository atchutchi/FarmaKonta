using System.Security.Cryptography;
using System.Text;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Identity;

namespace Nofarma.Infrastructure.Security;

public sealed class Pbkdf2CredentialHasher : ICredentialHasher
{
    public const int CurrentVersion = 1;
    public const int DefaultWorkFactor = 600_000;
    public const int MaximumCredentialLength = 256;
    public const string CurrentAlgorithm = "PBKDF2-HMAC-SHA256+HMAC-SHA256";

    private const int SaltSize = 16;
    private const int HashSize = 32;

    private readonly ICredentialPepperStore _pepperStore;
    private readonly int _workFactor;

    public Pbkdf2CredentialHasher(
        ICredentialPepperStore pepperStore,
        int workFactor = DefaultWorkFactor)
    {
        ArgumentNullException.ThrowIfNull(pepperStore);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workFactor);

        _pepperStore = pepperStore;
        _workFactor = workFactor;
    }

    public CredentialHash Hash(string credential)
    {
        ValidateCredential(credential);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = DeriveHash(credential, salt, _workFactor);

        return new CredentialHash(
            CurrentVersion,
            CurrentAlgorithm,
            _workFactor,
            salt,
            hash);
    }

    public bool Verify(string credential, CredentialHash stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ValidateCredential(credential);

        if (stored.Version <= 0 ||
            !string.Equals(stored.Algorithm, CurrentAlgorithm, StringComparison.Ordinal) ||
            stored.WorkFactor <= 0 ||
            stored.Salt.Length != SaltSize ||
            stored.Hash.Length != HashSize)
        {
            return false;
        }

        byte[] candidate = DeriveHash(credential, stored.Salt, stored.WorkFactor);
        try
        {
            return CryptographicOperations.FixedTimeEquals(candidate, stored.Hash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(candidate);
        }
    }

    public bool NeedsRehash(CredentialHash stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        return stored.Version != CurrentVersion ||
            !string.Equals(stored.Algorithm, CurrentAlgorithm, StringComparison.Ordinal) ||
            stored.WorkFactor != _workFactor ||
            stored.Salt.Length != SaltSize ||
            stored.Hash.Length != HashSize;
    }

    private byte[] DeriveHash(string credential, byte[] salt, int workFactor)
    {
        byte[] pepper = _pepperStore.GetOrCreate();
        byte[] credentialBytes = Encoding.UTF8.GetBytes(credential);
        byte[] pepperedCredential;

        try
        {
            using var hmac = new HMACSHA256(pepper);
            pepperedCredential = hmac.ComputeHash(credentialBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pepper);
            CryptographicOperations.ZeroMemory(credentialBytes);
        }

        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(
                pepperedCredential,
                salt,
                workFactor,
                HashAlgorithmName.SHA256,
                HashSize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pepperedCredential);
        }
    }

    private static void ValidateCredential(string credential)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credential);

        if (credential.Length > MaximumCredentialLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(credential),
                $"Credentials cannot exceed {MaximumCredentialLength} characters.");
        }
    }
}
