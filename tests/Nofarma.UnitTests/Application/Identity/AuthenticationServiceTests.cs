using Nofarma.Application.Abstractions;
using Nofarma.Application.Identity;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;

namespace Nofarma.UnitTests.Application.Identity;

public sealed class AuthenticationServiceTests
{
    private static readonly UtcInstant Start = UtcInstant.From(
        new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task CorrectCredentialCreatesAdministrativeSession()
    {
        var fixture = new Fixture();

        SignInResult result = await fixture.Service.SignInAsync(
            new SignInRequest(" admin ", "correct"),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Session);
        Assert.Equal(UserRole.Administrator, result.Session.Role);
        Assert.Equal(Start.Value.AddMinutes(15), result.Session.ExpiresAtUtc?.Value);
        Assert.Equal(0, fixture.Identity.User.FailedLoginCount);
        Assert.Equal("authentication.succeeded", fixture.Store.LastAudit?.Action);
    }

    [Fact]
    public async Task IncorrectCredentialUsesGenericMessageAndLocksOnFifthFailure()
    {
        var fixture = new Fixture();

        SignInResult? result = null;
        for (int attempt = 0; attempt < LocalUser.MaximumFailedLoginAttempts; attempt++)
        {
            result = await fixture.Service.SignInAsync(
                new SignInRequest("admin", "wrong"),
                TestContext.Current.CancellationToken);
        }

        Assert.NotNull(result);
        Assert.False(result.Succeeded);
        Assert.Equal(AuthenticationService.GenericFailureMessage, result.Message);
        Assert.Equal(Start.Value.AddMinutes(15), fixture.Identity.User.LockedUntilUtc?.Value);
        Assert.Equal("authentication.failed", fixture.Store.LastAudit?.Action);
        Assert.Equal("credential_invalid", fixture.Store.LastAudit?.DiagnosticCode);
    }

    [Fact]
    public async Task SuccessfulSignInAfterLockoutResetsFailureCounter()
    {
        var fixture = new Fixture();
        for (int attempt = 0; attempt < LocalUser.MaximumFailedLoginAttempts; attempt++)
        {
            await fixture.Service.SignInAsync(
                new SignInRequest("admin", "wrong"),
                TestContext.Current.CancellationToken);
        }

        fixture.Clock.Advance(TimeSpan.FromMinutes(16));
        SignInResult result = await fixture.Service.SignInAsync(
            new SignInRequest("admin", "correct"),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(0, fixture.Identity.User.FailedLoginCount);
        Assert.Null(fixture.Identity.User.LockedUntilUtc);
    }

    [Fact]
    public async Task UnknownLoginStillPerformsCredentialVerification()
    {
        var fixture = new Fixture();

        SignInResult result = await fixture.Service.SignInAsync(
            new SignInRequest("unknown", "anything"),
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(1, fixture.Hasher.VerifyCount);
        Assert.Equal(AuthenticationService.GenericFailureMessage, result.Message);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            User = LocalUser.CreatePrimaryAdministrator(
                EntityId.New(),
                "Administrador",
                "admin",
                Start);
            Identity = new AuthenticationIdentity(
                User,
                new CredentialHash(1, "test", 1, [1], [2]),
                EntityId.New(),
                EntityId.New());
            Store = new FakeAuthenticationStore(Identity);
            Hasher = new FakeHasher();
            Clock = new AdjustableClock(Start);
            Session = new CurrentSession();
            Service = new AuthenticationService(
                Store,
                Hasher,
                new CredentialHash(1, "test", 1, [3], [4]),
                Session,
                Clock);
        }

        public LocalUser User { get; }

        public AuthenticationIdentity Identity { get; }

        public FakeAuthenticationStore Store { get; }

        public FakeHasher Hasher { get; }

        public AdjustableClock Clock { get; }

        public CurrentSession Session { get; }

        public AuthenticationService Service { get; }
    }

    private sealed class FakeAuthenticationStore(AuthenticationIdentity identity)
        : ILocalAuthenticationStore
    {
        public AuditEvent? LastAudit { get; private set; }

        public Task<AuthenticationIdentity?> FindByLoginAsync(
            string normalizedLogin,
            CancellationToken cancellationToken) =>
            Task.FromResult<AuthenticationIdentity?>(
                normalizedLogin == identity.User.NormalizedLoginName ? identity : null);

        public Task SaveFailedSignInAsync(
            AuthenticationIdentity storedIdentity,
            AuditEvent auditEvent,
            CancellationToken cancellationToken)
        {
            LastAudit = auditEvent;
            return Task.CompletedTask;
        }

        public Task SaveSuccessfulSignInAsync(
            AuthenticationIdentity storedIdentity,
            LocalSession session,
            AuditEvent auditEvent,
            CancellationToken cancellationToken)
        {
            LastAudit = auditEvent;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHasher : ICredentialHasher
    {
        public int VerifyCount { get; private set; }

        public CredentialHash Hash(string secret) =>
            new(1, "test", 1, [1], [2]);

        public bool Verify(string secret, CredentialHash credential)
        {
            VerifyCount++;
            return secret == "correct";
        }

        public bool NeedsRehash(CredentialHash credential) => false;
    }

    private sealed class AdjustableClock(UtcInstant current) : IUtcClock
    {
        public UtcInstant Current { get; private set; } = current;

        public void Advance(TimeSpan duration) =>
            Current = UtcInstant.From(Current.Value.Add(duration));

        public UtcInstant GetCurrentInstant() => Current;
    }
}
