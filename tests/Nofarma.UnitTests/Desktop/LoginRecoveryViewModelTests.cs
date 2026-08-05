using Nofarma.Application.Identity.Authentication;
using Nofarma.Desktop.Services;
using Nofarma.Desktop.ViewModels;

namespace Nofarma.UnitTests.Desktop;

public sealed class LoginRecoveryViewModelTests
{
    [Fact]
    public async Task MismatchedConfirmationDoesNotRotateCredentials()
    {
        var operations = new RecordingLoginRecoveryOperations();
        var viewModel = new LoginRecoveryViewModel(operations);

        RecoveryResult result = await viewModel.RecoverAsync(
            "CODIGO-UNICO",
            "NovaSenha!2026",
            "SenhaDiferente!2026",
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("A confirmação não corresponde à nova palavra-passe.", result.Message);
        Assert.Equal(0, operations.Calls);
    }

    [Fact]
    public async Task ValidFormReturnsTheRotatedRecoveryCode()
    {
        var operations = new RecordingLoginRecoveryOperations(
            new RecoveryResult(true, string.Empty, "NOVO-CODIGO-UNICO"));
        var viewModel = new LoginRecoveryViewModel(operations);

        RecoveryResult result = await viewModel.RecoverAsync(
            "CODIGO-ANTIGO",
            "NovaSenha!2026",
            "NovaSenha!2026",
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("NOVO-CODIGO-UNICO", result.NewRecoveryCode);
        Assert.Equal(1, operations.Calls);
        Assert.Equal("CODIGO-ANTIGO", operations.RecoveryCode);
        Assert.Equal("NovaSenha!2026", operations.NewPassword);
    }

    [Fact]
    public async Task WeakPasswordProducesAUsefulLocalMessage()
    {
        var operations = new RecordingLoginRecoveryOperations
        {
            Exception = new ArgumentException("internal validation")
        };
        var viewModel = new LoginRecoveryViewModel(operations);

        RecoveryResult result = await viewModel.RecoverAsync(
            "CODIGO-UNICO",
            "fraca",
            "fraca",
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(
            "Usa pelo menos 12 caracteres, incluindo maiúscula, minúscula, número e símbolo.",
            result.Message);
        Assert.Equal(1, operations.Calls);
    }

    private sealed class RecordingLoginRecoveryOperations(
        RecoveryResult? result = null) : ILoginRecoveryOperations
    {
        public int Calls { get; private set; }

        public string? RecoveryCode { get; private set; }

        public string? NewPassword { get; private set; }

        public Exception? Exception { get; init; }

        public Task<RecoveryResult> RecoverAsync(
            string recoveryCode,
            string newPassword,
            CancellationToken cancellationToken)
        {
            Calls++;
            RecoveryCode = recoveryCode;
            NewPassword = newPassword;
            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(
                result ?? new RecoveryResult(false, RecoveryService.GenericFailureMessage, null));
        }
    }
}
