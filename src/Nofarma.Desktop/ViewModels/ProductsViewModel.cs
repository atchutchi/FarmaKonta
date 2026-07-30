using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Nofarma.Application.Catalog;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Domain.Catalog;
using Nofarma.Domain.Common;

namespace Nofarma.Desktop.ViewModels;

public interface IProductPageOperations
{
    Task<IReadOnlyList<ProductSummary>> SearchAsync(string query, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductCategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken);
    Task CreateAsync(CreateProductRequest request, CancellationToken cancellationToken);
}

public sealed class ProductPageOperations(
    ProductService products,
    CurrentSession currentSession) : IProductPageOperations
{
    public Task<IReadOnlyList<ProductSummary>> SearchAsync(string query, CancellationToken cancellationToken) =>
        products.SearchAsync(RequireSession(), query, cancellationToken);

    public Task<IReadOnlyList<ProductCategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken) =>
        products.ListCategoriesAsync(RequireSession(), cancellationToken);

    public async Task CreateAsync(CreateProductRequest request, CancellationToken cancellationToken) =>
        _ = await products.CreateAsync(RequireSession(), request, cancellationToken);

    private LocalSession RequireSession() => currentSession.Active
        ?? throw new InvalidOperationException("Não existe uma sessão activa.");
}

public sealed record ProductEditorInput(
    string? Code,
    string Name,
    EntityId? CategoryId,
    string BaseUnit,
    ProductType Type,
    string SalePriceXof,
    string PurchasePriceXof,
    string MinimumStockBase,
    bool RequiresPrescription,
    bool RequiresLot,
    bool RequiresExpiry);

public sealed class ProductsViewModel(IProductPageOperations operations)
{
    private CancellationTokenSource? _searchCancellation;
    private int _searchVersion;

    public IReadOnlyList<ProductSummary> Products { get; private set; } = [];
    public IReadOnlyList<ProductCategorySummary> Categories { get; private set; } = [];
    public IReadOnlyDictionary<string, string> ValidationErrors { get; private set; } =
        new Dictionary<string, string>();
    public bool IsLoading { get; private set; }
    public bool IsSaving { get; private set; }
    public bool IsEmpty => !IsLoading && Products.Count == 0;
    public string? ErrorMessage { get; private set; }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        Categories = await operations.ListCategoriesAsync(cancellationToken);
        await SearchAsync(string.Empty, cancellationToken);
    }

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
            IReadOnlyList<ProductSummary> results = await operations.SearchAsync(
                query?.Trim() ?? string.Empty,
                linked.Token);
            if (version == _searchVersion)
            {
                Products = results;
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            if (version == _searchVersion)
            {
                ErrorMessage = "Não foi possível carregar os produtos. Tenta novamente.";
            }
        }
        finally
        {
            if (version == _searchVersion)
            {
                IsLoading = false;
            }
        }
    }

    public async Task<bool> CreateAsync(ProductEditorInput input, CancellationToken cancellationToken)
    {
        if (IsSaving)
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(input);
        if (!TryBuildRequest(input, out CreateProductRequest? request))
        {
            return false;
        }

        IsSaving = true;
        ErrorMessage = null;
        try
        {
            await operations.CreateAsync(request, cancellationToken);
            await SearchAsync(string.Empty, cancellationToken);
            return true;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            ErrorMessage = "Não foi possível guardar o produto. Revê os dados e tenta novamente.";
            return false;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private bool TryBuildRequest(
        ProductEditorInput input,
        [NotNullWhen(true)] out CreateProductRequest? request)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(input.Name)) errors["Nome"] = "Indica o nome comercial.";
        if (input.CategoryId is null) errors["Categoria"] = "Escolhe uma categoria.";
        if (string.IsNullOrWhiteSpace(input.BaseUnit)) errors["Unidade base"] = "Indica a unidade base.";
        long sale = ParseNonNegative(input.SalePriceXof, "Preço de venda", errors);
        long purchase = ParseNonNegative(input.PurchasePriceXof, "Preço de compra", errors);
        long? minimum = ParseOptionalNonNegative(input.MinimumStockBase, "Stock mínimo", errors);
        ValidationErrors = errors;
        if (errors.Count > 0 || input.CategoryId is null)
        {
            request = null;
            return false;
        }

        request = new CreateProductRequest(
            input.Code,
            input.Name.Trim(),
            input.CategoryId.Value,
            input.BaseUnit.Trim(),
            input.Type,
            sale,
            purchase,
            minimum,
            input.RequiresPrescription,
            input.RequiresLot,
            input.RequiresExpiry);
        return true;
    }

    private static long ParseNonNegative(
        string value,
        string field,
        IDictionary<string, string> errors)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long result)
            && result >= 0)
        {
            return result;
        }

        errors[field] = "Usa um valor inteiro igual ou superior a zero.";
        return 0;
    }

    private static long? ParseOptionalNonNegative(
        string value,
        string field,
        IDictionary<string, string> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return ParseNonNegative(value, field, errors);
    }
}
