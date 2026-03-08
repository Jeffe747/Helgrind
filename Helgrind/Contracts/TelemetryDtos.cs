namespace Helgrind.Contracts;

public sealed class TelemetrySummaryDto
{
    public bool Enabled { get; set; }

    public bool AlertingConfigured { get; set; }

    public string AlertMinimumRiskLevel { get; set; } = string.Empty;

    public DateTimeOffset? LastAlertSentUtc { get; set; }

    public DateTimeOffset? AlertCooldownUntilUtc { get; set; }

    public string AlertStatus { get; set; } = string.Empty;

    public DateTimeOffset GeneratedUtc { get; set; } = DateTimeOffset.UtcNow;

    public int WindowHours { get; set; }

    public int EventCount { get; set; }

    public int HighRiskEventCount { get; set; }

    public int UniqueSourceCount { get; set; }

    public DateTimeOffset? LatestEventUtc { get; set; }
}

public sealed class TelemetryEventPageDto
{
    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalCount { get; set; }

    public string RiskLevelFilter { get; set; } = "All";

    public string CategoryFilter { get; set; } = "All";

    public List<SuspiciousRequestEventDto> Events { get; set; } = [];
}

public sealed class SuspiciousRequestEventDto
{
    public long Id { get; set; }

    public DateTimeOffset OccurredUtc { get; set; }

    public string RemoteAddress { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    public string Method { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string QuerySummary { get; set; } = string.Empty;

    public int StatusCode { get; set; }

    public string? MatchedRouteId { get; set; }

    public string? MatchedClusterId { get; set; }

    public string Category { get; set; } = string.Empty;

    public string RiskLevel { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
}

public sealed class TelemetryTopSourceDto
{
    public string RemoteAddress { get; set; } = string.Empty;

    public int EventCount { get; set; }

    public DateTimeOffset LastSeenUtc { get; set; }

    public string HighestRiskLevel { get; set; } = string.Empty;
}

public sealed class TelemetryTopTargetDto
{
    public string Host { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public int EventCount { get; set; }

    public DateTimeOffset LastSeenUtc { get; set; }

    public string HighestRiskLevel { get; set; } = string.Empty;
}

public sealed class TelemetryTrendBucketDto
{
    public DateTimeOffset BucketUtc { get; set; }

    public int EventCount { get; set; }

    public int HighRiskEventCount { get; set; }
}