using Nofarma.Domain.Common;

namespace Nofarma.Domain.Identity;

public sealed class LocalUser
{
    public const int MaximumFailedLoginAttempts = 5;
    public const int LockoutMinutes = 15;

    private LocalUser(
        EntityId id,
        string displayName,
        string loginName,
        UserRole role,
        CredentialKind credentialKind,
        bool isPrimaryAdministrator,
        UtcInstant createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(loginName);

        Id = id;
        DisplayName = displayName.Trim();
        LoginName = loginName.Trim();
        NormalizedLoginName = NormalizeLoginName(loginName);
        Role = role;
        CredentialKind = credentialKind;
        IsPrimaryAdministrator = isPrimaryAdministrator;
        CreatedAtUtc = createdAtUtc;
        Status = UserStatus.Active;
    }

    public EntityId Id { get; }

    public string DisplayName { get; }

    public string LoginName { get; }

    public string NormalizedLoginName { get; }

    public UserRole Role { get; private set; }

    public CredentialKind CredentialKind { get; private set; }

    public bool IsPrimaryAdministrator { get; }

    public UtcInstant CreatedAtUtc { get; }

    public UserStatus Status { get; private set; }

    public int FailedLoginCount { get; private set; }

    public UtcInstant? LockedUntilUtc { get; private set; }

    public UtcInstant? LastSuccessfulLoginUtc { get; private set; }

    public static LocalUser CreatePrimaryAdministrator(
        EntityId id,
        string displayName,
        string loginName,
        UtcInstant createdAtUtc) =>
        new(
            id,
            displayName,
            loginName,
            UserRole.Administrator,
            CredentialKind.Password,
            isPrimaryAdministrator: true,
            createdAtUtc);

    public static LocalUser CreateCashier(
        EntityId id,
        string displayName,
        string loginName,
        UtcInstant createdAtUtc) =>
        new(
            id,
            displayName,
            loginName,
            UserRole.Cashier,
            CredentialKind.Pin,
            isPrimaryAdministrator: false,
            createdAtUtc);

    public static LocalUser Create(
        EntityId id,
        string displayName,
        string loginName,
        UserRole role,
        UtcInstant createdAtUtc)
    {
        if (role == UserRole.AbiptomSupport)
        {
            throw new ArgumentException(
                "ABIPTOM support cannot have a standing local account.",
                nameof(role));
        }

        CredentialKind credentialKind = role == UserRole.Cashier
            ? CredentialKind.Pin
            : CredentialKind.Password;
        return new LocalUser(
            id,
            displayName,
            loginName,
            role,
            credentialKind,
            isPrimaryAdministrator: false,
            createdAtUtc);
    }

    public static LocalUser Restore(
        EntityId id,
        string displayName,
        string loginName,
        UserRole role,
        CredentialKind credentialKind,
        bool isPrimaryAdministrator,
        UtcInstant createdAtUtc,
        UserStatus status,
        int failedLoginCount,
        UtcInstant? lockedUntilUtc,
        UtcInstant? lastSuccessfulLoginUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(failedLoginCount);

        var user = new LocalUser(
            id,
            displayName,
            loginName,
            role,
            credentialKind,
            isPrimaryAdministrator,
            createdAtUtc)
        {
            Status = status,
            FailedLoginCount = failedLoginCount,
            LockedUntilUtc = lockedUntilUtc,
            LastSuccessfulLoginUtc = lastSuccessfulLoginUtc,
        };
        return user;
    }

    public bool IsLockedAt(UtcInstant instant) =>
        LockedUntilUtc is { } lockedUntil && lockedUntil.Value > instant.Value;

    public void RecordFailedLogin(UtcInstant occurredAtUtc)
    {
        EnsureActive();

        FailedLoginCount++;
        if (FailedLoginCount >= MaximumFailedLoginAttempts)
        {
            LockedUntilUtc = UtcInstant.From(
                occurredAtUtc.Value.AddMinutes(LockoutMinutes));
        }
    }

    public void RecordSuccessfulLogin(UtcInstant occurredAtUtc)
    {
        EnsureActive();

        FailedLoginCount = 0;
        LockedUntilUtc = null;
        LastSuccessfulLoginUtc = occurredAtUtc;
    }

    public void Unlock()
    {
        EnsureActive();

        FailedLoginCount = 0;
        LockedUntilUtc = null;
    }

    public void Deactivate()
    {
        if (IsPrimaryAdministrator)
        {
            throw new InvalidOperationException(
                "The primary administrator cannot be deactivated.");
        }

        Status = UserStatus.Disabled;
        FailedLoginCount = 0;
        LockedUntilUtc = null;
    }

    public void ChangeRole(UserRole role)
    {
        EnsureActive();
        if (role == UserRole.AbiptomSupport)
        {
            throw new ArgumentException(
                "ABIPTOM support cannot have a standing local account.",
                nameof(role));
        }

        if (IsPrimaryAdministrator && role != UserRole.Administrator)
        {
            throw new InvalidOperationException(
                "The primary administrator must retain the administrator role.");
        }

        Role = role;
        CredentialKind = role == UserRole.Cashier
            ? CredentialKind.Pin
            : CredentialKind.Password;
        FailedLoginCount = 0;
        LockedUntilUtc = null;
    }

    private static string NormalizeLoginName(string loginName) =>
        loginName.Trim().ToUpperInvariant();

    private void EnsureActive()
    {
        if (Status != UserStatus.Active)
        {
            throw new InvalidOperationException("The user is not active.");
        }
    }
}
