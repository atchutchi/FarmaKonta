using Microsoft.UI.Xaml.Controls;
using Nofarma.Desktop.Views;
using Nofarma.Infrastructure.Composition;

namespace Nofarma.Desktop.Services;

public sealed class NavigationService
{
    private Frame? _frame;

    public void Initialize(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _frame = frame;
    }

    public void NavigateTo(ApplicationStartDestination destination)
    {
        Type pageType = destination == ApplicationStartDestination.Setup
            ? typeof(SetupWizardPage)
            : typeof(LoginPage);
        Navigate(pageType);
    }

    public void NavigateToLogin() => Navigate(typeof(LoginPage));

    private void Navigate(Type pageType)
    {
        if (_frame is null)
        {
            throw new InvalidOperationException("A navegação ainda não foi inicializada.");
        }

        _frame.Navigate(pageType);
    }
}
