namespace Nofarma.Infrastructure.Persistence.Records;

public sealed class LocalUserRecord
{
    public Guid Id { get; set; }

    public Guid PharmacyId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string LoginName { get; set; } = string.Empty;

    public string NormalizedLoginName { get; set; } = string.Empty;

    public int Role { get; set; }

    public int CredentialKind { get; set; }

    public bool IsPrimaryAdministrator { get; set; }

    public int Status { get; set; }

    public int FailedLoginCount { get; set; }

    public DateTimeOffset? LockedUntilUtc { get; set; }

    public DateTimeOffset? LastSuccessfulLoginUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public CredentialRecord Credential { get; set; } = null!;
}
