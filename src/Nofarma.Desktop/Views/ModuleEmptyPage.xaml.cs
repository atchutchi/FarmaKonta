using Microsoft.UI.Xaml.Controls;

namespace Nofarma.Desktop.Views;

public sealed partial class ModuleEmptyPage : Page
{
    public ModuleEmptyPage(string title, string description)
    {
        InitializeComponent();
        TitleText.Text = title;
        DescriptionText.Text = description;
    }
}
