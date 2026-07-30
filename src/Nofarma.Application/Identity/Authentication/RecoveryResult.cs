namespace Nofarma.Application.Identity.Authentication;

public sealed record RecoveryResult(
    bool Succeeded,
    string Message,
    string? NewRecoveryCode);
