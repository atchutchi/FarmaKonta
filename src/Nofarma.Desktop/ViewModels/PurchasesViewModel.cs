using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Application.Purchasing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Inventory;

namespace Nofarma.Desktop.ViewModels;

public interface IPurchasesPageOperations
{
    Task<IReadOnlyList<PurchaseSummary>> SearchAsync(string query, CancellationToken cancellationToken);
    Task<PurchaseDetails> GetAsync(EntityId purchaseId, CancellationToken cancellationToken);
    Task ConfirmReceiptAsync(EntityId purchaseId, ConfirmPurchaseReceiptRequest request, CancellationToken cancellationToken);
}

public sealed class PurchasesPageOperations(
    PurchaseService purchases,
    CurrentSession currentSession) : IPurchasesPageOperations
{
    public Task<IReadOnlyList<PurchaseSummary>> SearchAsync(string query, CancellationToken cancellationToken) =>
        purchases.SearchAsync(RequireSession(), query, cancellationToken);
    public Task<PurchaseDetails> GetAsync(EntityId purchaseId, CancellationToken cancellationToken) =>
        purchases.GetAsync(RequireSession(), purchaseId, cancellationToken);
    public async Task ConfirmReceiptAsync(EntityId purchaseId, ConfirmPurchaseReceiptRequest request, CancellationToken cancellationToken) =>
        _ = await purchases.ConfirmReceiptAsync(RequireSession(), purchaseId, request, cancellationToken);
    private LocalSession RequireSession() => currentSession.Active
        ?? throw new InvalidOperationException("Não existe uma sessão activa.");
}

public sealed record PurchaseReceiptEditorInput(
    EntityId PurchaseLineId,
    string DocumentNumber,
    string LotNumber,
    string ExpiryDate,
    string PackageQuantity,
    string UnitCostXof);

public sealed class PurchasesViewModel(IPurchasesPageOperations operations)
{
    private string? _receiptIdempotencyKey;

    public IReadOnlyList<PurchaseSummary> Purchases { get; private set; } = [];
    public PurchaseDetails? SelectedPurchase { get; private set; }
    public IReadOnlyDictionary<string, string> ValidationErrors { get; private set; } = new Dictionary<string, string>();
    public bool IsLoading { get; private set; }
    public bool IsConfirming { get; private set; }
    public bool IsEmpty => !IsLoading && Purchases.Count == 0;
    public string? ErrorMessage { get; private set; }

    public async Task LoadAsync(CancellationToken cancellationToken) =>
        Purchases = await operations.SearchAsync(string.Empty, cancellationToken);

    public async Task SelectAsync(EntityId purchaseId, CancellationToken cancellationToken) =>
        SelectedPurchase = await operations.GetAsync(purchaseId, cancellationToken);

    public bool ValidateReceipt(PurchaseReceiptEditorInput input) =>
        TryBuildReceipt(input, out _);

    public async Task<bool> ConfirmReceiptAsync(
        EntityId purchaseId,
        PurchaseReceiptEditorInput input,
        CancellationToken cancellationToken)
    {
        if (IsConfirming || !TryBuildReceipt(input, out ConfirmPurchaseReceiptRequest? request)) return false;
        IsConfirming = true;
        ErrorMessage = null;
        try
        {
            await operations.ConfirmReceiptAsync(purchaseId, request, cancellationToken);
            _receiptIdempotencyKey = null;
            SelectedPurchase = await operations.GetAsync(purchaseId, cancellationToken);
            Purchases = await operations.SearchAsync(string.Empty, cancellationToken);
            return true;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            ErrorMessage = "Não foi possível confirmar a recepção. Os dados continuam disponíveis para nova tentativa.";
            return false;
        }
        finally
        {
            IsConfirming = false;
        }
    }

    private bool TryBuildReceipt(
        PurchaseReceiptEditorInput input,
        [NotNullWhen(true)] out ConfirmPurchaseReceiptRequest? request)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(input.DocumentNumber)) errors["Documento"] = "Indica o número do documento.";
        if (string.IsNullOrWhiteSpace(input.LotNumber)) errors["Lote"] = "Indica o lote recebido.";
        bool validDate = DateOnly.TryParse(input.ExpiryDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly expiry);
        if (!validDate) errors["Validade"] = "Indica uma validade no formato AAAA-MM-DD.";
        long quantity = ParsePositive(input.PackageQuantity, "Quantidade", errors);
        long cost = ParsePositive(input.UnitCostXof, "Custo", errors);
        ValidationErrors = errors;
        if (errors.Count > 0)
        {
            request = null;
            return false;
        }

        _receiptIdempotencyKey ??= $"receipt-ui-{Guid.NewGuid():N}";
        request = new ConfirmPurchaseReceiptRequest(
            input.DocumentNumber.Trim(),
            DateOnly.FromDateTime(DateTime.UtcNow),
            null,
            _receiptIdempotencyKey,
            [new ConfirmPurchaseReceiptLineRequest(
                input.PurchaseLineId,
                quantity,
                cost,
                input.LotNumber.Trim(),
                ExpiryDate.ForDay(expiry.Year, expiry.Month, expiry.Day))]);
        return true;
    }

    private static long ParsePositive(string value, string field, Dictionary<string, string> errors)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long result) && result > 0)
        {
            return result;
        }

        errors[field] = "Usa um número inteiro superior a zero.";
        return 0;
    }
}
