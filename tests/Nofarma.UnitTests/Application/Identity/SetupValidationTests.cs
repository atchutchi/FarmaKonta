using Nofarma.Application.Identity.Setup;

namespace Nofarma.UnitTests.Application.Identity;

public sealed class SetupValidationTests
{
    [Fact]
    public void InvalidDraftReturnsErrorsByFieldName()
    {
        var draft = new SetupDraft(
            "Farmacia Central",
            "",
            "Avenida principal",
            "",
            "Invalid/Zone",
            "",
            "Administrador",
            "",
            "weak",
            "different");

        IReadOnlyDictionary<string, string> errors = SetupValidator.Validate(draft);

        Assert.Contains(nameof(SetupDraft.TaxIdentifier), errors.Keys);
        Assert.Contains(nameof(SetupDraft.TimeZoneId), errors.Keys);
        Assert.Contains(nameof(SetupDraft.DeviceName), errors.Keys);
        Assert.Contains(nameof(SetupDraft.AdministratorLogin), errors.Keys);
        Assert.Contains(nameof(SetupDraft.AdministratorPassword), errors.Keys);
        Assert.Contains(nameof(SetupDraft.PasswordConfirmation), errors.Keys);
    }

    [Fact]
    public void ValidBissauDraftHasNoErrors()
    {
        SetupDraft draft = ValidDraft();

        Assert.Empty(SetupValidator.Validate(draft));
    }

    private static SetupDraft ValidDraft() =>
        new(
            "Farmacia Central",
            "510000001",
            "Avenida principal, Bissau",
            "+245 955 000 000",
            "Africa/Bissau",
            "Caixa principal",
            "Administrador",
            "admin",
            "FarmaKonta!2026",
            "FarmaKonta!2026");
}
