using System.Globalization;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Application.Sales;
using Nofarma.Domain.Sales;

namespace Nofarma.Desktop.ViewModels;

public interface ICashPageOperations
{
    Task<CashShiftSummary?> GetCurrentAsync(CancellationToken cancellationToken);
    Task<CashShiftSummary> OpenAsync(OpenCashShiftRequest request, CancellationToken cancellationToken);
    Task<CashShiftSummary> RecordManualMovementAsync(
        ManualCashMovementRequest request,
        CancellationToken cancellationToken);
    Task<CashShiftSummary> CloseAsync(CloseCashShiftRequest request, CancellationToken cancellationToken);
}

public sealed class CashPageOperations(
    CashShiftService cashShifts,
    CurrentSession currentSession) : ICashPageOperations
{
    public Task<CashShiftSummary?> GetCurrentAsync(CancellationToken cancellationToken) =>
        cashShifts.GetCurrentAsync(RequireSession(), cancellationToken);

    public Task<CashShiftSummary> OpenAsync(
        OpenCashShiftRequest request,
        CancellationToken cancellationToken) =>
        cashShifts.OpenAsync(RequireSession(), request, cancellationToken);

    public Task<CashShiftSummary> RecordManualMovementAsync(
        ManualCashMovementRequest request,
        CancellationToken cancellationToken) =>
        cashShifts.RecordManualMovementAsync(RequireSession(), request, cancellationToken);

    public Task<CashShiftSummary> CloseAsync(
        CloseCashShiftRequest request,
        CancellationToken cancellationToken) =>
        cashShifts.CloseAsync(RequireSession(), request, cancellationToken);

    private LocalSession RequireSession() => currentSession.Active
        ?? throw new InvalidOperationException("Não existe uma sessão activa.");
}

public enum CashDifferenceState
{
    None,
    Exact,
    Shortage,
    Overage
}

public sealed class CashViewModel(ICashPageOperations operations)
{
    private bool _hasLoaded;
    private string? _openIdempotencyKey;
    private long? _openKeyAmount;
    private string? _movementIdempotencyKey;
    private (CashMovementType Type, long Amount, string Reason)? _movementKeyInput;
    private string? _closeIdempotencyKey;
    private long? _closeKeyAmount;

