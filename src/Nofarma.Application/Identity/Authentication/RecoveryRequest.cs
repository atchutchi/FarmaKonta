namespace Nofarma.Application.Identity.Authentication;

public sealed record RecoveryRequest(
    string RecoveryCode,
    string NewAdministratorPassword);
