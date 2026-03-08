using System.Text.Json;
using Helgrind.Contracts;
using Helgrind.Data;
using Helgrind.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Helgrind.Services;

public sealed class ConfigurationService(
    HelgrindDbContext dbContext,
    ProxyConfigFactory proxyConfigFactory,
    InMemoryProxyConfigProvider proxyConfigProvider,
    CertificateService certificateService,
    IOptions<HelgrindOptions> options,
    IWebHostEnvironment environment,
    AdminAccessService adminAccessService,
    SelfUpdateService selfUpdateService)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        await EnsureSettingsAsync(cancellationToken);
        await certificateService.InitializeAsync(cancellationToken);
        await ApplyAsync(cancellationToken);
    }

    public async Task<HelgrindConfigurationDto> GetConfigurationAsync(CancellationToken cancellationToken)
    {
        var routes = await dbContext.Routes
            .AsNoTracking()
            .OrderBy(route => route.RouteId)
            .ToListAsync(cancellationToken);

        var clusters = await dbContext.Clusters
            .AsNoTracking()
            .Include(cluster => cluster.Destinations)
            .OrderBy(cluster => cluster.ClusterId)
            .ToListAsync(cancellationToken);

        var settings = await EnsureSettingsAsync(cancellationToken);
        var activeCertificate = await certificateService.GetActiveCertificateMetadataAsync(cancellationToken);
        var certificateRestartRequired = certificateService.RequiresRestart(activeCertificate);

        return new HelgrindConfigurationDto
        {
            Routes = routes.Select(route => new RouteDto
            {
                RouteId = route.RouteId,
                ClusterId = route.ClusterId,
                Path = route.Path,
                Hosts = JsonSerializer.Deserialize<List<string>>(route.HostsJson, JsonOptions) ?? [],
                Order = route.Order,
            }).ToList(),
            Clusters = clusters.Select(cluster => new ClusterDto
            {
                ClusterId = cluster.ClusterId,
                LoadBalancingPolicy = cluster.LoadBalancingPolicy,
                ConsecutiveFailuresThreshold = cluster.ConsecutiveFailuresThreshold,
                HealthCheck = new HealthCheckDto
                {
                    Enabled = cluster.HealthCheckEnabled,
                    Interval = cluster.HealthCheckInterval ?? "00:00:10",
                    Timeout = cluster.HealthCheckTimeout ?? "00:00:03",
                    Policy = cluster.HealthCheckPolicy ?? "ConsecutiveFailures",
                    Path = cluster.HealthCheckPath ?? string.Empty,
                    Query = cluster.HealthCheckQuery ?? string.Empty,
                },
                Destinations = cluster.Destinations
                    .OrderBy(destination => destination.DestinationId)
                    .Select(destination => new DestinationDto
                    {
                        DestinationId = destination.DestinationId,
                        Address = destination.Address,
                    })
                    .ToList(),
            }).ToList(),
            Settings = new SettingsDto
            {
                PublicHttpsPort = options.Value.PublicHttpsPort,
                AdminHttpsPort = options.Value.AdminHttpsPort,
                EnvironmentName = environment.EnvironmentName,
                PublicHttpsEndpointDisplay = GetPublicHttpsEndpointDisplay(),
                AdminHttpsEndpointDisplay = GetAdminHttpsEndpointDisplay(),
                AdminAccessPolicySummary = adminAccessService.GetSummary(),
                LastAppliedUtc = settings.LastAppliedUtc,
                LastImportedUtc = settings.LastImportedUtc,
                ActiveCertificate = activeCertificate,
                ImportedCertificateHint = string.IsNullOrWhiteSpace(settings.ImportedCertificateDisplayName)
                    ? null
                    : new CertificateMetadataDto
                    {
                        DisplayName = settings.ImportedCertificateDisplayName,
                        Thumbprint = settings.ImportedCertificateThumbprint ?? string.Empty,
                    },
                UsingFallbackCertificate = certificateService.UsingFallbackCertificate,
                CertificateRestartRequired = certificateRestartRequired,
                RestartHint = GetRestartHint(certificateRestartRequired),
                CertificateStatus = certificateService.GetCertificateStatus(activeCertificate),
                SelfUpdateEnabled = selfUpdateService.IsConfigured,
                SelfUpdateStatus = selfUpdateService.GetStatusMessage(),
                SelfUpdateButtonLabel = selfUpdateService.ButtonLabel,
            }
        };
    }

    public async Task<AdminHealthStatusDto> GetAdminHealthStatusAsync(CancellationToken cancellationToken)
    {
        var configuration = await GetConfigurationAsync(cancellationToken);

        return new AdminHealthStatusDto
        {
            EnvironmentName = configuration.Settings.EnvironmentName,
            GeneratedUtc = DateTimeOffset.UtcNow,
            PublicListener = new ListenerStatusDto
            {
                Name = "Public Proxy Listener",
                Endpoint = configuration.Settings.PublicHttpsEndpointDisplay,
                Status = "Ready",
                Exposure = "Public edge listener for reverse proxy traffic",
            },
            AdminListener = new ListenerStatusDto
            {
                Name = "Admin Dashboard Listener",
                Endpoint = configuration.Settings.AdminHttpsEndpointDisplay,
                Status = "LAN-only",
                Exposure = configuration.Settings.AdminAccessPolicySummary,
            },
            Certificate = new CertificateHealthDto
            {
                Status = configuration.Settings.CertificateStatus,
                UsingFallbackCertificate = configuration.Settings.UsingFallbackCertificate,
                RestartRequired = configuration.Settings.CertificateRestartRequired,
                RestartHint = configuration.Settings.RestartHint,
                ActiveCertificate = configuration.Settings.ActiveCertificate,
            },
            Proxy = new ProxyHealthDto
            {
                RouteCount = configuration.Routes.Count,
                ClusterCount = configuration.Clusters.Count,
                DestinationCount = configuration.Clusters.Sum(cluster => cluster.Destinations.Count),
                LastAppliedUtc = configuration.Settings.LastAppliedUtc,
                LoadedRoutes = configuration.Routes
                    .OrderBy(route => route.RouteId, StringComparer.OrdinalIgnoreCase)
                    .Select(route => new RouteHealthDto
                    {
                        RouteId = route.RouteId,
                        ClusterId = route.ClusterId,
                        Path = route.Path,
                        Hosts = route.Hosts,
                    })
                    .ToList(),
            }
        };
    }

    public async Task SaveConfigurationAsync(HelgrindConfigurationDto configuration, CancellationToken cancellationToken)
    {
        var normalized = Normalize(configuration);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        dbContext.Routes.RemoveRange(await dbContext.Routes.ToListAsync(cancellationToken));
        dbContext.Destinations.RemoveRange(await dbContext.Destinations.ToListAsync(cancellationToken));
        dbContext.Clusters.RemoveRange(await dbContext.Clusters.ToListAsync(cancellationToken));

        foreach (var cluster in normalized.Clusters)
        {
            dbContext.Clusters.Add(new ProxyClusterEntity
            {
                ClusterId = cluster.ClusterId,
                LoadBalancingPolicy = cluster.LoadBalancingPolicy,
                HealthCheckEnabled = cluster.HealthCheck.Enabled,
                HealthCheckInterval = cluster.HealthCheck.Interval,
                HealthCheckTimeout = cluster.HealthCheck.Timeout,
                HealthCheckPolicy = cluster.HealthCheck.Policy,
                HealthCheckPath = cluster.HealthCheck.Path,
                HealthCheckQuery = cluster.HealthCheck.Query,
                ConsecutiveFailuresThreshold = cluster.ConsecutiveFailuresThreshold,
                Destinations = cluster.Destinations.Select(destination => new ProxyDestinationEntity
                {
                    DestinationId = destination.DestinationId,
                    ClusterId = cluster.ClusterId,
                    Address = destination.Address,
                }).ToList(),
            });
        }

        foreach (var route in normalized.Routes)
        {
            dbContext.Routes.Add(new ProxyRouteEntity
            {
                RouteId = route.RouteId,
                ClusterId = route.ClusterId,
                Path = route.Path,
                HostsJson = JsonSerializer.Serialize(route.Hosts, JsonOptions),
                Order = route.Order,
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ApplyChangesResultDto> ApplyAsync(CancellationToken cancellationToken)
    {
        var configuration = await GetConfigurationAsync(cancellationToken);
        var buildResult = proxyConfigFactory.Build(configuration);

        if (buildResult.Errors.Count > 0)
        {
            return new ApplyChangesResultDto
            {
                Success = false,
                ProxyConfigApplied = false,
                CertificateReloaded = !certificateService.UsingFallbackCertificate,
                RequiresRestart = false,
                StatusMessage = "Configuration could not be applied because validation failed.",
                ValidationErrors = buildResult.Errors.ToList(),
            };
        }

        proxyConfigProvider.Update(buildResult.Routes, buildResult.Clusters);

        var settings = await EnsureSettingsAsync(cancellationToken);
        settings.LastAppliedUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ApplyChangesResultDto
        {
            Success = true,
            ProxyConfigApplied = true,
            CertificateReloaded = !certificateService.UsingFallbackCertificate,
            RequiresRestart = (await certificateService.GetActiveCertificateMetadataAsync(cancellationToken)) is not null,
            StatusMessage = certificateService.UsingFallbackCertificate
                ? "Proxy settings applied. Helgrind is still serving the built-in temporary certificate until you upload a PEM and key."
                : "Proxy settings applied. If you recently replaced the certificate files, restart Helgrind to switch the HTTPS certificate.",
        };
    }

    public async Task<HelgrindExportPackageDto> ExportAsync(CancellationToken cancellationToken) => new()
    {
        ExportedUtc = DateTimeOffset.UtcNow,
        Configuration = await GetConfigurationAsync(cancellationToken)
    };

    public async Task<ApplyChangesResultDto> ImportAsync(HelgrindExportPackageDto package, CancellationToken cancellationToken)
    {
        await SaveConfigurationAsync(package.Configuration, cancellationToken);

        var settings = await EnsureSettingsAsync(cancellationToken);
        settings.LastImportedUtc = DateTimeOffset.UtcNow;

        if (package.Configuration.Settings.ActiveCertificate is { } certificateHint)
        {
            settings.ImportedCertificateDisplayName = certificateHint.DisplayName;
            settings.ImportedCertificateThumbprint = certificateHint.Thumbprint;
        }
        else
        {
            settings.ImportedCertificateDisplayName = null;
            settings.ImportedCertificateThumbprint = null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var applyResult = await ApplyAsync(cancellationToken);
        if (package.Configuration.Settings.ActiveCertificate is not null)
        {
            applyResult.StatusMessage += " The imported package included certificate metadata only, so upload the PEM and key separately if you need to switch certificates.";
        }

        return applyResult;
    }

    public static HelgrindConfigurationDto Normalize(HelgrindConfigurationDto configuration)
    {
        var normalized = new HelgrindConfigurationDto
        {
            Routes = configuration.Routes
                .Select(route => new RouteDto
                {
                    RouteId = route.RouteId.Trim(),
                    ClusterId = route.ClusterId.Trim(),
                    Path = string.IsNullOrWhiteSpace(route.Path) ? "{**catch-all}" : route.Path.Trim(),
                    Hosts = route.Hosts
                        .Select(host => host.Trim())
                        .Where(host => !string.IsNullOrWhiteSpace(host))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    Order = route.Order,
                })
                .OrderBy(route => route.RouteId, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Clusters = configuration.Clusters
                .Select(cluster => new ClusterDto
                {
                    ClusterId = cluster.ClusterId.Trim(),
                    LoadBalancingPolicy = string.IsNullOrWhiteSpace(cluster.LoadBalancingPolicy) ? null : cluster.LoadBalancingPolicy.Trim(),
                    ConsecutiveFailuresThreshold = cluster.ConsecutiveFailuresThreshold,
                    HealthCheck = new HealthCheckDto
                    {
                        Enabled = cluster.HealthCheck.Enabled,
                        Interval = string.IsNullOrWhiteSpace(cluster.HealthCheck.Interval) ? "00:00:10" : cluster.HealthCheck.Interval.Trim(),
                        Timeout = string.IsNullOrWhiteSpace(cluster.HealthCheck.Timeout) ? "00:00:03" : cluster.HealthCheck.Timeout.Trim(),
                        Policy = string.IsNullOrWhiteSpace(cluster.HealthCheck.Policy) ? "ConsecutiveFailures" : cluster.HealthCheck.Policy.Trim(),
                        Path = cluster.HealthCheck.Path.Trim(),
                        Query = cluster.HealthCheck.Query.Trim(),
                    },
                    Destinations = cluster.Destinations
                        .Select(destination => new DestinationDto
                        {
                            DestinationId = destination.DestinationId.Trim(),
                            Address = destination.Address.Trim(),
                        })
                        .Where(destination => !string.IsNullOrWhiteSpace(destination.DestinationId) || !string.IsNullOrWhiteSpace(destination.Address))
                        .OrderBy(destination => destination.DestinationId, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                })
                .OrderBy(cluster => cluster.ClusterId, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Settings = configuration.Settings
        };

        return normalized;
    }

    private async Task<AppSettingsEntity> EnsureSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await dbContext.AppSettings.SingleOrDefaultAsync(entry => entry.Id == 1, cancellationToken);
        if (settings is not null)
        {
            return settings;
        }

        settings = new AppSettingsEntity { Id = 1 };
        dbContext.AppSettings.Add(settings);
        await dbContext.SaveChangesAsync(cancellationToken);
        return settings;
    }

    private string GetPublicHttpsEndpointDisplay()
    {
        return environment.IsDevelopment()
            ? $"https://localhost:{options.Value.PublicHttpsPort}"
            : options.Value.PublicHttpsPort == 443
                ? "https://*:443"
                : $"https://*:{options.Value.PublicHttpsPort}";
    }

    private string GetAdminHttpsEndpointDisplay()
    {
        return environment.IsDevelopment()
            ? $"https://localhost:{options.Value.AdminHttpsPort}"
            : $"https://<lan-ip>:{options.Value.AdminHttpsPort}";
    }

    private static string GetRestartHint(bool certificateRestartRequired)
    {
        return certificateRestartRequired
            ? "Run the restart helper for your host after replacing the certificate so Kestrel picks up the stored PEM and key."
            : "No restart is currently required for the stored certificate.";
    }
}