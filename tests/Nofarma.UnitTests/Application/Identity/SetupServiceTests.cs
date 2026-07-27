using Nofarma.Application.Abstractions;
using Nofarma.Application.Identity;
using Nofarma.Application.Identity.Setup;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;

namespace Nofarma.UnitTests.Application.Identity;

public sealed class SetupServiceTests
{
    private static readonly UtcInstant Now = UtcInstant.From(
        new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task ConfigurePreparesCompleteAtomicPayloadAndReturnsRecoveryCodeOnce()
    {
        var store = new RecordingIdentityStore();
        var service = new SetupService(
            store,
            new DeterministicHasher(),
            new FixedRecoveryCodeGenerator("7K9M-2P4Q-B6TR-8V5N-3DXC"),
            new FixedClock(Now));

        SetupResult result = await service.ConfigureAsync(
            ValidRequest(),
            TestContext.Current.CancellationToken);

        InitialSetupData saved = Assert.IsType<InitialSetupData>(store.Saved);
        Assert.Equal(InstallationStatus.ReadyForActivation, saved.Installation.Status);
        Assert.Equal("MARIA.INDJAI", saved.Installation.PrimaryAdministrator.NormalizedLoginName);
        Assert.Equal("hash:Palavra-passe forte 2026!", Text(saved.AdministratorCredential.Hash));
        Assert.Equal("hash:7K9M-2P4Q-B6TR-8V5N-3DXC", Text(saved.RecoveryCredential.Hash));
        Assert.Equal("installation.configured", saved.AuditEvent.Action);
        Assert.Equal("7K9M-2P4Q-B6TR-8V5N-3DXC", result.RecoveryCode);
        Assert.Equal(saved.Installation.Id, result.InstallationId);
    }

    [Fact]
    public async Task ConfigureRejectsAnExistingInstallationWithoutSaving()
    {
        var store = new RecordingIdentityStore { IsConfigured = true };
        var service = new SetupService(
            store,
            new DeterministicHasher(),
            new FixedRecoveryCodeGenerator("7K9M-2P4Q-B6TR-8V5N-3DXC"),
            new FixedClock(Now));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConfigureAsync(
            ValidRequest(),
            TestContext.Current.CancellationToken));

        Assert.Null(store.Saved);
    }

    [Fact]
    public async Task ConfigureRejectsWeakAdministratorPassword()
    {
        var store = new RecordingIdentityStore();
        var service = new SetupService(
            store,
            new DeterministicHasher(),
            new FixedRecoveryCodeGenerator("7K9M-2P4Q-B6TR-8V5N-3DXC"),
            new FixedClock(Now));
        SetupRequest request = ValidRequest() with { AdministratorPassword = "password" };

        await Assert.ThrowsAsync<ArgumentException>(() => service.ConfigureAsync(
            request,
            TestContext.Current.CancellationToken));

        Assert.Null(store.Saved);
    }

    private static SetupRequest ValidRequest() => new(
        "Farmácia Central",
        "500123456",
        "Avenida Amílcar Cabral, Bissau",
        "+245 955 000 000",
        "Africa/Bissau",
        "NBF-PC-001",
        "Maria Indjai",
        "maria.indjai",
        "Palavra-passe forte 2026!");

    private static string Text(byte[] value) => System.Text.Encoding.UTF8.GetString(value);

    private sealed class RecordingIdentityStore : ILocalIdentityStore
    {
        public bool IsConfigured { get; init; }

        public InitialSetupData? Saved { get; private set; }

        public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken) =>
            Task.FromResult(IsConfigured);

        public Task SaveInitialSetupAsync(
            InitialSetupData data,
            CancellationToken cancellationToken)
        {
            Saved = data;
            return Task.CompletedTask;
        }
    }

    private sealed class DeterministicHasher : ICredentialHasher
    {
        public CredentialHash Hash(string credential) => new(
            1,
            "TEST",
            1,
            [1],
            System.Text.Encoding.UTF8.GetBytes($"hash:{credential}"));

        public bool Verify(string credential, CredentialHash stored) =>
            Text(stored.Hash) == $"hash:{credential}";

        public bool NeedsRehash(CredentialHash stored) => false;
    }

    private sealed class FixedRecoveryCodeGenerator(string value) : IRecoveryCodeGenerator
    {
        public string Generate() => value;
    }

    private sealed class FixedClock(UtcInstant value) : IUtcClock
    {
        public UtcInstant GetCurrentInstant() => value;
    }
}
