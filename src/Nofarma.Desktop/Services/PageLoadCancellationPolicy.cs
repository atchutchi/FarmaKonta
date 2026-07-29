namespace Nofarma.Desktop.Services;

public static class PageLoadCancellationPolicy
{
    public static async Task RunAsync(
        Func<CancellationToken, Task> load,
        CancellationToken lifetimeCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(load);
        try
        {
            await load(lifetimeCancellationToken);
        }
        catch (OperationCanceledException) when (lifetimeCancellationToken.IsCancellationRequested)
        {
        }
    }
}
