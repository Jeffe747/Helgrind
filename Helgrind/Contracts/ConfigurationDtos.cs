namespace Helgrind.Contracts;

public sealed class HelgrindConfigurationDto
{
    public List<RouteDto> Routes { get; set; } = [];

    public List<ClusterDto> Clusters { get; set; } = [];

    public SettingsDto Settings { get; set; } = new();
}

public sealed class RouteDto
{
    public string RouteId { get; set; } = string.Empty;

    public string ClusterId { get; set; } = string.Empty;

    public string Path { get; set; } = "{**catch-all}";

    public List<string> Hosts { get; set; } = [];

    public int? Order { get; set; }
}

public sealed class ClusterDto
{
    public string ClusterId { get; set; } = string.Empty;

    public string? LoadBalancingPolicy { get; set; }

    public HealthCheckDto HealthCheck { get; set; } = new();

    public int? ConsecutiveFailuresThreshold { get; set; }

    public List<DestinationDto> Destinations { get; set; } = [];
}

public sealed class DestinationDto
{
    public string DestinationId { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;
}

public sealed class HealthCheckDto
{
    public bool Enabled { get; set; }

    public string Interval { get; set; } = "00:00:10";

    public string Timeout { get; set; } = "00:00:03";

    public string Policy { get; set; } = "ConsecutiveFailures";

    public string Path { get; set; } = string.Empty;

    public string Query { get; set; } = string.Empty;
}

public sealed class SettingsDto
{
    public int PublicHttpsPort { get; set; } = 443;

    public int AdminHttpsPort { get; set; } = 8444;
    public string RuntimeVersionDisplay { get; set; } = string.Empty;
    public string RuntimeVersionDetails { get; set; } = string.Empty;

    public string EnvironmentName { get; set; } = string.Empty;

    public string PublicHttpsEndpointDisplay { get; set; } = string.Empty;

    public string AdminHttpsEndpointDisplay { get; set; } = string.Empty;

    public string AdminAccessPolicySummary { get; set; } = string.Empty;

    public DateTimeOffset? LastAppliedUtc { get; set; }

    public DateTimeOffset? LastImportedUtc { get; set; }

    public CertificateMetadataDto? ActiveCertificate { get; set; }

    public CertificateMetadataDto? ImportedCertificateHint { get; set; }

    public bool UsingFallbackCertificate { get; set; }

    public bool CertificateRestartRequired { get; set; }

    public string RestartHint { get; set; } = string.Empty;

    public string CertificateStatus { get; set; } = string.Empty;

    public bool SelfUpdateEnabled { get; set; }

    public string SelfUpdateStatus { get; set; } = string.Empty;

    public string SelfUpdateButtonLabel { get; set; } = "Update Helgrind";

    public bool TelemetryEnabled { get; set; }

    public int TelemetryRetentionDays { get; set; }

    public string TelemetrySmokePath { get; set; } = string.Empty;

    public bool TelemetryAlertingEnabled { get; set; }
}

public sealed class CertificateMetadataDto
{
    public Guid Id { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string Thumbprint { get; set; } = string.Empty;

    public DateTimeOffset UploadedUtc { get; set; }
}

public sealed class HelgrindExportPackageDto
{
    public int SchemaVersion { get; set; } = 1;

    public DateTimeOffset ExportedUtc { get; set; } = DateTimeOffset.UtcNow;

    public HelgrindConfigurationDto Configuration { get; set; } = new();
}

public sealed class ApplyChangesResultDto
{
    public bool Success { get; set; }

    public bool ProxyConfigApplied { get; set; }

    public bool CertificateReloaded { get; set; }

    public bool RequiresRestart { get; set; }

    public string StatusMessage { get; set; } = string.Empty;

    public List<string> ValidationErrors { get; set; } = [];
}

public sealed class CertificateUploadResultDto
{
    public bool Success { get; set; }

    public bool UsingFallbackCertificate { get; set; }

    public string StatusMessage { get; set; } = string.Empty;

    public CertificateMetadataDto? ActiveCertificate { get; set; }
}

public sealed class AdminHealthStatusDto
{
    public string EnvironmentName { get; set; } = string.Empty;

    public DateTimeOffset GeneratedUtc { get; set; } = DateTimeOffset.UtcNow;

    public ListenerStatusDto PublicListener { get; set; } = new();

    public ListenerStatusDto AdminListener { get; set; } = new();

    public CertificateHealthDto Certificate { get; set; } = new();

    public ProxyHealthDto Proxy { get; set; } = new();
}

public sealed class ListenerStatusDto
{
    public string Name { get; set; } = string.Empty;

    public string Endpoint { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Exposure { get; set; } = string.Empty;
}

public sealed class CertificateHealthDto
{
    public string Status { get; set; } = string.Empty;

    public bool UsingFallbackCertificate { get; set; }

    public bool RestartRequired { get; set; }

    public string RestartHint { get; set; } = string.Empty;

    public CertificateMetadataDto? ActiveCertificate { get; set; }
}

public sealed class ProxyHealthDto
{
    public int RouteCount { get; set; }

    public int ClusterCount { get; set; }

    public int DestinationCount { get; set; }

    public DateTimeOffset? LastAppliedUtc { get; set; }

    public List<RouteHealthDto> LoadedRoutes { get; set; } = [];
}

public sealed class RouteHealthDto
{
    public string RouteId { get; set; } = string.Empty;

    public string ClusterId { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public List<string> Hosts { get; set; } = [];
}

public sealed class SelfUpdateResultDto
{
    public bool Success { get; set; }

    public bool Accepted { get; set; }

    public string StatusMessage { get; set; } = string.Empty;
}