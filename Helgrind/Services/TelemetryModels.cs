namespace Helgrind.Services;

public sealed record SuspiciousRequestObservation(
    DateTimeOffset OccurredUtc,
    string RemoteAddress,
    string Host,
    string Method,
    string Path,
    string QuerySummary,
    int StatusCode,
    string? MatchedRouteId,
    string? MatchedClusterId,
    bool RouteMatched,
    bool PathMatched,
    bool HostMatched);

public sealed record SuspiciousRequestEventRecord(
    DateTimeOffset OccurredUtc,
    string RemoteAddress,
    string Host,
    string Method,
    string Path,
    string QuerySummary,
    int StatusCode,
    string? MatchedRouteId,
    string? MatchedClusterId,
    string Category,
    string RiskLevel,
    int RiskScore,
    string Reason);

public sealed record RouteMatchResult(
    bool Matched,
    string? RouteId,
    string? ClusterId,
    bool HostMatched,
    bool PathMatched)
{
    public static RouteMatchResult None { get; } = new(false, null, null, false, false);
}

public static class TelemetryRiskLevels
{
    public const int LowScore = 1;
    public const int MediumScore = 2;
    public const int HighScore = 3;

    public static string FromScore(int score) => score switch
    {
        >= HighScore => "High",
        >= MediumScore => "Medium",
        _ => "Low",
    };
}

public static class TelemetryPathUtility
{
    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        var normalized = path.StartsWith('/') ? path : "/" + path;
        return normalized.Length > 1 ? normalized.TrimEnd('/') : normalized;
    }

    public static bool Matches(string? requestPath, string? configuredPath)
        => string.Equals(NormalizePath(requestPath), NormalizePath(configuredPath), StringComparison.OrdinalIgnoreCase);
}