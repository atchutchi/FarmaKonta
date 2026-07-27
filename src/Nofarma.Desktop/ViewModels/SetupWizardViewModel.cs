using System.ComponentModel;
using System.Runtime.CompilerServices;
using Nofarma.Application.Identity.Setup;

namespace Nofarma.Desktop.ViewModels;

public sealed class SetupWizardViewModel(SetupService setupService) : INotifyPropertyChanged
{
    private bool _isBusy;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string PharmacyName { get; set; } = string.Empty;

    public string TaxIdentifier { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public string Contact { get; set; } = string.Empty;

    public string TimeZoneId { get; set; } = "Africa/Bissau";

    public string DeviceName { get; set; } = Environment.MachineName;

    public string AdministratorName { get; set; } = string.Empty;

    public string AdministratorLogin { get; set; } = string.Empty;

    public string AdministratorPassword { get; set; } = string.Empty;

    public string PasswordConfirmation { get; set; } = string.Empty;

    public string? RecoveryCode { get; private set; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value)
            {
                return;
            }

            _isBusy = value;
            OnPropertyChanged();
        }
    }

    public IReadOnlyDictionary<string, string> Validate()
    {
        SetupDraft draft = CreateDraft();
        return SetupValidator.Validate(draft);
    }

    public async Task<IReadOnlyDictionary<string, string>> ConfigureAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, string> errors = Validate();
        if (errors.Count > 0 || IsBusy)
        {
            return errors;
        }

        IsBusy = true;
        try
        {
            SetupResult result = await setupService.ConfigureAsync(
                new SetupRequest(
                    PharmacyName,
                    TaxIdentifier,
                    Address,
                    Contact,
                    TimeZoneId,
                    DeviceName,
                    AdministratorName,
                    AdministratorLogin,
                    AdministratorPassword),
                cancellationToken);
            RecoveryCode = result.RecoveryCode;
            AdministratorPassword = string.Empty;
            PasswordConfirmation = string.Empty;
            OnPropertyChanged(nameof(RecoveryCode));
            return new Dictionary<string, string>();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private SetupDraft CreateDraft() =>
        new(
            PharmacyName,
            TaxIdentifier,
            Address,
            Contact,
            TimeZoneId,
            DeviceName,
            AdministratorName,
            AdministratorLogin,
            AdministratorPassword,
            PasswordConfirmation);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
