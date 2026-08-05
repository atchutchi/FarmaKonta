using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Desktop.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Nofarma.Desktop.Views;

public sealed partial class RecoveryAccessDialog : ContentDialog
{
    private readonly LoginRecoveryViewModel _viewModel;
    private bool _recoveryCompleted;

    public RecoveryAccessDialog(LoginRecoveryViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
    }

    private async void OnPrimaryButtonClick(
        ContentDialog sender,
        ContentDialogButtonClickEventArgs args)
    {
        if (_recoveryCompleted)
        {
            args.Cancel = RecoverySavedCheckBox.IsChecked != true;
            return;
        }

        args.Cancel = true;
        ContentDialogButtonClickDeferral deferral = args.GetDeferral();
        try
        {
            IsPrimaryButtonEnabled = false;
            RecoveryErrorBar.IsOpen = false;
            RecoveryResult result = await _viewModel.RecoverAsync(
                RecoveryCodeBox.Text,
                NewPasswordBox.Password,
                PasswordConfirmationBox.Password,
                CancellationToken.None);
            if (!result.Succeeded || string.IsNullOrWhiteSpace(result.NewRecoveryCode))
            {
                RecoveryErrorBar.Message = result.Message;
                RecoveryErrorBar.IsOpen = true;
                return;
            }

            RecoveryCodeBox.Text = string.Empty;
            NewPasswordBox.Password = string.Empty;
            PasswordConfirmationBox.Password = string.Empty;
            NewRecoveryCodeText.Text = result.NewRecoveryCode;
            RecoveryFormPanel.Visibility = Visibility.Collapsed;
            RecoverySuccessPanel.Visibility = Visibility.Visible;
            PrimaryButtonText = "Concluir";
            IsSecondaryButtonEnabled = false;
            _recoveryCompleted = true;
        }
        finally
        {
            IsPrimaryButtonEnabled = _recoveryCompleted
                ? RecoverySavedCheckBox.IsChecked == true
                : true;
            deferral.Complete();
        }
    }

    private void OnCopyRecoveryCode(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NewRecoveryCodeText.Text))
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(NewRecoveryCodeText.Text);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    private void OnRecoverySavedChanged(object sender, RoutedEventArgs e)
    {
        if (_recoveryCompleted)
        {
            IsPrimaryButtonEnabled = RecoverySavedCheckBox.IsChecked == true;
        }
    }
}
