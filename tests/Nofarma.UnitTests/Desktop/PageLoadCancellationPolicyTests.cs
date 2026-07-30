using Nofarma.Desktop.Services;

namespace Nofarma.UnitTests.Desktop;

public sealed class PageLoadCancellationPolicyTests
{
    [Fact]
    public async Task LifetimeCancellationDuringLoadDoesNotEscapeThePageEventBoundary()
    {
        using var lifetime = new CancellationTokenSource();

        await PageLoadCancellationPolicy.RunAsync(
            async cancellationToken =>
            {
                lifetime.Cancel();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            },
            lifetime.Token);
    }

    [Fact]
    public async Task CancellationNotOwnedByThePageLifetimeStillPropagates()
    {
        using var unrelated = new CancellationTokenSource();
        unrelated.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PageLoadCancellationPolicy.RunAsync(
                _ => Task.FromCanceled(unrelated.Token),
                CancellationToken.None));
    }
}
