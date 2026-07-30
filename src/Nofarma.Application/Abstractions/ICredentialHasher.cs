using Nofarma.Application.Identity;

namespace Nofarma.Application.Abstractions;

public interface ICredentialHasher
{
    CredentialHash Hash(string credential);

    bool Verify(string credential, CredentialHash stored);

    bool NeedsRehash(CredentialHash stored);
}
