using System.Globalization;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Application.Sales;
using Nofarma.Domain.Common;
using Nofarma.Domain.Sales;

namespace Nofarma.Desktop.ViewModels;

public interface ISalesPageOperations
{
    Task<IReadOnlyList<SaleProductResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<SuspendedSaleSummary>> GetSuspendedAsync(
        CancellationToken cancellationToken);
    Task<SuspendedSaleSummary> SuspendAsync(
        SuspendSaleRequest request,
        CancellationToken cancellationToken);
    Task<ResumedSaleDetails> ResumeSuspendedAsync(
        EntityId suspendedSaleId,
        CancellationToken cancellationToken);
    Task<bool> DeleteSuspendedAsync(
        EntityId suspendedSaleId,
        CancellationToken cancellationToken);
    Task<SaleSummary> CompleteAsync(
        CompleteSaleRequest request,
        CancellationToken cancellationToken);
    Task<ReceiptDetails?> GetReceiptAsync(
        EntityId receiptId,
        CancellationToken cancellationToken);
}

public sealed class SalesPageOperations(
    SaleService sales,
    CurrentSession currentSession) : ISalesPageOperations
{
    public Task<IReadOnlyList<SaleProductResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken) =>
        sales.SearchProductsAsync(RequireSession(), query, cancellationToken);

    public Task<IReadOnlyList<SuspendedSaleSummary>> GetSuspendedAsync(
        CancellationToken cancellationToken) =>
        sales.GetSuspendedAsync(RequireSession(), cancellationToken);

    public Task<SuspendedSaleSummary> SuspendAsync(
        SuspendSaleRequest request,
        CancellationToken cancellationToken) =>
        sales.SuspendAsync(RequireSession(), request, cancellationToken);

    public Task<ResumedSaleDetails> ResumeSuspendedAsync(
        EntityId suspendedSaleId,
        CancellationToken cancellationToken) =>
        sales.ResumeSuspendedAsync(RequireSession(), suspendedSaleId, cancellationToken);

    public Task<bool> DeleteSuspendedAsync(
        EntityId suspendedSaleId,
        CancellationToken cancellationToken) =>
        sales.DeleteSuspendedAsync(RequireSession(), suspendedSaleId, cancellationToken);

    public Task<SaleSummary> CompleteAsync(
        CompleteSaleRequest request,
        CancellationToken cancellationToken) =>
        sales.CompleteAsync(RequireSession(), request, cancellationToken);

    public Task<ReceiptDetails?> GetReceiptAsync(
        EntityId receiptId,
        CancellationToken cancellationToken) =>
        sales.GetReceiptAsync(RequireSession(), receiptId, cancellationToken);

    private LocalSession RequireSession() => currentSession.Active
        ?? throw new InvalidOperationException("Não existe uma sessão activa.");
}

public sealed record PaymentEntryInput(
    PaymentMethod Method,
    string Amount,
    string? Reference);

public sealed class PaymentEntryViewModel(
    PaymentMethod method,
    string amount,
    string? reference)
{
    public PaymentMethod Method { get; set; } = method;
    public string Amount { get; set; } = amount;
    public string? Reference { get; set; } = reference;
}

public sealed class SaleCartLineViewModel
{
    internal SaleCartLineViewModel(SaleProductResult product)
    {
        ProductId = product.ProductId;
        PackageId = product.PackageId;
        Code = product.Code;
        Name = product.Name;
        PackageName = product.PackageName;
        PackageFactor = product.PackageFactor;
        UnitPriceXof = product.SalePriceXof;
        AvailableQuantityBase = product.AvailableQuantityBase;
        QuantityPackages = 1;
    }