    public CashShiftSummary? CurrentShift { get; private set; }
    public IReadOnlyDictionary<string, string> ValidationErrors { get; private set; } =
        new Dictionary<string, string>();
    public bool IsLoading { get; private set; }
    public bool IsSubmitting { get; private set; }
    public bool HasOpenShift => CurrentShift is { Status: CashShiftStatus.Open };
    public bool IsNoShift => _hasLoaded && !IsLoading && CurrentShift is null;
    public string StatusMessage { get; private set; } = "A confirmar o estado do caixa.";
    public string? ErrorMessage { get; private set; }
    public string OpeningCashText => FormatXof(CurrentShift?.OpeningCashXof);
    public string TotalEntriesText => FormatXof(CurrentShift?.TotalEntriesXof);
    public string TotalExitsText => FormatXof(CurrentShift?.TotalExitsXof);
    public string ExpectedCashText => FormatXof(CurrentShift?.ExpectedCashXof);
    public string OpenedAtText => CurrentShift is null
        ? "Indisponível"
        : CurrentShift.OpenedAtUtc.Value.ToString("dd/MM/yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture);
    public string MovementCountText => CurrentShift is null
        ? "Indisponível"
        : CurrentShift.MovementCount == 1
            ? "1 movimento registado"
            : $"{CurrentShift.MovementCount} movimentos registados";
    public long? CloseDifferenceXof { get; private set; }
    public CashDifferenceState DifferenceState { get; private set; }
    public string DifferenceText { get; private set; } = "Indica o valor contado para calcular a diferença.";

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        _hasLoaded = false;
        ErrorMessage = null;
        StatusMessage = "A confirmar o estado do caixa.";
        try
        {
            CurrentShift = await operations.GetCurrentAsync(cancellationToken);
            _hasLoaded = true;
            StatusMessage = CurrentShift is null
                ? "Não existe um turno aberto neste posto de caixa. Indica o fundo inicial para começar."
                : "Turno aberto neste posto de caixa.";
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            CurrentShift = null;
            ErrorMessage = SafeError(exception, "Não foi possível consultar o turno de caixa.");
            StatusMessage = "O estado do caixa não foi confirmado.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public bool ValidateOpeningAmount(string amount) => TryOpeningAmount(amount, out _);

    public async Task<bool> OpenAsync(string amount, CancellationToken cancellationToken)
    {
        if (IsSubmitting || !TryOpeningAmount(amount, out long openingCash))
        {
            return false;
        }

        IsSubmitting = true;
        ErrorMessage = null;
        if (_openKeyAmount != openingCash)
        {
            _openIdempotencyKey = null;
            _openKeyAmount = openingCash;
        }
        _openIdempotencyKey ??= $"cash-open-ui-{Guid.NewGuid():N}";
        try
        {
            CurrentShift = await operations.OpenAsync(
                new OpenCashShiftRequest(openingCash, _openIdempotencyKey),
                cancellationToken);
            _hasLoaded = true;
            _openIdempotencyKey = null;
            _openKeyAmount = null;
            ValidationErrors = new Dictionary<string, string>();
            StatusMessage = "Turno aberto neste posto de caixa.";
            ResetClosePreview();
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            ErrorMessage = SafeError(exception, "Não foi possível abrir o turno. Os valores continuam disponíveis para nova tentativa.");
            return false;
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    public bool ValidateManualMovement(CashMovementType type, string amount, string reason) =>
        TryManualMovement(type, amount, reason, out _, out _);

    public bool CanSubmitManualMovement(CashMovementType type, string amount, string reason) =>
        HasOpenShift && BuildManualMovementErrors(type, amount, reason, out _, out _).Count == 0;

    public void ResetManualMovementForm() => SetValidationGroup(
        ["Movimento", "Valor do movimento", "Motivo"],
        new Dictionary<string, string>());

    public async Task<bool> RecordManualMovementAsync(
        CashMovementType type,
        string amount,
        string reason,
        CancellationToken cancellationToken)
    {
        if (IsSubmitting || !TryManualMovement(type, amount, reason, out long movementAmount, out string normalizedReason))
        {
            return false;
        }

        IsSubmitting = true;
        ErrorMessage = null;
        var keyInput = (type, movementAmount, normalizedReason);
        if (_movementKeyInput != keyInput)
        {
            _movementIdempotencyKey = null;
            _movementKeyInput = keyInput;
        }
        _movementIdempotencyKey ??= $"cash-movement-ui-{Guid.NewGuid():N}";
        try
        {
            CurrentShift = await operations.RecordManualMovementAsync(
                new ManualCashMovementRequest(
                    type,
                    movementAmount,
                    normalizedReason,
                    _movementIdempotencyKey),
                cancellationToken);
            _movementIdempotencyKey = null;
            _movementKeyInput = null;
            ValidationErrors = new Dictionary<string, string>();
            StatusMessage = type == CashMovementType.ManualEntry
                ? "Entrada manual registada."
                : "Saída manual registada.";
            ResetClosePreview();
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            ErrorMessage = SafeError(exception, "Não foi possível registar o movimento. Os valores continuam disponíveis para nova tentativa.");
            return false;
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    public bool PreviewClose(string countedCash)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!TryParseWholeXof(countedCash, allowZero: true, out long counted))
        {
            errors["Valor contado"] = "Usa um número inteiro igual ou superior a zero.";
        }
        if (CurrentShift is null)
        {
            errors["Valor contado"] = "Abre um turno antes de indicar o valor contado.";
        }

        SetValidationGroup(["Valor contado"], errors);
        if (errors.Count > 0)
        {
            ResetClosePreview();
            return false;
        }

        CloseDifferenceXof = counted - CurrentShift!.ExpectedCashXof;
        DifferenceState = CloseDifferenceXof switch
        {
            < 0 => CashDifferenceState.Shortage,
            > 0 => CashDifferenceState.Overage,
            _ => CashDifferenceState.Exact
        };
        DifferenceText = DifferenceState switch
        {
            CashDifferenceState.Shortage => $"Falta {FormatXof(Math.Abs(CloseDifferenceXof.Value))}",
            CashDifferenceState.Overage => $"Excesso de {FormatXof(CloseDifferenceXof.Value)}",
            _ => "Sem diferença. O valor contado coincide com o esperado."
        };
        return true;
    }

    public async Task<bool> CloseAsync(string countedCash, CancellationToken cancellationToken)
    {
        if (IsSubmitting || !PreviewClose(countedCash))
        {
            return false;
        }

        long counted = checked(CurrentShift!.ExpectedCashXof + CloseDifferenceXof!.Value);
        IsSubmitting = true;
        ErrorMessage = null;
        if (_closeKeyAmount != counted)
        {
            _closeIdempotencyKey = null;
            _closeKeyAmount = counted;
        }
        _closeIdempotencyKey ??= $"cash-close-ui-{Guid.NewGuid():N}";
        try
        {
            _ = await operations.CloseAsync(
                new CloseCashShiftRequest(counted, _closeIdempotencyKey),
                cancellationToken);
            CurrentShift = null;
            _hasLoaded = true;
            _closeIdempotencyKey = null;
            _closeKeyAmount = null;
            ValidationErrors = new Dictionary<string, string>();
            ResetClosePreview();
            StatusMessage = "Turno fechado. O caixa está pronto para uma nova abertura.";
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            ErrorMessage = SafeError(exception, "Não foi possível fechar o turno. O valor contado continua disponível para nova tentativa.");
            return false;
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    private bool TryOpeningAmount(string amount, out long openingCash)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!TryParseWholeXof(amount, allowZero: true, out openingCash))
        {
            errors["Fundo inicial"] = "Usa um número inteiro igual ou superior a zero.";
        }
        SetValidationGroup(["Fundo inicial"], errors);
        return errors.Count == 0;
    }

    private bool TryManualMovement(
        CashMovementType type,
        string amount,
        string reason,
        out long movementAmount,
        out string normalizedReason)
    {
        Dictionary<string, string> errors = BuildManualMovementErrors(
            type,
            amount,
            reason,
            out movementAmount,
            out normalizedReason);
        SetValidationGroup(["Movimento", "Valor do movimento", "Motivo"], errors);
        return errors.Count == 0;
    }

    private static Dictionary<string, string> BuildManualMovementErrors(
        CashMovementType type,
        string amount,
        string reason,
        out long movementAmount,
        out string normalizedReason)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (type is not CashMovementType.ManualEntry and not CashMovementType.ManualExit)
        {
            errors["Movimento"] = "Selecciona entrada ou saída manual.";
        }
        if (!TryParseWholeXof(amount, allowZero: false, out movementAmount))
        {
            errors["Valor do movimento"] = "Usa um número inteiro superior a zero.";
        }
        normalizedReason = reason?.Trim() ?? string.Empty;
        if (normalizedReason.Length == 0)
        {
            errors["Motivo"] = "Indica o motivo do movimento manual.";
        }
        else if (normalizedReason.Length > 500)
        {
            errors["Motivo"] = "O motivo não pode exceder 500 caracteres.";
        }
        return errors;
    }

    private void SetValidationGroup(
        IReadOnlyCollection<string> groupKeys,
        IReadOnlyDictionary<string, string> groupErrors)
    {
        var merged = ValidationErrors
            .Where(entry => !groupKeys.Contains(entry.Key, StringComparer.Ordinal))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        foreach ((string key, string value) in groupErrors)
        {
            merged[key] = value;
        }
        ValidationErrors = merged;
    }

    private static bool TryParseWholeXof(string value, bool allowZero, out long amount)
    {
        bool parsed = long.TryParse(
            value?.Trim(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out amount);
        return parsed && (allowZero ? amount >= 0 : amount > 0);
    }

    private static string FormatXof(long? amount) => amount is null
        ? "Indisponível"
        : $"{amount.Value.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ')} XOF";

    private static string SafeError(Exception exception, string fallback) => exception switch
    {
        SalesValidationException or CashShiftOperationBlockedException or
        CashShiftConflictException or CashShiftConcurrencyException => exception.Message,
        _ => fallback
    };

    private void ResetClosePreview()
    {
        CloseDifferenceXof = null;
        DifferenceState = CashDifferenceState.None;
        DifferenceText = "Indica o valor contado para calcular a diferença.";
    }
}
