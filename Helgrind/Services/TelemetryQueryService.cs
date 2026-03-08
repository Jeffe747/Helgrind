using Helgrind.Contracts;
using Helgrind.Data;
using Helgrind.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Helgrind.Services;

public sealed class TelemetryQueryService(
    HelgrindDbContext dbContext,
    IOptions<HelgrindOptions> options,
    TelemetryAlertService telemetryAlertService)
{
    public async Task<TelemetrySummaryDto> GetSummaryAsync(int hours, CancellationToken cancellationToken)
    {
        var since = GetWindowStart(hours);
        var events = await GetWindowedEventsAsync(since, cancellationToken);
        var alertSnapshot = telemetryAlertService.GetSnapshot();

        return new TelemetrySummaryDto
        {
            Enabled = options.Value.TelemetryEnabled,
            AlertingConfigured = alertSnapshot.IsConfigured,
            AlertMinimumRiskLevel = alertSnapshot.MinimumRiskLevel,
            LastAlertSentUtc = alertSnapshot.LastAlertSentUtc,
            AlertCooldownUntilUtc = alertSnapshot.CooldownUntilUtc,
            AlertStatus = alertSnapshot.Status,
            GeneratedUtc = DateTimeOffset.UtcNow,
            WindowHours = Math.Max(1, hours),
            EventCount = events.Count,
            HighRiskEventCount = events.Count(eventEntity => eventEntity.RiskScore >= TelemetryRiskLevels.HighScore),
            UniqueSourceCount = events.Select(eventEntity => eventEntity.RemoteAddress).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            LatestEventUtc = events.Count == 0 ? null : events.Max(eventEntity => eventEntity.OccurredUtc),
        };
    }

    public async Task<TelemetryEventPageDto> GetEventsAsync(int hours, int page, int pageSize, string? riskLevel, string? category, CancellationToken cancellationToken)
    {
        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(pageSize, 1, Math.Max(1, options.Value.TelemetryMaxEventPageSize));
        var since = GetWindowStart(hours);
        var events = await GetWindowedEventsAsync(since, cancellationToken);
        var safeRiskLevel = NormalizeFilter(riskLevel);
        var safeCategory = NormalizeFilter(category);

        events = events
            .Where(eventEntity => FilterRiskLevel(eventEntity, safeRiskLevel))
            .Where(eventEntity => FilterCategory(eventEntity, safeCategory))
            .OrderByDescending(eventEntity => eventEntity.OccurredUtc)
            .ThenByDescending(eventEntity => eventEntity.Id)
            .ToList();

        var totalCount = events.Count;
        var pageEvents = events
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .Select(eventEntity => new SuspiciousRequestEventDto
            {
                Id = eventEntity.Id,
                OccurredUtc = eventEntity.OccurredUtc,
                RemoteAddress = eventEntity.RemoteAddress,
                Host = eventEntity.Host,
                Method = eventEntity.Method,
                Path = eventEntity.Path,
                QuerySummary = eventEntity.QuerySummary,
                StatusCode = eventEntity.StatusCode,
                MatchedRouteId = eventEntity.MatchedRouteId,
                MatchedClusterId = eventEntity.MatchedClusterId,
                Category = eventEntity.Category,
                RiskLevel = eventEntity.RiskLevel,
                Reason = eventEntity.Reason,
            })
            .ToList();

        return new TelemetryEventPageDto
        {
            Page = safePage,
            PageSize = safePageSize,
            TotalCount = totalCount,
            RiskLevelFilter = safeRiskLevel,
            CategoryFilter = safeCategory,
            Events = pageEvents,
        };
    }

    public async Task<List<TelemetryTopSourceDto>> GetTopSourcesAsync(int hours, int limit, CancellationToken cancellationToken)
    {
        var since = GetWindowStart(hours);
        var events = await GetWindowedEventsAsync(since, cancellationToken);

        return events
            .GroupBy(eventEntity => eventEntity.RemoteAddress)
            .Select(group => new
            {
                RemoteAddress = group.Key,
                EventCount = group.Count(),
                LastSeenUtc = group.Max(eventEntity => eventEntity.OccurredUtc),
                HighestRiskScore = group.Max(eventEntity => eventEntity.RiskScore),
            })
            .OrderByDescending(group => group.EventCount)
            .ThenByDescending(group => group.HighestRiskScore)
            .ThenBy(group => group.RemoteAddress)
            .Take(Math.Clamp(limit, 1, 50))
            .Select(group => new TelemetryTopSourceDto
            {
                RemoteAddress = group.RemoteAddress,
                EventCount = group.EventCount,
                LastSeenUtc = group.LastSeenUtc,
                HighestRiskLevel = TelemetryRiskLevels.FromScore(group.HighestRiskScore),
            })
            .ToList();
    }

    public async Task<List<TelemetryTopTargetDto>> GetTopTargetsAsync(int hours, int limit, CancellationToken cancellationToken)
    {
        var since = GetWindowStart(hours);
        var events = await GetWindowedEventsAsync(since, cancellationToken);

        return events
            .GroupBy(eventEntity => new { eventEntity.Host, eventEntity.Path })
            .Select(group => new
            {
                Host = group.Key.Host,
                Path = group.Key.Path,
                EventCount = group.Count(),
                LastSeenUtc = group.Max(eventEntity => eventEntity.OccurredUtc),
                HighestRiskScore = group.Max(eventEntity => eventEntity.RiskScore),
            })
            .OrderByDescending(group => group.EventCount)
            .ThenByDescending(group => group.HighestRiskScore)
            .ThenBy(group => group.Host)
            .ThenBy(group => group.Path)
            .Take(Math.Clamp(limit, 1, 50))
            .Select(group => new TelemetryTopTargetDto
            {
                Host = group.Host,
                Path = group.Path,
                EventCount = group.EventCount,
                LastSeenUtc = group.LastSeenUtc,
                HighestRiskLevel = TelemetryRiskLevels.FromScore(group.HighestRiskScore),
            })
            .ToList();
    }

    public async Task<List<TelemetryTrendBucketDto>> GetTrendAsync(int hours, int bucketMinutes, CancellationToken cancellationToken)
    {
        var safeBucketMinutes = Math.Clamp(bucketMinutes, 1, 240);
        var since = GetWindowStart(hours);
        var events = await GetWindowedEventsAsync(since, cancellationToken);

        return events
            .GroupBy(eventEntity => Bucket(eventEntity.OccurredUtc, safeBucketMinutes))
            .OrderBy(group => group.Key)
            .Select(group => new TelemetryTrendBucketDto
            {
                BucketUtc = group.Key,
                EventCount = group.Count(),
                HighRiskEventCount = group.Count(eventEntity => eventEntity.RiskScore >= TelemetryRiskLevels.HighScore),
            })
            .ToList();
    }

    private static DateTimeOffset Bucket(DateTimeOffset occurredUtc, int bucketMinutes)
    {
        var utc = occurredUtc.ToUniversalTime();
        var truncatedMinutes = utc.Minute - (utc.Minute % bucketMinutes);
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, truncatedMinutes, 0, TimeSpan.Zero);
    }

    private static DateTimeOffset GetWindowStart(int hours) => DateTimeOffset.UtcNow.AddHours(-Math.Max(1, hours));

    private async Task<List<SuspiciousRequestEventEntity>> GetWindowedEventsAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        var events = await dbContext.SuspiciousRequestEvents
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return events
            .Where(eventEntity => eventEntity.OccurredUtc >= since)
            .ToList();
    }

    private static string NormalizeFilter(string? value)
        => string.IsNullOrWhiteSpace(value) ? "All" : value.Trim();

    private static bool FilterRiskLevel(SuspiciousRequestEventEntity eventEntity, string riskLevel)
        => riskLevel.Equals("All", StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventEntity.RiskLevel, riskLevel, StringComparison.OrdinalIgnoreCase);

    private static bool FilterCategory(SuspiciousRequestEventEntity eventEntity, string category)
        => category.Equals("All", StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventEntity.Category, category, StringComparison.OrdinalIgnoreCase);
}