    internal SaleCartLineViewModel(ResumedSaleLineDetails line)
    {
        ProductId = line.ProductId;
        PackageId = line.PackageId;
        Code = line.Code ?? "INDISPONÍVEL";
        Name = line.Name ?? "Produto indisponível";
        PackageName = line.PackageName ?? "Embalagem indisponível";
        PackageFactor = line.PackageFactor ?? 1;
        QuantityPackages = line.QuantityPackages;
        DiscountXof = line.DiscountXof;
        UnitPriceXof = line.SalePriceXof ?? 0;
        AvailableQuantityBase = line.AvailableQuantityBase;
        RequiresReview = line.RequiresReview;
        ReviewMessage = line.RequiresReview
            ? "Revê esta linha porque o preço ou o stock actual não permite confirmação directa."
            : null;
    }

    public EntityId ProductId { get; }
    public EntityId PackageId { get; }
    public string Code { get; }
    public string Name { get; }
    public string PackageName { get; }
    public long PackageFactor { get; }
    public long QuantityPackages { get; internal set; }
    public long DiscountXof { get; internal set; }
    public long UnitPriceXof { get; }
    public long AvailableQuantityBase { get; }
    public bool RequiresReview { get; internal set; }
    public string? ReviewMessage { get; internal set; }
    public long GrossXof => checked(UnitPriceXof * QuantityPackages);
    public long TotalXof => checked(GrossXof - DiscountXof);
    public long RequiredQuantityBase => checked(PackageFactor * QuantityPackages);
}

public sealed class SalesViewModel(ISalesPageOperations operations)
{
    private readonly List<SaleCartLineViewModel> _cartLines = [];
    private long _totalDiscountXof;
    private long _changeXof;
    private string? _completionKey;
    private string? _completionSignature;

