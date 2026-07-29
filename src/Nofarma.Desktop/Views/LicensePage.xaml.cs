using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Nofarma.Desktop.ViewModels;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;
using WinRT.Interop;

namespace Nofarma.Desktop.Views;

public sealed partial class LicensePage : Page, IDisposable
{
    private readonly LicenseViewModel _viewModel;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _loaded;
    private bool _disposed;
    private bool _pageBusy;
    private string? _pageMessage;
    private bool _pageMessageIsSuccess;

    public LicensePage()
    {
        _viewModel = App.Services.GetRequiredService<LicenseViewModel>();
        InitializeComponent();
        RefreshSurface();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await _viewModel.LoadAsync(_lifetime.Token);
        RefreshSurface();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Dispose();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }

    private async void OnExportRequest(object sender, RoutedEventArgs e)
    {
        if (_pageBusy)
        {
            return;
        }

        _pageBusy = true;
        _pageMessage = null;
        RefreshSurface();
        try
        {
            byte[]? request = await _viewModel.CreateRequestAsync(_lifetime.Token);
            if (request is null)
            {
                RefreshSurface();
                return;
            }

            var picker = new FileSavePicker
            {
                SuggestedFileName = "pedido-activacao-nofarma"
            };
            picker.FileTypeChoices.Add(
                "Pedido NôFarma",
                new List<string> { ".nofarma-request" });
            InitializePicker(picker);
            StorageFile? file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }

            CachedFileManager.DeferUpdates(file);
            await FileIO.WriteBytesAsync(file, request);
            FileUpdateStatus update = await CachedFileManager.CompleteUpdatesAsync(file);
            if (update != FileUpdateStatus.Complete)
            {
                throw new IOException("The selected activation request file could not be updated.");
            }

            _pageMessageIsSuccess = true;
            _pageMessage = "Pedido guardado. Envie apenas o ficheiro .nofarma-request à ABIPTOM.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            _pageMessageIsSuccess = false;
            _pageMessage = "Não foi possível guardar o pedido. Escolha outra pasta e tente novamente.";
        }
        finally
        {
            _pageBusy = false;
            RefreshSurface();
        }
    }

    private async void OnImportLicense(object sender, RoutedEventArgs e)
    {
        if (_pageBusy)
        {
            return;
        }

        _pageBusy = true;
        _pageMessage = null;
        RefreshSurface();
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".nofarma-license");
            InitializePicker(picker);
            StorageFile? file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            if (!file.Name.EndsWith(".nofarma-license", StringComparison.OrdinalIgnoreCase))
            {
                ShowPageError("Selecciona um ficheiro com a extensão .nofarma-license.");
                return;
            }

            byte[] document = await ReadBoundedAsync(
                file.Path,
                LicenseViewModel.MaximumLicenseDocumentBytes,
                _lifetime.Token);
            bool imported = await _viewModel.ImportAsync(document, _lifetime.Token);
            if (imported)
            {
                _pageMessageIsSuccess = true;
                _pageMessage = _viewModel.SuccessMessage;
            }
        }
        catch (InvalidDataException)
        {
            ShowPageError("O ficheiro está vazio ou é demasiado grande. Selecciona uma licença válida e tenta novamente.");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            ShowPageError("Não foi possível ler a licença. Confirma o ficheiro e tenta novamente.");
        }
        finally
        {
            _pageBusy = false;
            RefreshSurface();
        }
    }

    private void RefreshSurface()
    {
        StatusText.Text = _viewModel.StatusText;
        StatusDescriptionText.Text = _viewModel.StatusDescription;
        PlanText.Text = _viewModel.PlanText;
        ValidFromText.Text = _viewModel.ValidFromText;
        ValidUntilText.Text = _viewModel.ValidUntilText;
        GraceUntilText.Text = _viewModel.GraceUntilText;
        DeviceIdentifierBox.Text = _viewModel.DeviceIdentifierText;
        QaModeBadge.Visibility = _viewModel.IsQaMode
            ? Visibility.Visible
            : Visibility.Collapsed;

        ApplyStatusAppearance();
        ApplyPrimaryAction();

        bool busy = _pageBusy || _viewModel.IsBusy;
        ExportRequestButton.IsEnabled = !busy;
        ImportLicenseButton.IsEnabled = !busy;
        LicenseProgress.IsActive = busy;
        LicenseProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

        string? message = _pageMessage ?? _viewModel.ErrorMessage;
        bool success = _pageMessage is not null
            ? _pageMessageIsSuccess
            : _viewModel.SuccessMessage is not null && _viewModel.ErrorMessage is null;
        LicenseMessageBar.IsOpen = !string.IsNullOrWhiteSpace(message);
        LicenseMessageBar.Severity = success
            ? InfoBarSeverity.Success
            : InfoBarSeverity.Error;
        LicenseMessageBar.Title = success ? "Operação concluída" : "Não foi possível concluir";
        LicenseMessageBar.Message = message ?? string.Empty;
    }

    private void ApplyStatusAppearance()
    {
        string glyph;
        string brushName;
        string softBrushName;
        switch (_viewModel.PresentationKind)
        {
            case LicensePresentationKind.Success:
                glyph = "\uE73E";
                brushName = "NofarmaGreenBrush";
                softBrushName = "NofarmaSuccessSoftBrush";
                break;
            case LicensePresentationKind.Error:
                glyph = "\uE783";
                brushName = "NofarmaDangerBrush";
                softBrushName = "NofarmaDangerSoftBrush";
                break;
            default:
                glyph = "\uE946";
                brushName = "NofarmaWarningBrush";
                softBrushName = "NofarmaWarningSoftBrush";
                break;
        }

        StatusIcon.Glyph = glyph;
        StatusIcon.Foreground = (Brush)Microsoft.UI.Xaml.Application.Current.Resources[brushName];
        StatusIconBackground.Background = (Brush)Microsoft.UI.Xaml.Application.Current.Resources[softBrushName];
        AutomationProperties.SetName(StatusIcon, $"Estado: {_viewModel.StatusText}");
    }

    private void ApplyPrimaryAction()
    {
        bool exportIsPrimary = _viewModel.StatusText is
            "Sem licença" or "Licença inválida" or "Verificar relógio";
        Style primary = (Style)Microsoft.UI.Xaml.Application.Current.Resources["NofarmaPrimaryButtonStyle"];
        ExportRequestButton.Style = exportIsPrimary ? primary : null;
        ImportLicenseButton.Style = exportIsPrimary ? null : primary;
    }

    private void ShowPageError(string message)
    {
        _pageMessageIsSuccess = false;
        _pageMessage = message;
    }

    private static async Task<byte[]> ReadBoundedAsync(
        string path,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        if (stream.Length <= 0 || stream.Length > maximumBytes)
        {
            throw new InvalidDataException("The selected licence file size is invalid.");
        }

        byte[] document = new byte[(int)stream.Length];
        int offset = 0;
        while (offset < document.Length)
        {
            int read = await stream.ReadAsync(
                document.AsMemory(offset),
                cancellationToken);
            if (read == 0)
            {
                throw new InvalidDataException("The selected licence file ended unexpectedly.");
            }

            offset += read;
        }

        if (await stream.ReadAsync(new byte[1], cancellationToken) != 0)
        {
            throw new InvalidDataException("The selected licence file changed while being read.");
        }

        return document;
    }

    private static void InitializePicker(object picker)
    {
        nint windowHandle = WindowNative.GetWindowHandle(
            App.Services.GetRequiredService<MainWindow>());
        InitializeWithWindow.Initialize(picker, windowHandle);
    }
}
