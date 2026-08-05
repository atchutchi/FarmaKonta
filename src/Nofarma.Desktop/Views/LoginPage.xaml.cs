using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Desktop.Services;
using Nofarma.Desktop.ViewModels;

namespace Nofarma.Desktop.Views;

public sealed partial class LoginPage : Page
{
    private readonly LoginViewModel _viewModel;
    private readonly LoginRecoveryViewModel _recoveryViewModel;
    private readonly NavigationService _navigation;
    private readonly DesktopLicenseConfiguration _licenseConfiguration;
    private bool _loaded;

    public LoginPage()
    {
        _viewModel = App.Services.GetRequiredService<LoginViewModel>();
        _recoveryViewModel = App.Services.GetRequiredService<LoginRecoveryViewModel>();
        _navigation = App.Services.GetRequiredService<NavigationService>();
        _licenseConfiguration = App.Services.GetRequiredService<DesktopLicenseConfiguration>();
        InitializeComponent();
        QaModeText.Visibility = _licenseConfiguration.IsQa
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await _viewModel.LoadAsync(CancellationToken.None);
        AdministratorProfileBox.ItemsSource = _viewModel.AdministratorProfiles;
        CashierProfileBox.ItemsSource = _viewModel.CashierProfiles;
        AdministratorProfileBox.SelectedIndex = _viewModel.AdministratorProfiles.Count > 0 ? 0 : -1;
        CashierProfileBox.SelectedIndex = _viewModel.CashierProfiles.Count > 0 ? 0 : -1;
    }

    private async void OnAdministratorSignIn(object sender, RoutedEventArgs e) =>
        await SignInAsync(
            AdministratorProfileBox.SelectedItem as LocalProfile,
            AdministratorCredentialBox.Password);

    private async void OnCashierSignIn(object sender, RoutedEventArgs e) =>
        await SignInAsync(
            CashierProfileBox.SelectedItem as LocalProfile,
            CashierCredentialBox.Password);

    private async void OnRecoverAccess(object sender, RoutedEventArgs e)
    {
        var dialog = new RecoveryAccessDialog(_recoveryViewModel)
        {
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }

    private async Task SignInAsync(LocalProfile? profile, string credential)
    {
        LoginErrorBar.IsOpen = false;
        if (profile is null)
        {
            ShowError("Não existe um perfil disponível para este tipo de acesso.");
            return;
        }

        SignInResult result;
        try
        {
            result = await _viewModel.SignInAsync(profile, credential, CancellationToken.None);
        }
        catch (ArgumentException)
        {
            ShowError(AuthenticationService.GenericFailureMessage);
            return;
        }

        AdministratorCredentialBox.Password = string.Empty;
        CashierCredentialBox.Password = string.Empty;
        if (!result.Succeeded)
        {
            string message = result.Message;
            if (result.LockedUntilUtc is { } lockedUntil)
            {
                TimeSpan remaining = lockedUntil.Value - DateTimeOffset.UtcNow;
                if (remaining > TimeSpan.Zero)
                {
                    message += $" Tenta novamente dentro de {Math.Ceiling(remaining.TotalMinutes)} minutos.";
                }
            }

            ShowError(message);
            return;
        }

        _navigation.NavigateToShell();
    }

    private void OnPinKey(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string digit } && CashierCredentialBox.Password.Length < 6)
        {
            CashierCredentialBox.Password += digit;
        }
    }

    private void OnPinBackspace(object sender, RoutedEventArgs e)
    {
        string pin = CashierCredentialBox.Password;
        CashierCredentialBox.Password = pin.Length > 0 ? pin[..^1] : string.Empty;
    }

    private void OnPinClear(object sender, RoutedEventArgs e) =>
        CashierCredentialBox.Password = string.Empty;

    private void ShowError(string message)
    {
        LoginErrorBar.Message = message;
        LoginErrorBar.IsOpen = true;
    }
}
