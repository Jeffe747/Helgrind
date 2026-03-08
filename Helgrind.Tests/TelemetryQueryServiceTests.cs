using Helgrind.Data;
using Helgrind.Options;
using Helgrind.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Helgrind.Tests;

public sealed class TelemetryQueryServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public TelemetryQueryServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    [Fact]
    public async Task QueryServices_ReturnExpectedSummarySourcesTargetsAndTrends()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;
        dbContext.SuspiciousRequestEvents.AddRange(
        [
            CreateEvent(now.AddMinutes(-5), "203.0.113.10", "edge.example.com", "/.env", "ExploitPath", TelemetryRiskLevels.HighScore),
            CreateEvent(now.AddMinutes(-4), "203.0.113.10", "edge.example.com", "/.env", "ExploitPath", TelemetryRiskLevels.HighScore),
            CreateEvent(now.AddMinutes(-3), "198.51.100.25", "api.example.com", "/missing", "RouteMiss", TelemetryRiskLevels.MediumScore),
            CreateEvent(now.AddMinutes(-2), "198.51.100.30", "api.example.com", "/missing", "Burst", TelemetryRiskLevels.MediumScore),
        ]);
        await dbContext.SaveChangesAsync();

        var queryService = CreateQueryService(dbContext);

        var summary = await queryService.GetSummaryAsync(24, CancellationToken.None);
        var events = await queryService.GetEventsAsync(24, 1, 10, null, null, CancellationToken.None);
        var topSources = await queryService.GetTopSourcesAsync(24, 5, CancellationToken.None);
        var topTargets = await queryService.GetTopTargetsAsync(24, 5, CancellationToken.None);
        var trends = await queryService.GetTrendAsync(24, 60, CancellationToken.None);

        Assert.True(summary.Enabled);
        Assert.Equal(4, summary.EventCount);
        Assert.Equal(2, summary.HighRiskEventCount);
        Assert.Equal(3, summary.UniqueSourceCount);
        Assert.Equal(4, events.TotalCount);
        Assert.Equal(4, events.Events.Count);
        Assert.Equal("203.0.113.10", topSources[0].RemoteAddress);
        Assert.Equal(2, topSources[0].EventCount);
        Assert.Equal("High", topSources[0].HighestRiskLevel);
        Assert.Equal("/.env", topTargets[0].Path);
        Assert.Equal(2, topTargets[0].EventCount);
        Assert.NotEmpty(trends);
        Assert.Equal(4, trends.Sum(bucket => bucket.EventCount));
        Assert.False(summary.AlertingConfigured);
    }

    [Fact]
    public async Task GetEventsAsync_AppliesRiskAndCategoryFilters()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;
        dbContext.SuspiciousRequestEvents.AddRange(
        [
            CreateEvent(now.AddMinutes(-5), "203.0.113.10", "edge.example.com", "/.env", "ExploitPath", TelemetryRiskLevels.HighScore),
            CreateEvent(now.AddMinutes(-4), "203.0.113.10", "edge.example.com", "/.git", "ExploitPath", TelemetryRiskLevels.HighScore),
            CreateEvent(now.AddMinutes(-3), "198.51.100.25", "api.example.com", "/missing", "RouteMiss", TelemetryRiskLevels.MediumScore)
        ]);
        await dbContext.SaveChangesAsync();

        var queryService = CreateQueryService(dbContext);

        var filtered = await queryService.GetEventsAsync(24, 1, 10, "High", "ExploitPath", CancellationToken.None);

        Assert.Equal(2, filtered.TotalCount);
        Assert.All(filtered.Events, eventItem => Assert.Equal("High", eventItem.RiskLevel));
        Assert.All(filtered.Events, eventItem => Assert.Equal("ExploitPath", eventItem.Category));
    }

    [Fact]
    public async Task RetentionService_PrunesEventsOlderThanConfiguredWindow()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.SuspiciousRequestEvents.AddRange(
        [
            CreateEvent(DateTimeOffset.UtcNow.AddDays(-45), "203.0.113.10", "edge.example.com", "/old", "RouteMiss", TelemetryRiskLevels.MediumScore),
            CreateEvent(DateTimeOffset.UtcNow.AddDays(-2), "203.0.113.11", "edge.example.com", "/new", "RouteMiss", TelemetryRiskLevels.MediumScore),
        ]);
        await dbContext.SaveChangesAsync();

        var retentionService = new TelemetryRetentionService(
            dbContext,
            Microsoft.Extensions.Options.Options.Create(new HelgrindOptions
            {
                TelemetryEnabled = true,
                TelemetryRetentionDays = 30,
            }));

        var deletedCount = await retentionService.PruneExpiredEventsAsync(CancellationToken.None);
        var remainingCount = await dbContext.SuspiciousRequestEvents.CountAsync();

        Assert.Equal(1, deletedCount);
        Assert.Equal(1, remainingCount);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private HelgrindDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HelgrindDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new HelgrindDbContext(options);
    }

    private static TelemetryQueryService CreateQueryService(HelgrindDbContext dbContext)
        => new(
            dbContext,
            Microsoft.Extensions.Options.Options.Create(new HelgrindOptions
            {
                TelemetryEnabled = true,
                TelemetryMaxEventPageSize = 100,
            }),
            new TelemetryAlertService(
                new HttpClient(new StubHttpMessageHandler()),
                Microsoft.Extensions.Options.Options.Create(new HelgrindOptions()),
                TimeProvider.System,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<TelemetryAlertService>.Instance));

    private static SuspiciousRequestEventEntity CreateEvent(
        DateTimeOffset occurredUtc,
        string remoteAddress,
        string host,
        string path,
        string category,
        int riskScore)
        => new()
        {
            OccurredUtc = occurredUtc,
            RemoteAddress = remoteAddress,
            Host = host,
            Method = "GET",
            Path = path,
            QuerySummary = string.Empty,
            StatusCode = 404,
            Category = category,
            RiskLevel = TelemetryRiskLevels.FromScore(riskScore),
            RiskScore = riskScore,
            Reason = category,
        };

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));
    }
}