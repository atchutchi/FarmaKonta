using Nofarma.Application.Identity.Authentication;
using Nofarma.Application.Supply;

namespace Nofarma.Desktop.ViewModels;

public interface ISupplierPageOperations
{
    Task<IReadOnlyList<SupplierSummary>> SearchAsync(string query, CancellationToken cancellationToken);
    Task CreateAsync(CreateSupplierRequest request, CancellationToken cancellationToken);
}

public sealed class SupplierPageOperations(
    SupplierService suppliers,
    CurrentSession currentSession) : ISupplierPageOperations
{
    public Task<IReadOnlyList<SupplierSummary>> SearchAsync(string query, CancellationToken cancellationToken) =>
        suppliers.SearchAsync(RequireSession(), query, cancellationToken);

    public async Task CreateAsync(CreateSupplierRequest request, CancellationToken cancellationToken) =>
        _ = await suppliers.CreateAsync(RequireSession(), request, cancellationToken);

    private LocalSession RequireSession() => currentSession.Active
        ?? throw new InvalidOperationException("Não existe uma sessão activa.");
}

public sealed record SupplierEditorInput(
    string Name,
    string? TaxIdentifier,
    string? Phone,
    string? Email,
    string? Address,
    string? Notes);

public sealed class SuppliersViewModel(ISupplierPageOperations operations)
{
    private CancellationTokenSource? _searchCancellation;
    private int _searchVersion;

    public IReadOnlyList<SupplierSummary> Suppliers { get; private set; } = [];
    public IReadOnlyDictionary<string, string> ValidationErrors { get; private set; } =
        new Dictionary<string, string>();
    public bool IsLoading { get; private set; }
    public bool IsSaving { get; private set; }
    public bool IsEmpty => !IsLoading && Suppliers.Count == 0;
    public string? ErrorMessage { get; private set; }

    public Task LoadAsync(CancellationToken cancellationToken) =>
        SearchAsync(string.Empty, cancellationToken);

    public async Task SearchAsync(string? query, CancellationToken cancellationToken)
    {
        int version = Interlocked.Increment(ref _searchVersion);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationTokenSource? previous = Interlocked.Exchange(ref _searchCancellation, linked);
        previous?.Cancel();
        previous?.Dispose();
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            IReadOnlyList<SupplierSummary> results = await operations.SearchAsync(
                query?.Trim() ?? string.Empty,
                linked.Token);
            if (version == _searchVersion) Suppliers = results;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            if (version == _searchVersion)
            {
                ErrorMessage = "Não foi possível carregar os fornecedores. Tenta novamente.";
            }
        }
        finally
        {
            if (version == _searchVersion) IsLoading = false;
        }
    }

    public async Task<bool> CreateAsync(SupplierEditorInput input, CancellationToken cancellationToken)
    {
        if (IsSaving) return false;
        ArgumentNullException.ThrowIfNull(input);
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(input.Name)) errors["Nome"] = "Indica o nome do fornecedor.";
        if (!string.IsNullOrWhiteSpace(input.Email) && !input.Email.Contains('@', StringComparison.Ordinal))
        {
            errors["Email"] = "Indica um endereço de email válido.";
        }

        ValidationErrors = errors;
        if (errors.Count > 0) return false;
        IsSaving = true;
        ErrorMessage = null;
        try
        {
            await operations.CreateAsync(new CreateSupplierRequest(
                input.Name.Trim(), input.TaxIdentifier?.Trim(), input.Phone?.Trim(),
                input.Email?.Trim(), input.Address?.Trim(), input.Notes?.Trim()), cancellationToken);
            await SearchAsync(string.Empty, cancellationToken);
            return true;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            ErrorMessage = "Não foi possível guardar o fornecedor. Revê os dados e tenta novamente.";
            return false;
        }
        finally
        {
            IsSaving = false;
        }
    }
}
