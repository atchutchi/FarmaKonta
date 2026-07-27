using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Nofarma.Desktop.Services;
using Nofarma.Desktop.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Nofarma.Desktop.Views;

public sealed partial class SetupWizardPage : Page
{
    private readonly NavigationService _navigation;
    private int _step = 1;

    public SetupWizardPage()
    {
        ViewModel = App.Services.GetRequiredService<SetupWizardViewModel>();
        _navigation = App.Services.GetRequiredService<NavigationService>();
        InitializeComponent();
    }

    public SetupWizardViewModel ViewModel { get; }

    private async void OnContinue(object sender, RoutedEventArgs e)
    {
        ErrorBar.IsOpen = false;
        if (_step == 1)
        {
            if (string.IsNullOrWhiteSpace(ViewModel.PharmacyName) ||
                string.IsNullOrWhiteSpace(ViewModel.TaxIdentifier) ||
                string.IsNullOrWhiteSpace(ViewModel.Address) ||
                string.IsNullOrWhiteSpace(ViewModel.DeviceName))
            {
                ShowError("Preenche os campos obrigatórios da farmácia.");
                return;
            }

            SetStep(2);
            return;
        }

        if (_step == 2)
        {
            ViewModel.AdministratorPassword = AdministratorPasswordBox.Password;
            ViewModel.PasswordConfirmation = PasswordConfirmationBox.Password;
            ContinueButton.IsEnabled = false;
            IReadOnlyDictionary<string, string> errors;
            try
            {
                errors = await ViewModel.ConfigureAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                ShowError(exception.Message);
                ContinueButton.IsEnabled = true;
                return;
            }

            ContinueButton.IsEnabled = true;
            if (errors.Count > 0)
            {
                ShowError(string.Join(" ", errors.Values.Distinct(StringComparer.Ordinal)));
                return;
            }

            AdministratorPasswordBox.Password = string.Empty;
            PasswordConfirmationBox.Password = string.Empty;
            RecoveryCodeText.Text = ViewModel.RecoveryCode;
            SetStep(3);
            return;
        }

        if (_step == 3 && RecoverySavedCheckBox.IsChecked == true)
        {
            SetStep(4);
            _navigation.NavigateToLogin();
        }
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (_step == 2)
        {
            SetStep(1);
        }
    }

    private void OnCopyRecoveryCode(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.RecoveryCode))
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(ViewModel.RecoveryCode);
        Clipboard.SetContent(package);
    }

    private void OnRecoveryConfirmationChanged(object sender, RoutedEventArgs e)
    {
        if (_step == 3)
        {
            ContinueButton.IsEnabled = RecoverySavedCheckBox.IsChecked == true;
        }
    }

    private void SetStep(int step)
    {
        _step = step;
        PharmacyPanel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        AdministratorPanel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        RecoveryPanel.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        ContinueButton.Content = step == 3 ? "Concluir preparação" : "Continuar";
        ContinueButton.IsEnabled = step != 3 || RecoverySavedCheckBox.IsChecked == true;

        SetCircle(StepOneCircle, StepOneNumber, step, 1);
        SetCircle(StepTwoCircle, StepTwoNumber, step, 2);
        SetCircle(StepThreeCircle, StepThreeNumber, step, 3);
        SetCircle(StepFourCircle, StepFourNumber, step, 4);
    }

    private static void SetCircle(Ellipse circle, TextBlock number, int current, int target)
    {
        if (current >= target)
        {
            circle.Fill = target < current
                ? (Brush)Microsoft.UI.Xaml.Application.Current.Resources["NofarmaGreenBrush"]
                : (Brush)Microsoft.UI.Xaml.Application.Current.Resources["NofarmaBlueBrush"];
            circle.StrokeThickness = 0;
            number.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
            number.Text = target < current ? "✓" : target.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private void ShowError(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }
}
