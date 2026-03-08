using Helgrind.Data;
using Microsoft.EntityFrameworkCore;

namespace Helgrind.Services;

public sealed class TelemetryBackgroundService(
    IServiceScopeFactory serviceScopeFactory,
    TelemetryEventSink eventSink,
    TelemetryAlertService alertService,
    ILogger<TelemetryBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnsureDatabaseAsync(stoppingToken);
        var nextPruneUtc = DateTimeOffset.UtcNow.AddMinutes(15);

        while (!stoppingToken.IsCancellationRequested)
        {
            SuspiciousRequestEventRecord telemetryEvent;
            try
            {
                telemetryEvent = await eventSink.Reader.ReadAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var batch = new List<SuspiciousRequestEventRecord> { telemetryEvent };

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), stoppingToken);
            }
            catch (OperationCanceledException)
            {
            }

            while (batch.Count < 50 && eventSink.Reader.TryRead(out var bufferedEvent))
            {
                batch.Add(bufferedEvent);
            }

            await PersistBatchAsync(batch, stoppingToken);
            await alertService.TrySendAlertAsync(batch, stoppingToken);

            if (DateTimeOffset.UtcNow >= nextPruneUtc)
            {
                await PruneExpiredEventsAsync(stoppingToken);
                nextPruneUtc = DateTimeOffset.UtcNow.AddMinutes(15);
            }
        }
    }

    private async Task EnsureDatabaseAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HelgrindDbContext>();
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
    }

    private async Task PersistBatchAsync(List<SuspiciousRequestEventRecord> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return;
        }

        try
        {
            await using var scope = serviceScopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<HelgrindDbContext>();
            dbContext.SuspiciousRequestEvents.AddRange(batch.Select(eventRecord => new SuspiciousRequestEventEntity
            {
                OccurredUtc = eventRecord.OccurredUtc,
                RemoteAddress = eventRecord.RemoteAddress,
                Host = eventRecord.Host,
                Method = eventRecord.Method,
                Path = eventRecord.Path,
                QuerySummary = eventRecord.QuerySummary,
                StatusCode = eventRecord.StatusCode,
                MatchedRouteId = eventRecord.MatchedRouteId,
                MatchedClusterId = eventRecord.MatchedClusterId,
                Category = eventRecord.Category,
                RiskLevel = eventRecord.RiskLevel,
                RiskScore = eventRecord.RiskScore,
                Reason = eventRecord.Reason,
            }));
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to persist telemetry batch containing {Count} suspicious request events.", batch.Count);
        }
    }

    private async Task PruneExpiredEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = serviceScopeFactory.CreateAsyncScope();
            var retentionService = scope.ServiceProvider.GetRequiredService<TelemetryRetentionService>();
            var deletedCount = await retentionService.PruneExpiredEventsAsync(cancellationToken);
            if (deletedCount > 0)
            {
                logger.LogInformation("Pruned {Count} expired telemetry events.", deletedCount);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to prune expired telemetry events.");
        }
    }
}