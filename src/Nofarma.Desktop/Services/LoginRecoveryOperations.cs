using Nofarma.Application.Identity.Authentication;

namespace Nofarma.Desktop.Services;

public interface ILoginRecoveryOperations
{
    Task<RecoveryResult> RecoverAsync(
        string recoveryCode,
        string newPassword,
        CancellationToken cancellationToken);
}

public sealed class LoginRecoveryOperations(
    RecoveryService recoveryService) : ILoginRecoveryOperations
{
    public Task<RecoveryResult> RecoverAsync(
        string recoveryCode,
        string newPassword,
        CancellationToken cancellationToken) =>
        recoveryService.RecoverAsync(
            new RecoveryRequest(recoveryCode, newPassword),
            cancellationToken);
}
