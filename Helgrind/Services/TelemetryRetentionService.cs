using Helgrind.Data;
using Helgrind.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Helgrind.Services;

public sealed class TelemetryRetentionService(
    HelgrindDbContext dbContext,
    IOptions<HelgrindOptions> options)
{
    public async Task<int> PruneExpiredEventsAsync(CancellationToken cancellationToken)
    {
        var retentionDays = Math.Max(1, options.Value.TelemetryRetentionDays);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
        var expiredEvents = await dbContext.SuspiciousRequestEvents
            .ToListAsync(cancellationToken);

        expiredEvents = expiredEvents
            .Where(eventEntity => eventEntity.OccurredUtc < cutoff)
            .ToList();

        if (expiredEvents.Count == 0)
        {
            return 0;
        }

        dbContext.SuspiciousRequestEvents.RemoveRange(expiredEvents);
        await dbContext.SaveChangesAsync(cancellationToken);
        return expiredEvents.Count;
    }
}