    public IReadOnlyList<SaleProductResult> SearchResults { get; private set; } = [];
    public IReadOnlyList<SaleCartLineViewModel> CartLines => _cartLines;
    public IReadOnlyList<SuspendedSaleSummary> SuspendedSales { get; private set; } = [];
    public IReadOnlyDictionary<string, string> ValidationErrors { get; private set; } =
        new Dictionary<string, string>();
    public bool IsSearching { get; private set; }
    public bool IsSubmitting { get; private set; }
    public bool IsPaymentOpen { get; private set; }
    public bool IsSuspendedSalesOpen { get; private set; }
    public bool ShouldRefocusSearch { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string StatusMessage { get; private set; } = "Pesquisa um produto para iniciar a venda.";
    public ReceiptDetails? LastReceipt { get; private set; }
    public string SubtotalText => FormatXof(SubtotalXof);
    public string DiscountText => FormatXof(TotalDiscountXof);
    public string TotalText => FormatXof(TotalXof);
    public string ChangeText => FormatXof(_changeXof);

    private long SubtotalXof => _cartLines.Sum(line => line.GrossXof);
    private long TotalDiscountXof => checked(
        _cartLines.Sum(line => line.DiscountXof) + _totalDiscountXof);
    private long TotalXof => checked(SubtotalXof - TotalDiscountXof);

    public async Task SearchAsync(string query, CancellationToken cancellationToken)
    {
        string normalized = query?.Trim() ?? string.Empty;
        ErrorMessage = null;
        ShouldRefocusSearch = false;
        if (normalized.Length == 0)
        {
            SearchResults = [];
            return;
        }
        IsSearching = true;
        try
        {
            SearchResults = await operations.SearchAsync(normalized, cancellationToken);
            StatusMessage = SearchResults.Count == 0
                ? "Nenhum produto vendável corresponde à pesquisa."
                : $"{SearchResults.Count} produto(s) encontrado(s).";
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            SearchResults = [];
            ErrorMessage = SafeError(exception, "Não foi possível pesquisar produtos.");
        }
        finally
        {
            IsSearching = false;
        }
    }

    public bool AddProduct(SaleProductResult product)
    {
        ArgumentNullException.ThrowIfNull(product);
        ErrorMessage = null;
        if (product.AvailableQuantityBase <= 0 || product.PackageFactor <= 0)
        {
            ErrorMessage = "O produto não tem stock vendável.";
            return false;
        }
        SaleCartLineViewModel? existing = _cartLines.SingleOrDefault(line =>
            line.ProductId == product.ProductId && line.PackageId == product.PackageId);
        if (existing is null)
        {
            var line = new SaleCartLineViewModel(product);
            if (line.RequiredQuantityBase > line.AvailableQuantityBase)
            {
                ErrorMessage = "A embalagem seleccionada excede o stock disponível.";
                return false;
            }
            _cartLines.Add(line);
        }
        else
        {
            long nextQuantity = checked(existing.QuantityPackages + 1);
            if (checked(nextQuantity * existing.PackageFactor) > existing.AvailableQuantityBase)
            {
                ErrorMessage = "Não existe stock suficiente para aumentar a quantidade.";
                return false;
            }
            existing.QuantityPackages = nextQuantity;
        }
        InvalidateCompletion();
        ShouldRefocusSearch = true;
        StatusMessage = "Produto adicionado ao carrinho.";
        return true;
    }

    public bool SetQuantity(SaleCartLineViewModel line, long quantityPackages)
    {
        if (!_cartLines.Contains(line) || quantityPackages <= 0 ||
            checked(quantityPackages * line.PackageFactor) > line.AvailableQuantityBase)
        {
            ErrorMessage = "A quantidade não é válida para o stock disponível.";
            return false;
        }
        line.QuantityPackages = quantityPackages;
        if (line.DiscountXof > line.GrossXof)
        {
            line.DiscountXof = 0;
        }
        line.RequiresReview = false;
        line.ReviewMessage = null;
        InvalidateCompletion();
        return true;
    }

    public bool SetLineDiscount(SaleCartLineViewModel line, string amount)
    {
        if (!_cartLines.Contains(line) || !TryWholeXof(amount, allowZero: true, out long discount) ||
            discount > line.GrossXof)
        {
            SetValidation("Desconto da linha", "Indica um desconto inteiro entre zero e o valor bruto da linha.");
            return false;
        }
        line.DiscountXof = discount;
        ClearValidation("Desconto da linha");
        InvalidateCompletion();
        return true;
    }

    public bool SetTotalDiscount(string amount)
    {
        if (!TryWholeXof(amount, allowZero: true, out long discount) ||
            discount > checked(SubtotalXof - _cartLines.Sum(line => line.DiscountXof)))
        {
            SetValidation("Desconto total", "Indica um desconto inteiro que não exceda o total do carrinho.");
            return false;
        }
        _totalDiscountXof = discount;
        ClearValidation("Desconto total");
        InvalidateCompletion();
        return true;
    }

    public void OpenPayment()
    {
        IsPaymentOpen = true;
        IsSuspendedSalesOpen = false;
        _completionKey ??= $"sale-complete-ui-{Guid.NewGuid():N}";
        _completionSignature = null;
        ValidationErrors = new Dictionary<string, string>();
    }

    public async Task<bool> CompleteAsync(
        IReadOnlyCollection<PaymentEntryInput> payments,
        CancellationToken cancellationToken)
    {
        if (IsSubmitting)
        {
            return false;
        }
        if (!TryBuildPayments(payments, out SalePaymentRequest[] parsed, out long change))
        {
            IsPaymentOpen = true;
            return false;
        }
        string signature = BuildCompletionSignature(parsed);
        if (!string.Equals(signature, _completionSignature, StringComparison.Ordinal))
        {
            _completionKey ??= $"sale-complete-ui-{Guid.NewGuid():N}";
            _completionSignature = signature;
        }
        CompleteSaleRequest request = new(
            _cartLines.Select(line => new CompleteSaleLineRequest(
                line.ProductId,
                line.PackageId,
                line.QuantityPackages,
                line.DiscountXof)).ToArray(),
            _totalDiscountXof,
            parsed,
            _completionKey!);
        IsSubmitting = true;
        ErrorMessage = null;
        try
        {
            SaleSummary result = await operations.CompleteAsync(request, cancellationToken);
            _changeXof = change;
            LastReceipt = await operations.GetReceiptAsync(result.ReceiptId, cancellationToken);
            _cartLines.Clear();
            _totalDiscountXof = 0;
            _completionKey = null;
            _completionSignature = null;
            IsPaymentOpen = false;
            ValidationErrors = new Dictionary<string, string>();
            StatusMessage = $"Venda {result.Number} concluída.";
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            ErrorMessage = SafeError(
                exception,
                "Não foi possível concluir a venda. O carrinho mantém-se disponível para nova tentativa.");
            return false;
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    public async Task<bool> SuspendAsync(string? name, CancellationToken cancellationToken)
    {
        if (IsSubmitting || _cartLines.Count == 0)
        {
            ErrorMessage = "Adiciona pelo menos um produto antes de suspender a venda.";
            return false;
        }
        IsSubmitting = true;
        ErrorMessage = null;
        try
        {
            SuspendedSaleSummary result = await operations.SuspendAsync(
                new SuspendSaleRequest(
                    null,
                    name,
                    _cartLines.Select(line => new CompleteSaleLineRequest(
                        line.ProductId,
                        line.PackageId,
                        line.QuantityPackages,
                        line.DiscountXof)).ToArray()),
                cancellationToken);
            _cartLines.Clear();
            _totalDiscountXof = 0;
            InvalidateCompletion();
            StatusMessage = result.Name is null
                ? "Venda suspensa."
                : $"Venda suspensa: {result.Name}.";
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            ErrorMessage = SafeError(exception, "Não foi possível suspender a venda.");
            return false;
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    public async Task LoadSuspendedAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        try
        {
            SuspendedSales = await operations.GetSuspendedAsync(cancellationToken);
            IsSuspendedSalesOpen = true;
            IsPaymentOpen = false;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            ErrorMessage = SafeError(exception, "Não foi possível consultar as vendas suspensas.");
        }
    }

    public async Task<bool> ResumeSuspendedAsync(
        EntityId suspendedSaleId,
        CancellationToken cancellationToken)
    {
        if (IsSubmitting)
        {
            return false;
        }
        IsSubmitting = true;
        ErrorMessage = null;
        try
        {
            ResumedSaleDetails resumed = await operations.ResumeSuspendedAsync(
                suspendedSaleId,
                cancellationToken);
            _cartLines.Clear();
            _cartLines.AddRange(resumed.Lines.Select(line => new SaleCartLineViewModel(line)));
            _totalDiscountXof = 0;
            InvalidateCompletion();
            _ = await operations.DeleteSuspendedAsync(suspendedSaleId, cancellationToken);
            SuspendedSales = SuspendedSales.Where(item => item.Id != suspendedSaleId).ToArray();
            IsSuspendedSalesOpen = false;
            StatusMessage = _cartLines.Any(line => line.RequiresReview)
                ? "Venda retomada. Revê as linhas assinaladas antes de cobrar."
                : "Venda retomada com preços e stock actuais.";
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            ErrorMessage = SafeError(exception, "Não foi possível retomar a venda suspensa.");
            return false;
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    public async Task<bool> DeleteSuspendedAsync(
        EntityId suspendedSaleId,
        CancellationToken cancellationToken)
    {
        try
        {
            bool deleted = await operations.DeleteSuspendedAsync(
                suspendedSaleId,
                cancellationToken);
            if (deleted)
            {
                SuspendedSales = SuspendedSales.Where(item => item.Id != suspendedSaleId).ToArray();
                StatusMessage = "Venda suspensa eliminada.";
            }
            return deleted;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            ErrorMessage = SafeError(exception, "Não foi possível eliminar a venda suspensa.");
            return false;
        }
    }

    public void CloseActivePanel()
    {
        if (IsPaymentOpen)
        {
            IsPaymentOpen = false;
        }
        else if (IsSuspendedSalesOpen)
        {
            IsSuspendedSalesOpen = false;
        }
    }

    public void AcknowledgeSearchRefocus() => ShouldRefocusSearch = false;

    private bool TryBuildPayments(
        IReadOnlyCollection<PaymentEntryInput> payments,
        out SalePaymentRequest[] parsed,
        out long change)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        var requests = new List<SalePaymentRequest>();
        if (_cartLines.Count == 0)
        {
            errors["Carrinho"] = "Adiciona pelo menos um produto.";
        }
        if (_cartLines.Any(line => line.RequiresReview))
        {
            errors["Carrinho"] = "Revê as linhas sem stock suficiente antes de cobrar.";
        }
        if (payments is null || payments.Count == 0)
        {
            errors["Pagamentos"] = "Adiciona pelo menos um pagamento.";
        }
        else
        {
            int index = 0;
            foreach (PaymentEntryInput payment in payments)
            {
                index++;
                if (!Enum.IsDefined(payment.Method) ||
                    !TryWholeXof(payment.Amount, allowZero: false, out long amount))
                {
                    errors[$"Pagamento {index}"] = "Selecciona um método e indica um valor inteiro positivo.";
                    continue;
                }
                string? reference = string.IsNullOrWhiteSpace(payment.Reference)
                    ? null
                    : payment.Reference.Trim();
                if (reference?.Length > 160)
                {
                    errors[$"Pagamento {index}"] = "A referência não pode exceder 160 caracteres.";
                    continue;
                }
                requests.Add(new SalePaymentRequest(payment.Method, amount, reference));
            }
        }
        long paid = requests.Sum(payment => payment.AmountXof);
        change = Math.Max(0, paid - TotalXof);
        long cash = requests
            .Where(payment => payment.Method == PaymentMethod.Cash)
            .Sum(payment => payment.AmountXof);
        if (paid < TotalXof)
        {
            errors["Total pago"] = $"Faltam {FormatXof(TotalXof - paid)}.";
        }
        else if (change > cash)
        {
            errors["Total pago"] = "O troco não pode exceder o valor recebido em dinheiro.";
        }
        ValidationErrors = errors;
        parsed = requests.ToArray();
        return errors.Count == 0;
    }

    private string BuildCompletionSignature(IReadOnlyCollection<SalePaymentRequest> payments) =>
        string.Join(
            "|",
            _cartLines.Select(line =>
                $"{line.ProductId.Value:N}:{line.PackageId.Value:N}:{line.QuantityPackages}:{line.DiscountXof}")
                .Concat(payments.Select(payment =>
                    $"{(int)payment.Method}:{payment.AmountXof}:{payment.Reference}"))
                .Append($"discount:{_totalDiscountXof}"));

    private void InvalidateCompletion()
    {
        _completionKey = null;
        _completionSignature = null;
        LastReceipt = null;
        _changeXof = 0;
    }

    private void SetValidation(string key, string value)
    {
        var updated = ValidationErrors.ToDictionary(item => item.Key, item => item.Value);
        updated[key] = value;
        ValidationErrors = updated;
    }

    private void ClearValidation(string key)
    {
        ValidationErrors = ValidationErrors
            .Where(item => !string.Equals(item.Key, key, StringComparison.Ordinal))
            .ToDictionary(item => item.Key, item => item.Value);
    }

    private static bool TryWholeXof(string value, bool allowZero, out long amount)
    {
        bool parsed = long.TryParse(
            value?.Trim(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out amount);
        return parsed && (allowZero ? amount >= 0 : amount > 0);
    }

    private static string FormatXof(long amount) =>
        $"{amount.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ')} XOF";

    private static string SafeError(Exception exception, string fallback) => exception switch
    {
        SalesValidationException or SaleConflictException or SaleConcurrencyException or
        SaleOperationBlockedException => exception.Message,
        _ => fallback
    };
}
