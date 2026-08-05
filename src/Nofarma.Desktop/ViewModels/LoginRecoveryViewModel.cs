using Nofarma.Application.Identity.Authentication;
using Nofarma.Desktop.Services;

namespace Nofarma.Desktop.ViewModels;

public sealed class LoginRecoveryViewModel(
    ILoginRecoveryOperations operations)
{
    public const string PasswordRequirementsMessage =
        "Usa pelo menos 12 caracteres, incluindo maiúscula, minúscula, número e símbolo.";

    public async Task<RecoveryResult> RecoverAsync(
        string recoveryCode,
        string newPassword,
        string passwordConfirmation,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(newPassword, passwordConfirmation, StringComparison.Ordinal))
        {
            return new RecoveryResult(
                false,
                "A confirmação não corresponde à nova palavra-passe.",
                null);
        }

        try
        {
            return await operations.RecoverAsync(
                recoveryCode,
                newPassword,
                cancellationToken);
        }
        catch (ArgumentException)
        {
            return new RecoveryResult(false, PasswordRequirementsMessage, null);
        }
    }
}
