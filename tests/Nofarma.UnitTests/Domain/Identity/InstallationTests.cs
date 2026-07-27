using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;

namespace Nofarma.UnitTests.Domain.Identity;

public sealed class InstallationTests
{
    [Fact]
    public void CreateStartsPreparingWithPrimaryAdministrator()
    {
        UtcInstant now = UtcInstant.From(
            new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero));

        LocalUser administrator = LocalUser.CreatePrimaryAdministrator(
            EntityId.New(),
            "Maria Indjai",
            "maria.indjai",
            now);

        Installation installation = Installation.Create(
            EntityId.New(),
            new Pharmacy(
                EntityId.New(),
                "Farmácia Central",
                "500123456",
                "Bissau",
                "+245 955 000 000",
                "Africa/Bissau"),
            new Device(EntityId.New(), "NBF-PC-001"),
            administrator,
            now);

        Assert.Equal(InstallationStatus.Preparing, installation.Status);
        Assert.True(installation.PrimaryAdministrator.IsPrimaryAdministrator);
        Assert.Equal(CredentialKind.Password, installation.PrimaryAdministrator.CredentialKind);
        Assert.Equal("MARIA.INDJAI", installation.PrimaryAdministrator.NormalizedLoginName);
    }

    [Fact]
    public void ReadyInstallationRejectsASecondCompletion()
    {
        UtcInstant now = UtcInstant.From(
            new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero));
        Installation installation = Installation.Create(
            EntityId.New(),
            new Pharmacy(
                EntityId.New(),
                "Farmácia Central",
                "500123456",
                "Bissau",
                "+245 955 000 000",
                "Africa/Bissau"),
            new Device(EntityId.New(), "NBF-PC-001"),
            LocalUser.CreatePrimaryAdministrator(
                EntityId.New(),
                "Maria Indjai",
                "maria.indjai",
                now),
            now);

        installation.MarkReadyForActivation(now);

        Assert.Equal(InstallationStatus.ReadyForActivation, installation.Status);
        Assert.Throws<InvalidOperationException>(() => installation.MarkReadyForActivation(now));
    }
}
