using Helgrind.Options;
using Helgrind.Services;

namespace Helgrind.Tests;

public sealed class TelemetryClassifierServiceTests
{
    [Fact]
    public void Classify_ReturnsHighRiskExploitPathEvent_ForCommonProbePath()
    {
        var service = CreateService();

        var result = service.Classify(new SuspiciousRequestObservation(
            DateTimeOffset.UtcNow,
            "203.0.113.10",
            "edge.example.com",
            "GET",
            "/.env",
            string.Empty,
            404,
            null,
            null,
            false,
            false,
            false));

        Assert.NotNull(result);
        Assert.Equal("ExploitPath", result.Category);
        Assert.Equal("High", result.RiskLevel);
        Assert.Contains("/.env", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_ReturnsNull_ForNormalMatchedTraffic()
    {
        var service = CreateService();

        var result = service.Classify(new SuspiciousRequestObservation(
            DateTimeOffset.UtcNow,
            "203.0.113.10",
            "api.example.com",
            "GET",
            "/",
            string.Empty,
            200,
            "route1",
            "cluster1",
            true,
            true,
            true));

        Assert.Null(result);
    }

    [Fact]
    public void Classify_PromotesBurstCategory_WhenThresholdIsReached()
    {
        var service = CreateService(new HelgrindOptions
        {
            TelemetryEnabled = true,
            TelemetryBurstThreshold = 3,
            TelemetryBurstWindowSeconds = 60,
        });
        var now = DateTimeOffset.UtcNow;

        service.Classify(CreateRouteMissObservation(now, "198.51.100.20"));
        service.Classify(CreateRouteMissObservation(now.AddSeconds(1), "198.51.100.20"));
        var result = service.Classify(CreateRouteMissObservation(now.AddSeconds(2), "198.51.100.20"));

        Assert.NotNull(result);
        Assert.Equal("Burst", result.Category);
        Assert.Contains("exceeded 3 suspicious requests", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_ReturnsSmokeTestEvent_ForConfiguredSmokePath()
    {
        var service = CreateService(new HelgrindOptions
        {
            TelemetryEnabled = true,
            TelemetrySmokePath = "/__helgrind/telemetry/smoke",
        });

        var result = service.Classify(new SuspiciousRequestObservation(
            DateTimeOffset.UtcNow,
            "203.0.113.10",
            "edge.example.com",
            "GET",
            "/__helgrind/telemetry/smoke",
            string.Empty,
            404,
            null,
            null,
            false,
            false,
            false));

        Assert.NotNull(result);
        Assert.Equal("SmokeTest", result.Category);
        Assert.Equal("Low", result.RiskLevel);
    }

    private static SuspiciousRequestObservation CreateRouteMissObservation(DateTimeOffset occurredUtc, string remoteAddress)
        => new(
            occurredUtc,
            remoteAddress,
            "edge.example.com",
            "GET",
            "/missing",
            string.Empty,
            404,
            null,
            null,
            false,
            false,
            false);

    private static TelemetryClassifierService CreateService(HelgrindOptions? options = null)
        => new(Microsoft.Extensions.Options.Options.Create(options ?? new HelgrindOptions { TelemetryEnabled = true }), new TelemetryRateTracker());
}