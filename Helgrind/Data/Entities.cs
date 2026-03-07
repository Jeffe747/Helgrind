using System.ComponentModel.DataAnnotations;

namespace Helgrind.Data;

public sealed class ProxyRouteEntity
{
    [Key]
    public string RouteId { get; set; } = string.Empty;

    public string ClusterId { get; set; } = string.Empty;

    public string Path { get; set; } = "{**catch-all}";

    public string HostsJson { get; set; } = "[]";

    public int Order { get; set; }
}

public sealed class ProxyClusterEntity
{
    [Key]
    public string ClusterId { get; set; } = string.Empty;

    public string? LoadBalancingPolicy { get; set; }

    public bool HealthCheckEnabled { get; set; }

    public string? HealthCheckInterval { get; set; }

    public string? HealthCheckTimeout { get; set; }

    public string? HealthCheckPolicy { get; set; }

    public string? HealthCheckPath { get; set; }

    public string? HealthCheckQuery { get; set; }

    public int? ConsecutiveFailuresThreshold { get; set; }

    public List<ProxyDestinationEntity> Destinations { get; set; } = [];
}

public sealed class ProxyDestinationEntity
{
    [Key]
    public string DestinationId { get; set; } = string.Empty;

    public string ClusterId { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public ProxyClusterEntity? Cluster { get; set; }
}

public sealed class AppSettingsEntity
{
    [Key]
    public int Id { get; set; } = 1;

    public DateTimeOffset? LastAppliedUtc { get; set; }

    public DateTimeOffset? LastImportedUtc { get; set; }

    public string? ImportedCertificateDisplayName { get; set; }

    public string? ImportedCertificateThumbprint { get; set; }
}

public sealed class StoredCertificateEntity
{
    [Key]
    public Guid Id { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string Thumbprint { get; set; } = string.Empty;

    public string PemFilePath { get; set; } = string.Empty;

    public string KeyFilePath { get; set; } = string.Empty;

    public string OriginalPemFileName { get; set; } = string.Empty;

    public string OriginalKeyFileName { get; set; } = string.Empty;

    public DateTimeOffset UploadedUtc { get; set; }

    public bool IsActive { get; set; }
}