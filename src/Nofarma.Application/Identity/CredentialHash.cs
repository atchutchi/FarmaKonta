namespace Nofarma.Application.Identity;

public sealed record CredentialHash(
    int Version,
    string Algorithm,
    int WorkFactor,
    byte[] Salt,
    byte[] Hash);
