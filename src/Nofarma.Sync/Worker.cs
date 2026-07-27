namespace Nofarma.Sync;

public sealed class Worker(ILogger<Worker> logger) : BackgroundService
{
    private static readonly Action<ILogger, DateTimeOffset, Exception?> LogWorkerRunning =
        LoggerMessage.Define<DateTimeOffset>(
            LogLevel.Information,
            new EventId(1, nameof(ExecuteAsync)),
            "Worker running at: {Time}");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                LogWorkerRunning(logger, DateTimeOffset.Now, null);
            }
            await Task.Delay(1000, stoppingToken);
        }
    }
}
