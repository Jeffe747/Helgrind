using Helgrind.Options;
using Microsoft.Extensions.Options;

namespace Helgrind.Services;

public sealed class TelemetryClassifierService(
    IOptions<HelgrindOptions> options,
    TelemetryRateTracker rateTracker)
{
    private static readonly string[] ExploitPathNeedles =
    [
        "/.env",
        "/.git",
        "/wp-admin",
        "/wp-login",
        "/phpmyadmin",
        "/cgi-bin",
        "/server-status",
        "/boaform",
    ];

    private static readonly HashSet<string> SuspiciousMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "TRACE",
        "CONNECT",
        "TRACK"
    };

    public SuspiciousRequestEventRecord? Classify(SuspiciousRequestObservation observation)
    {
        if (!options.Value.TelemetryEnabled)
        {
            return null;
        }

        if (TelemetryPathUtility.Matches(observation.Path, options.Value.TelemetrySmokePath))
        {
            return new SuspiciousRequestEventRecord(
                observation.OccurredUtc,
                observation.RemoteAddress,
                observation.Host,
                observation.Method,
                observation.Path,
                observation.QuerySummary,
                observation.StatusCode,
                observation.MatchedRouteId,
                observation.MatchedClusterId,
                "SmokeTest",
                "Low",
                TelemetryRiskLevels.LowScore,
                "Telemetry smoke test request was received on the public listener.");
        }

        var signals = new List<(string Category, int RiskScore, string Reason)>();

        if (SuspiciousMethods.Contains(observation.Method))
        {
            signals.Add(("UnsupportedMethod", TelemetryRiskLevels.HighScore, $"Method {observation.Method} is not expected on the public proxy."));
        }

        var matchedExploitNeedle = ExploitPathNeedles.FirstOrDefault(needle =>
            observation.Path.Contains(needle, StringComparison.OrdinalIgnoreCase));
        if (matchedExploitNeedle is not null)
        {
            signals.Add(("ExploitPath", TelemetryRiskLevels.HighScore, $"Path matches common probe target {matchedExploitNeedle}."));
        }

        if (!observation.RouteMatched && observation.StatusCode == StatusCodes.Status404NotFound)
        {
            signals.Add(("RouteMiss", TelemetryRiskLevels.MediumScore, "Request did not match a configured public route."));
        }

        if (!observation.RouteMatched && observation.PathMatched && !observation.HostMatched)
        {
            signals.Add(("HostMismatch", TelemetryRiskLevels.MediumScore, "Path resembled a configured route, but the host header did not match any configured public host."));
        }

        if (!observation.RouteMatched && (observation.StatusCode == StatusCodes.Status400BadRequest || observation.StatusCode == StatusCodes.Status405MethodNotAllowed))
        {
            signals.Add(("ProtocolAnomaly", TelemetryRiskLevels.MediumScore, $"Response status {observation.StatusCode} suggests malformed or unsupported public traffic."));
        }

        var burstCandidate = signals.Count > 0 || !observation.RouteMatched;
        if (burstCandidate && rateTracker.RegisterAndCheckBurst(
            observation.RemoteAddress,
            observation.OccurredUtc,
            TimeSpan.FromSeconds(Math.Max(1, options.Value.TelemetryBurstWindowSeconds)),
            Math.Max(2, options.Value.TelemetryBurstThreshold)))
        {
            signals.Add(("Burst", TelemetryRiskLevels.MediumScore, $"Source exceeded {options.Value.TelemetryBurstThreshold} suspicious requests within {options.Value.TelemetryBurstWindowSeconds} seconds."));
        }

        if (signals.Count == 0)
        {
            return null;
        }

        var primarySignal = signals
            .OrderByDescending(signal => signal.RiskScore)
            .ThenBy(signal => signal.Category, StringComparer.OrdinalIgnoreCase)
            .First();

        return new SuspiciousRequestEventRecord(
            observation.OccurredUtc,
            observation.RemoteAddress,
            observation.Host,
            observation.Method,
            observation.Path,
            observation.QuerySummary,
            observation.StatusCode,
            observation.MatchedRouteId,
            observation.MatchedClusterId,
            primarySignal.Category,
            TelemetryRiskLevels.FromScore(primarySignal.RiskScore),
            primarySignal.RiskScore,
            string.Join(" ", signals.Select(signal => signal.Reason).Distinct(StringComparer.Ordinal)));
    }
}