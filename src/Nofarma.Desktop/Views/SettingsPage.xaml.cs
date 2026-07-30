using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Configuration;

namespace Nofarma.Desktop.Views;

public sealed partial class SettingsPage : Page
{
    private readonly ILocalApplicationInfoStore _applicationInfo;
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ABIPTOM",
        "Nofarma",
        "config",
        "fiscal-settings.json");

    public SettingsPage()
    {
        _applicationInfo = App.Services.GetRequiredService<ILocalApplicationInfoStore>();
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        LocalApplicationInfo? info = await _applicationInfo.GetAsync(CancellationToken.None);
        PharmacyNameBox.Text = info?.PharmacyName ?? string.Empty;
        TaxIdentifierBox.Text = info?.TaxIdentifier ?? string.Empty;
        AddressBox.Text = info?.Address ?? string.Empty;
        ContactBox.Text = info?.Contact ?? string.Empty;

        if (File.Exists(_settingsPath))
        {
            await using FileStream stream = File.OpenRead(_settingsPath);
            FiscalSettings? settings = await JsonSerializer.DeserializeAsync<FiscalSettings>(stream);
            DgciTestUrlBox.Text = settings?.TestUrl ?? string.Empty;
            DgciProductionUrlBox.Text = settings?.ProductionUrl ?? string.Empty;
        }
    }

    private async void OnSaveFiscalSettings(object sender, RoutedEventArgs e)
    {
        SettingsMessageBar.IsOpen = false;
        if (!IsEmptyOrAbsoluteHttps(DgciTestUrlBox.Text) ||
            !IsEmptyOrAbsoluteHttps(DgciProductionUrlBox.Text))
        {
            SettingsMessageBar.Severity = InfoBarSeverity.Error;
            SettingsMessageBar.Message = "Usa um endereço HTTPS completo ou deixa o campo vazio.";
            SettingsMessageBar.IsOpen = true;
            return;
        }

        string? directory = Path.GetDirectoryName(_settingsPath);
        if (directory is null)
        {
            return;
        }

        Directory.CreateDirectory(directory);
        await using FileStream stream = File.Create(_settingsPath);
        await JsonSerializer.SerializeAsync(
            stream,
            new FiscalSettings(
                DgciTestUrlBox.Text.Trim(),
                DgciProductionUrlBox.Text.Trim()));
        SettingsMessageBar.Severity = InfoBarSeverity.Success;
        SettingsMessageBar.Message = "Configuração guardada apenas neste computador.";
        SettingsMessageBar.IsOpen = true;
    }

    private static bool IsEmptyOrAbsoluteHttps(string value) =>
        string.IsNullOrWhiteSpace(value) ||
        (Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri) &&
            uri.Scheme == Uri.UriSchemeHttps);

    private sealed record FiscalSettings(string TestUrl, string ProductionUrl);
}
