namespace Nofarma.Application.Identity.Setup;

public sealed record SetupRequest(
    string PharmacyName,
    string TaxIdentifier,
    string Address,
    string Contact,
    string TimeZoneId,
    string DeviceName,
    string AdministratorName,
    string AdministratorLogin,
    string AdministratorPassword);
