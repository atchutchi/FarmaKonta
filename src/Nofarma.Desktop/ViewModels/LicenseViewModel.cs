using System.Globalization;
using Nofarma.Application.Licensing;
using Nofarma.Domain.Licensing;

namespace Nofarma.Desktop.ViewModels;

public enum LicensePresentationKind
{
    Neutral = 0,
    Success = 1,
    Warning = 2,
    Error = 3
}

public sealed record LicensePageSnapshot(
    LicenseStatus Status,
    string? DeviceKeyThumbprint);

public interface ILicensePageOperations
{
    bool IsQa { get; }

    Task<LicensePageSnapshot> LoadAsync(CancellationToken cancellationToken);

    Task<ReadOnlyMemory<byte>> CreateRequestAsync(CancellationToken cancellationToken);

    Task ImportAsync(ReadOnlyMemory<byte> document, CancellationToken cancellationToken);
}

public sealed class LicenseViewModel(ILicensePageOperations operations)
{
    public const int MaximumLicenseDocumentBytes = 64 * 1024;

    public bool IsBusy { get; private set; }

    public bool IsQaMode => operations.IsQa;

    public string ChannelText => IsQaMode ? "Modo QA" : string.Empty;

    public string StatusText { get; private set; } = "A confirmar licença";

    public string StatusDescription { get; private set; } =
        "A aplicação está a confirmar o estado guardado neste computador.";

    public LicensePresentationKind PresentationKind { get; private set; } =
        LicensePresentationKind.Neutral;

    public string PlanText { get; private set; } = "Não disponível";

    public string ValidFromText { get; private set; } = "Não disponível";

    public string ValidUntilText { get; private set; } = "Não disponível";

    public string GraceUntilText { get; private set; } = "Não disponível";

    public string DeviceIdentifierText { get; private set; } = "Não disponível";

    public string? ErrorMessage { get; private set; }

    public string? SuccessMessage { get; private set; }

    public bool LastImportWasPersisted { get; private set; }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        SuccessMessage = null;
        try
        {
            await LoadCoreAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            ErrorMessage = "Não foi possível confirmar a licença. Tenta novamente. Se o problema continuar, exporta um novo pedido neste computador.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<byte[]?> CreateRequestAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return null;
        }

