using Nofarma.Application.Abstractions;
using Nofarma.Application.Identity;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;

namespace Nofarma.UnitTests.Application.Identity;

public sealed class RecoveryServiceTests
{
    [Fact]
    public async Task ValidRecoveryRotatesPasswordAndOneTimeCode()
    {
        var identity = CreateIdentity();
        var store = new FakeRecoveryStore(
            new RecoveryIdentity(identity, HashFor("old-code")));
        var service = new RecoveryService(
            store,
            new FakeHasher(),
            new SequentialCodeGenerator(),
            new CurrentSession(),
            new FixedClock());

        RecoveryResult result = await service.RecoverAsync(
            new RecoveryRequest("old-code", "NovaSenha!2026"),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("NEWC-ODE2-3456-789A-BCDE", result.NewRecoveryCode);
        Assert.Equal("NovaSenha!2026", store.NewPasswordHash?.Algorithm);
        Assert.Equal("NEWC-ODE2-3456-789A-BCDE", store.NewRecoveryHash?.Algorithm);
        Assert.True(store.SessionsInvalidated);
        Assert.Equal("recovery.completed", store.LastAudit?.Action);
    }

    [Fact]
    public async Task PreviousRecoveryCodeFailsAfterSuccessfulUse()
    {
        var identity = CreateIdentity();
        var store = new FakeRecoveryStore(
            new RecoveryIdentity(identity, HashFor("old-code")));
        var service = new RecoveryService(
            store,
            new FakeHasher(),
            new SequentialCodeGenerator(),
            new CurrentSession(),
            new FixedClock());

        await service.RecoverAsync(
            new RecoveryRequest("old-code", "NovaSenha!2026"),
            TestContext.Current.CancellationToken);
        RecoveryResult repeated = await service.RecoverAsync(
            new RecoveryRequest("old-code", "OutraSenha!2026"),
            TestContext.Current.CancellationToken);

        Assert.False(repeated.Succeeded);
        Assert.Null(repeated.NewRecoveryCode);
        Assert.Equal(RecoveryService.GenericFailureMessage, repeated.Message);
    }

    private static AuthenticationIdentity CreateIdentity()
    {
        UtcInstant now = new FixedClock().GetCurrentInstant();
        return new AuthenticationIdentity(
            LocalUser.CreatePrimaryAdministrator(
                EntityId.New(),
                "Administrador",
                "admin",
                now),
            HashFor("password"),
            EntityId.New(),
            EntityId.New());
    }

    private static CredentialHash HashFor(string secret) =>
        new(1, secret, 1, [1], [2]);

    private sealed class FakeRecoveryStore(RecoveryIdentity current)
        : ILocalRecoveryStore
    {
        private RecoveryIdentity _current = current;

        public CredentialHash? NewPasswordHash { get; private set; }

        public CredentialHash? NewRecoveryHash { get; private set; }

        public bool SessionsInvalidated { get; private set; }

        public AuditEvent? LastAudit { get; private set; }

        public Task<RecoveryIdentity?> GetRecoveryIdentityAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<RecoveryIdentity?>(_current);

        public Task CompleteRecoveryAsync(
            RecoveryIdentity identity,
            CredentialHash newPassword,
            CredentialHash newRecoveryCode,
            AuditEvent auditEvent,
            UtcInstant occurredAtUtc,
            CancellationToken cancellationToken)
        {
            NewPasswordHash = newPassword;
            NewRecoveryHash = newRecoveryCode;
            SessionsInvalidated = true;
            LastAudit = auditEvent;
            _current = identity with { RecoveryCredential = newRecoveryCode };
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHasher : ICredentialHasher
    {
        public CredentialHash Hash(string secret) => HashFor(secret);

        public bool Verify(string secret, CredentialHash credential) =>
            secret == credential.Algorithm;

        public bool NeedsRehash(CredentialHash credential) => false;
    }

    private sealed class SequentialCodeGenerator : IRecoveryCodeGenerator
    {
        public string Generate() => "NEWC-ODE2-3456-789A-BCDE";
    }

    private sealed class FixedClock : IUtcClock
    {
        public UtcInstant GetCurrentInstant() =>
            UtcInstant.From(new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero));
    }
}
