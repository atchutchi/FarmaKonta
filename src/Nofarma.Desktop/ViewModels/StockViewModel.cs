using Nofarma.Application.Identity.Authentication;
using Nofarma.Application.Inventory;

namespace Nofarma.Desktop.ViewModels;

public interface IStockPageOperations
{
    Task<StockOverview> SearchAsync(string query, CancellationToken cancellationToken);
}

public sealed class StockPageOperations(
    InventoryQueryService inventory,
    CurrentSession currentSession) : IStockPageOperations
{
    public Task<StockOverview> SearchAsync(string query, CancellationToken cancellationToken) =>
        inventory.SearchAsync(currentSession.Active
            ?? throw new InvalidOperationException("Não existe uma sessão activa."), query, cancellationToken);
}

public sealed class StockViewModel(IStockPageOperations operations)
{
    private CancellationTokenSource? _searchCancellation;
    private int _searchVersion;

    public IReadOnlyList<StockOverviewItem> Items { get; private set; } = [];
    public int LowStockProducts { get; private set; }
    public int OutOfStockProducts { get; private set; }
    public int ExpiryAttentionLots { get; private set; }
    public bool IsLoading { get; private set; }
    public bool IsEmpty => !IsLoading && Items.Count == 0;
    public string? ErrorMessage { get; private set; }

    public Task LoadAsync(CancellationToken cancellationToken) => SearchAsync(string.Empty, cancellationToken);

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
            StockOverview result = await operations.SearchAsync(query?.Trim() ?? string.Empty, linked.Token);
            if (version != _searchVersion) return;
            Items = result.Items;
            LowStockProducts = result.LowStockProducts;
            OutOfStockProducts = result.OutOfStockProducts;
            ExpiryAttentionLots = result.ExpiryAttentionLots;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            if (version == _searchVersion)
            {
                ErrorMessage = "Não foi possível carregar o stock. Tenta novamente.";
            }
        }
        finally
        {
            if (version == _searchVersion) IsLoading = false;
        }
    }
}