        IsBusy = true;
        ErrorMessage = null;
        SuccessMessage = null;
        try
        {
            ReadOnlyMemory<byte> request = await operations.CreateRequestAsync(cancellationToken);
            if (request.IsEmpty)
            {
                ErrorMessage = "Não foi possível preparar o pedido. Tenta novamente.";
                return null;
            }

            return request.ToArray();
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            ErrorMessage = "Não foi possível preparar o pedido. Confirma a configuração da farmácia e tenta novamente.";
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> ImportAsync(
        byte[] document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (IsBusy)
        {
            return false;
        }

        LastImportWasPersisted = false;

        if (document.Length == 0)
        {
            ErrorMessage = "O ficheiro está vazio. Selecciona uma licença emitida pela ABIPTOM.";
            SuccessMessage = null;
            return false;
        }

        if (document.Length > MaximumLicenseDocumentBytes)
        {
            ErrorMessage = "O ficheiro é demasiado grande. Selecciona um ficheiro .nofarma-license válido.";
            SuccessMessage = null;
            return false;
        }

        byte[] stableDocument = document.ToArray();
        IsBusy = true;
        ErrorMessage = null;
        SuccessMessage = null;
        try
        {
            await operations.ImportAsync(stableDocument, cancellationToken);
            LastImportWasPersisted = true;
            await LoadCoreAsync(cancellationToken);
            SuccessMessage = "Licença importada e estado actualizado.";
            return true;
        }
        catch (OperationCanceledException) when (LastImportWasPersisted)
        {
            MarkPersistedImportUnconfirmed();
            return false;
        }
        catch (LicenseImportException exception)
        {
            ErrorMessage = RecoveryMessage(exception.Code);
            return false;
        }
        catch (Exception) when (LastImportWasPersisted)
        {
            MarkPersistedImportUnconfirmed();
            return false;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            ErrorMessage = "Não foi possível importar a licença. Mantivemos o estado anterior. Confirma o ficheiro e tenta novamente.";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        LicensePageSnapshot snapshot = await operations.LoadAsync(cancellationToken);
        Apply(snapshot);
    }

    private void Apply(LicensePageSnapshot snapshot)
    {
        (StatusText, StatusDescription, PresentationKind) = snapshot.Status.State switch
        {
            LicenseState.Missing => (
                "Sem licença",
                "Este computador ainda não tem uma licença instalada. Exporte um pedido e envie-o à ABIPTOM.",
                LicensePresentationKind.Warning),
            LicenseState.Valid => (
                "Licença activa",
                "As novas operações estão disponíveis neste computador.",
                LicensePresentationKind.Success),
            LicenseState.Grace => (
                "Tolerância",
                "A validade terminou e decorre o período de tolerância. Renove antes da data indicada.",
                LicensePresentationKind.Warning),
            LicenseState.ExpiredReadOnly => (
                "Só consulta",
                "A validade e a tolerância terminaram. A consulta permanece disponível. Importe uma renovação para retomar novas operações.",
                LicensePresentationKind.Error),
            LicenseState.Invalid => (
                "Licença inválida",
                "A licença instalada não pode ser validada. Exporte um novo pedido e contacte a ABIPTOM.",
                LicensePresentationKind.Error),
            LicenseState.ClockRollback => (
                "Verificar relógio",
                "O relógio deste computador precisa de ser confirmado. Corrija a data e a hora antes de tentar novamente.",
                LicensePresentationKind.Error),
            LicenseState.NotYetValid => (
                "Ainda não válida",
                "A licença está instalada, mas a data de início ainda não chegou. Confirma as datas indicadas.",
                LicensePresentationKind.Warning),
            _ => (
                "Licença inválida",
                "Não foi possível reconhecer o estado da licença. Exporte um novo pedido e contacte a ABIPTOM.",
                LicensePresentationKind.Error)
        };

        LicenseGrant? grant = snapshot.Status.Grant;
        PlanText = grant?.Plan switch
        {
            LicensePlan.Monthly => "Mensal",
            LicensePlan.Annual => "Anual",
            _ => "Não disponível"
        };
        ValidFromText = FormatDate(grant?.ValidFromUtc.Value, inclusive: false);
        ValidUntilText = FormatDate(grant?.ValidUntilUtc.Value, inclusive: true);
        GraceUntilText = FormatDate(grant?.GraceUntilUtc.Value, inclusive: true);
        DeviceIdentifierText = string.IsNullOrWhiteSpace(snapshot.DeviceKeyThumbprint)
            ? "Não disponível"
            : snapshot.DeviceKeyThumbprint;
    }

    private static string FormatDate(DateTimeOffset? instant, bool inclusive)
    {
        if (instant is null)
        {
            return "Não disponível";
        }

        string date = instant.Value.ToUniversalTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        return inclusive ? $"{date}, inclusive" : date;
    }

    private void MarkPersistedImportUnconfirmed()
    {
        StatusText = "Estado por confirmar";
        StatusDescription = "A licença foi instalada. O estado actualizado ainda não foi confirmado.";
        PresentationKind = LicensePresentationKind.Warning;
        PlanText = "Por confirmar";
        ValidFromText = "Por confirmar";
        ValidUntilText = "Por confirmar";
        GraceUntilText = "Por confirmar";
        ErrorMessage = "A licença foi instalada, mas não foi possível confirmar o novo estado. Volta a abrir esta página para verificar a licença.";
    }

    private static string RecoveryMessage(string code) => code switch
    {
        "DEVICE_MISMATCH" or "LICENSE_BINDING_INVALID" =>
            "Esta licença pertence a outro computador. Exporte um novo pedido neste dispositivo e peça uma nova licença.",
        "PHARMACY_MISMATCH" =>
            "Esta licença pertence a outra farmácia. Confirma o pedido enviado à ABIPTOM e selecciona a licença correcta.",
        "LICENSE_ROLLBACK" =>
            "Já existe uma licença mais recente neste computador. Selecciona a renovação mais recente ou contacta a ABIPTOM.",
        "CHANNEL_MISMATCH" or "LICENSE_CHANNEL_INVALID" =>
            "Esta licença foi emitida para outro canal. Instala a versão correcta do NôFarma e pede uma licença para esse canal.",
        "DOCUMENT_TOO_LARGE" =>
            "O ficheiro é demasiado grande. Selecciona um ficheiro .nofarma-license válido.",
        "DOCUMENT_INVALID" or "DOCUMENT_TOO_DEEP" or "DOCUMENT_VERSION_UNSUPPORTED" =>
            "O ficheiro não é válido ou não é suportado por esta versão. Pede uma nova licença à ABIPTOM.",
        "LICENSE_CLOCK_CHECKPOINT" or "CLOCK_ROLLBACK" =>
            "Não foi possível confirmar o relógio deste computador. Corrige a data e a hora e tenta novamente.",
        "LICENSE_CONFLICT" =>
            "A licença mudou durante a importação. Mantivemos o estado anterior. Tenta importar novamente.",
        _ =>
            "A licença não pôde ser importada. Mantivemos o estado anterior. Confirma o ficheiro e pede apoio à ABIPTOM."
    };
}
