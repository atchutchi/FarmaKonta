namespace Nofarma.Application.Identity.Authentication;

public sealed record RecoveryIdentity(
    AuthenticationIdentity Administrator,
    CredentialHash RecoveryCredential);
