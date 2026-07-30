using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nofarma.Desktop.ViewModels;
using Nofarma.Domain.Identity;

namespace Nofarma.Desktop.Views;

public sealed partial class UsersPage : Page
{
    private readonly UsersViewModel _viewModel;
    private bool _loaded;

    public UsersPage()
    {
        _viewModel = App.Services.GetRequiredService<UsersViewModel>();
        InitializeComponent();
        NewRoleBox.ItemsSource = new[]
        {
            UserRole.Cashier,
            UserRole.Pharmacist,
            UserRole.StockManager,
            UserRole.Manager,
            UserRole.Auditor,
            UserRole.Administrator
        };
        NewRoleBox.SelectedItem = UserRole.Cashier;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await RefreshAsync();
    }

    private async void OnCreateUser(object sender, RoutedEventArgs e)
    {
        UserMessageBar.IsOpen = false;
        if (NewRoleBox.SelectedItem is not UserRole role)
        {
            return;
        }

        try
        {
            await _viewModel.CreateAsync(
                NewDisplayNameBox.Text,
                NewLoginBox.Text,
                role,
                NewCredentialBox.Password,
                CancellationToken.None);
            NewDisplayNameBox.Text = string.Empty;
            NewLoginBox.Text = string.Empty;
            NewCredentialBox.Password = string.Empty;
            UserMessageBar.Severity = InfoBarSeverity.Success;
            UserMessageBar.Message = "Utilizador criado localmente.";
            UserMessageBar.IsOpen = true;
            UsersList.ItemsSource = _viewModel.Users;
        }
        catch (Exception exception)
        {
            UserMessageBar.Severity = InfoBarSeverity.Error;
            UserMessageBar.Message = exception.Message;
            UserMessageBar.IsOpen = true;
        }
    }

    private void OnRoleChanged(object sender, SelectionChangedEventArgs e)
    {
        bool isCashier = NewRoleBox.SelectedItem is UserRole.Cashier;
        CredentialLabel.Text = isCashier ? "PIN do Caixa" : "Palavra-passe";
        CredentialHelpText.Text = isCashier
            ? "Use apenas quatro a seis algarismos."
            : "Use pelo menos 12 caracteres com maiúscula, minúscula, número e símbolo.";
        NewCredentialBox.MaxLength = isCashier ? 6 : 256;
        NewCredentialBox.Password = string.Empty;
    }

    private async Task RefreshAsync()
    {
        await _viewModel.LoadAsync(CancellationToken.None);
        UsersList.ItemsSource = _viewModel.Users;
    }
}
