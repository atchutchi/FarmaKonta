using Nofarma.Contracts.Diagnostics;

namespace Nofarma.Sync;

public sealed class Worker(ILogger<Worker> logger) : BackgroundService
{
    private static readonly Action<ILogger, string, Exception?> LogServiceStarted =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1, nameof(ExecuteAsync)),
            "{Service} iniciado");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogServiceStarted(logger, ServiceNames.Sync, null);
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
