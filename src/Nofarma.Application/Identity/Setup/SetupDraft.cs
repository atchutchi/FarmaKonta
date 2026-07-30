namespace Nofarma.Application.Identity.Setup;

public sealed record SetupDraft(
    string PharmacyName,
    string TaxIdentifier,
    string Address,
    string Contact,
    string TimeZoneId,
    string DeviceName,
    string AdministratorName,
    string AdministratorLogin,
    string AdministratorPassword,
    string PasswordConfirmation);
