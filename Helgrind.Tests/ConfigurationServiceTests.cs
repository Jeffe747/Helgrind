using Helgrind.Contracts;
using Helgrind.Data;
using Helgrind.Options;
using Helgrind.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Helgrind.Tests;

public sealed class ConfigurationServiceTests : IDisposable
{
    private readonly string _contentRootPath = Path.Combine(Path.GetTempPath(), $"helgrind-tests-{Guid.NewGuid():N}");
    private readonly SqliteConnection _connection;

    public ConfigurationServiceTests()
    {
        Directory.CreateDirectory(_contentRootPath);
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    [Fact]
    public async Task ImportAsync_StoresImportedCertificateHint_AndAppliesConfiguration()
    {
        await using var dbContext = CreateDbContext();
        var hostEnvironment = new TestWebHostEnvironment(_contentRootPath);
        var options = Microsoft.Extensions.Options.Options.Create(new HelgrindOptions
        {
            HttpsPort = 443,
            DatabasePath = "App_Data/helgrind.db",
            CertificateStoragePath = "App_Data/certificates"
        });
        var runtimeState = new CertificateRuntimeState();
        var certificateService = new CertificateService(dbContext, runtimeState, hostEnvironment, options);
        var selfUpdateService = new SelfUpdateService(options, hostEnvironment, NullLogger<SelfUpdateService>.Instance);
        var configurationService = new ConfigurationService(
            dbContext,
            new ProxyConfigFactory(),
            new InMemoryProxyConfigProvider(),
            certificateService,
            options,
            hostEnvironment,
            new AdminAccessService(options),
            selfUpdateService);

        await configurationService.InitializeAsync(CancellationToken.None);

        var result = await configurationService.ImportAsync(
            new HelgrindExportPackageDto
            {
                Configuration = new HelgrindConfigurationDto
                {
                    Routes =
                    [
                        new RouteDto
                        {
                            RouteId = "route1",
                            ClusterId = "cluster1",
                            Hosts = ["api.example.com"],
                            Path = "{**catch-all}"
                        }
                    ],
                    Clusters =
                    [
                        new ClusterDto
                        {
                            ClusterId = "cluster1",
                            Destinations =
                            [
                                new DestinationDto
                                {
                                    DestinationId = "destination1",
                                    Address = "https://backend.internal:5001"
                                }
                            ]
                        }
                    ],
                    Settings = new SettingsDto
                    {
                        ActiveCertificate = new CertificateMetadataDto
                        {
                            DisplayName = "wildcard.example.com",
                            Thumbprint = "ABC123"
                        }
                    }
                }
            },
            CancellationToken.None);

        var configuration = await configurationService.GetConfigurationAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Single(configuration.Routes);
        Assert.Single(configuration.Clusters);
        Assert.NotNull(configuration.Settings.ImportedCertificateHint);
        Assert.Equal("wildcard.example.com", configuration.Settings.ImportedCertificateHint!.DisplayName);
        Assert.Equal("ABC123", configuration.Settings.ImportedCertificateHint.Thumbprint);
        Assert.NotNull(configuration.Settings.LastImportedUtc);
        Assert.Equal("Development", configuration.Settings.EnvironmentName);
        Assert.Equal("https://localhost:443", configuration.Settings.PublicHttpsEndpointDisplay);
        Assert.Equal("https://localhost:8444", configuration.Settings.AdminHttpsEndpointDisplay);
        Assert.Contains("192.168.0.0/16", configuration.Settings.AdminAccessPolicySummary);
        Assert.False(configuration.Settings.CertificateRestartRequired);
    }

    [Fact]
    public void Normalize_DefaultsNullRouteOrderToZero()
    {
        var normalized = ConfigurationService.Normalize(new HelgrindConfigurationDto
        {
            Routes =
            [
                new RouteDto
                {
                    RouteId = "route1",
                    ClusterId = "cluster1",
                    Hosts = ["api.example.com"],
                    Order = null,
                }
            ]
        });

        Assert.Single(normalized.Routes);
        Assert.Equal(0, normalized.Routes[0].Order);
    }

    [Fact]
    public async Task SaveConfigurationAsync_RoundTripsAllowedClientNetworks()
    {
        await using var dbContext = CreateDbContext();
        var hostEnvironment = new TestWebHostEnvironment(_contentRootPath);
        var options = Microsoft.Extensions.Options.Options.Create(new HelgrindOptions
        {
            PublicHttpsPort = 443,
            AdminHttpsPort = 8444,
            DatabasePath = "App_Data/helgrind.db",
            CertificateStoragePath = "App_Data/certificates"
        });
        var runtimeState = new CertificateRuntimeState();
        var certificateService = new CertificateService(dbContext, runtimeState, hostEnvironment, options);
        var selfUpdateService = new SelfUpdateService(options, hostEnvironment, NullLogger<SelfUpdateService>.Instance);
        var configurationService = new ConfigurationService(
            dbContext,
            new ProxyConfigFactory(),
            new InMemoryProxyConfigProvider(),
            certificateService,
            options,
            hostEnvironment,
            new AdminAccessService(options),
            selfUpdateService);

        await configurationService.InitializeAsync(CancellationToken.None);
        await configurationService.SaveConfigurationAsync(
            new HelgrindConfigurationDto
            {
                Routes =
                [
                    new RouteDto
                    {
                        RouteId = "route1",
                        ClusterId = "cluster1",
                        Path = "{**catch-all}",
                        Hosts = ["assistant.icicle.dk"],
                        AllowedClientNetworks = [" 85.184.162.188 ", "185.50.193.0/24", "85.184.162.188"]
                    }
                ],
                Clusters =
                [
                    new ClusterDto
                    {
                        ClusterId = "cluster1",
                        Destinations =
                        [
                            new DestinationDto
                            {
                                DestinationId = "destination1",
                                Address = "https://backend.internal:5001"
                            }
                        ]
                    }
                ]
            },
            CancellationToken.None);

        var configuration = await configurationService.GetConfigurationAsync(CancellationToken.None);

        var route = Assert.Single(configuration.Routes);
        Assert.Equal(["85.184.162.188", "185.50.193.0/24"], route.AllowedClientNetworks);
    }

    [Fact]
    public async Task GetAdminHealthStatusAsync_ReturnsListenerAndRouteSummary()
    {
        await using var dbContext = CreateDbContext();
        var hostEnvironment = new TestWebHostEnvironment(_contentRootPath);
        var options = Microsoft.Extensions.Options.Options.Create(new HelgrindOptions
        {
            PublicHttpsPort = 443,
            AdminHttpsPort = 8444,
            DatabasePath = "App_Data/helgrind.db",
            CertificateStoragePath = "App_Data/certificates"
        });
        var runtimeState = new CertificateRuntimeState();
        var certificateService = new CertificateService(dbContext, runtimeState, hostEnvironment, options);
        var selfUpdateService = new SelfUpdateService(options, hostEnvironment, NullLogger<SelfUpdateService>.Instance);
        var configurationService = new ConfigurationService(
            dbContext,
            new ProxyConfigFactory(),
            new InMemoryProxyConfigProvider(),
            certificateService,
            options,
            hostEnvironment,
            new AdminAccessService(options),
            selfUpdateService);

        await configurationService.InitializeAsync(CancellationToken.None);
        await configurationService.SaveConfigurationAsync(
            new HelgrindConfigurationDto
            {
                Routes =
                [
                    new RouteDto
                    {
                        RouteId = "route1",
                        ClusterId = "cluster1",
                        Path = "{**catch-all}",
                        Hosts = ["api.example.com"]
                    }
                ],
                Clusters =
                [
                    new ClusterDto
                    {
                        ClusterId = "cluster1",
                        Destinations =
                        [
                            new DestinationDto
                            {
                                DestinationId = "destination1",
                                Address = "https://backend.internal:5001"
                            }
                        ]
                    }
                ]
            },
            CancellationToken.None);
        await configurationService.ApplyAsync(CancellationToken.None);

        var status = await configurationService.GetAdminHealthStatusAsync(CancellationToken.None);

        Assert.Equal("Development", status.EnvironmentName);
        Assert.Equal("https://localhost:443", status.PublicListener.Endpoint);
        Assert.Equal("https://localhost:8444", status.AdminListener.Endpoint);
        Assert.Equal(1, status.Proxy.RouteCount);
        Assert.Equal(1, status.Proxy.ClusterCount);
        Assert.Equal(1, status.Proxy.DestinationCount);
        Assert.Single(status.Proxy.LoadedRoutes);
        Assert.Equal("route1", status.Proxy.LoadedRoutes[0].RouteId);
    }

    [Fact]
    public async Task InitializeAsync_RecreatesTelemetryTable_WhenDatabasePredatesTelemetrySchema()
    {
        await using var seedContext = CreateDbContext();
        await seedContext.Database.EnsureCreatedAsync();
        await seedContext.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS \"SuspiciousRequestEvents\"");

        await using var dbContext = CreateDbContext();
        var hostEnvironment = new TestWebHostEnvironment(_contentRootPath);
        var options = Microsoft.Extensions.Options.Options.Create(new HelgrindOptions
        {
            PublicHttpsPort = 443,
            AdminHttpsPort = 8444,
            DatabasePath = "App_Data/helgrind.db",
            CertificateStoragePath = "App_Data/certificates"
        });
        var runtimeState = new CertificateRuntimeState();
        var certificateService = new CertificateService(dbContext, runtimeState, hostEnvironment, options);
        var selfUpdateService = new SelfUpdateService(options, hostEnvironment, NullLogger<SelfUpdateService>.Instance);
        var configurationService = new ConfigurationService(
            dbContext,
            new ProxyConfigFactory(),
            new InMemoryProxyConfigProvider(),
            certificateService,
            options,
            hostEnvironment,
            new AdminAccessService(options),
            selfUpdateService);

        await configurationService.InitializeAsync(CancellationToken.None);

        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'SuspiciousRequestEvents'";
        var tableCount = Convert.ToInt32(await command.ExecuteScalarAsync());

        Assert.Equal(1, tableCount);
    }

    [Fact]
    public async Task ApplyAsync_DoesNotWarnAboutCertificateRestart_WhenStoredCertificateIsAlreadyActive()
    {
        await using var dbContext = CreateDbContext();
        var hostEnvironment = new TestWebHostEnvironment(_contentRootPath);
        var options = Microsoft.Extensions.Options.Options.Create(new HelgrindOptions
        {
            PublicHttpsPort = 443,
            AdminHttpsPort = 8444,
            DatabasePath = "App_Data/helgrind.db",
            CertificateStoragePath = "App_Data/certificates"
        });
        var runtimeState = new CertificateRuntimeState();
        var certificateService = new CertificateService(dbContext, runtimeState, hostEnvironment, options);
        var selfUpdateService = new SelfUpdateService(options, hostEnvironment, NullLogger<SelfUpdateService>.Instance);
        var configurationService = new ConfigurationService(
            dbContext,
            new ProxyConfigFactory(),
            new InMemoryProxyConfigProvider(),
            certificateService,
            options,
            hostEnvironment,
            new AdminAccessService(options),
            selfUpdateService);

        await configurationService.InitializeAsync(CancellationToken.None);

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=helgrind-tests", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        runtimeState.SetActiveCertificate(certificate);

        dbContext.Certificates.Add(new StoredCertificateEntity
        {
            Id = Guid.NewGuid(),
            DisplayName = "test-cert",
            Thumbprint = certificate.Thumbprint,
            PemFilePath = Path.Combine(_contentRootPath, "certificate.pem"),
            KeyFilePath = Path.Combine(_contentRootPath, "certificate.key"),
            OriginalPemFileName = "certificate.pem",
            OriginalKeyFileName = "certificate.key",
            UploadedUtc = DateTimeOffset.UtcNow,
            IsActive = true,
        });
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await configurationService.SaveConfigurationAsync(
            new HelgrindConfigurationDto
            {
                Routes =
                [
                    new RouteDto
                    {
                        RouteId = "route1",
                        ClusterId = "cluster1",
                        Path = "{**catch-all}",
                        Hosts = ["api.example.com"]
                    }
                ],
                Clusters =
                [
                    new ClusterDto
                    {
                        ClusterId = "cluster1",
                        Destinations =
                        [
                            new DestinationDto
                            {
                                DestinationId = "destination1",
                                Address = "https://backend.internal:5001"
                            }
                        ]
                    }
                ]
            },
            CancellationToken.None);

        var result = await configurationService.ApplyAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(result.RequiresRestart);
        Assert.Equal("Proxy settings applied.", result.StatusMessage);
    }

    [Fact]
    public async Task ApplyAsync_ReturnsNeutralStatusMessage_WhenCertificateIsStoredButRestartIsStillRequired()
    {
        await using var dbContext = CreateDbContext();
        var hostEnvironment = new TestWebHostEnvironment(_contentRootPath);
        var options = Microsoft.Extensions.Options.Options.Create(new HelgrindOptions
        {
            PublicHttpsPort = 443,
            AdminHttpsPort = 8444,
            DatabasePath = "App_Data/helgrind.db",
            CertificateStoragePath = "App_Data/certificates"
        });
        var runtimeState = new CertificateRuntimeState();
        var certificateService = new CertificateService(dbContext, runtimeState, hostEnvironment, options);
        var selfUpdateService = new SelfUpdateService(options, hostEnvironment, NullLogger<SelfUpdateService>.Instance);
        var configurationService = new ConfigurationService(
            dbContext,
            new ProxyConfigFactory(),
            new InMemoryProxyConfigProvider(),
            certificateService,
            options,
            hostEnvironment,
            new AdminAccessService(options),
            selfUpdateService);

        await configurationService.InitializeAsync(CancellationToken.None);

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=helgrind-tests-pending", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

        dbContext.Certificates.Add(new StoredCertificateEntity
        {
            Id = Guid.NewGuid(),
            DisplayName = "test-cert-pending",
            Thumbprint = certificate.Thumbprint,
            PemFilePath = Path.Combine(_contentRootPath, "certificate-pending.pem"),
            KeyFilePath = Path.Combine(_contentRootPath, "certificate-pending.key"),
            OriginalPemFileName = "certificate-pending.pem",
            OriginalKeyFileName = "certificate-pending.key",
            UploadedUtc = DateTimeOffset.UtcNow,
            IsActive = true,
        });
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await configurationService.SaveConfigurationAsync(
            new HelgrindConfigurationDto
            {
                Routes =
                [
                    new RouteDto
                    {
                        RouteId = "route1",
                        ClusterId = "cluster1",
                        Path = "{**catch-all}",
                        Hosts = ["api.example.com"]
                    }
                ],
                Clusters =
                [
                    new ClusterDto
                    {
                        ClusterId = "cluster1",
                        Destinations =
                        [
                            new DestinationDto
                            {
                                DestinationId = "destination1",
                                Address = "https://backend.internal:5001"
                            }
                        ]
                    }
                ]
            },
            CancellationToken.None);

        var result = await configurationService.ApplyAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.RequiresRestart);
        Assert.Equal("Proxy settings applied.", result.StatusMessage);
    }

    [Fact]
    public async Task ApplyAsync_ReturnsNeutralStatusMessage_WhenNoCertificateIsConfigured()
    {
        await using var dbContext = CreateDbContext();
        var hostEnvironment = new TestWebHostEnvironment(_contentRootPath);
        var options = Microsoft.Extensions.Options.Options.Create(new HelgrindOptions
        {
            PublicHttpsPort = 443,
            AdminHttpsPort = 8444,
            DatabasePath = "App_Data/helgrind.db",
            CertificateStoragePath = "App_Data/certificates"
        });
        var runtimeState = new CertificateRuntimeState();
        var certificateService = new CertificateService(dbContext, runtimeState, hostEnvironment, options);
        var selfUpdateService = new SelfUpdateService(options, hostEnvironment, NullLogger<SelfUpdateService>.Instance);
        var configurationService = new ConfigurationService(
            dbContext,
            new ProxyConfigFactory(),
            new InMemoryProxyConfigProvider(),
            certificateService,
            options,
            hostEnvironment,
            new AdminAccessService(options),
            selfUpdateService);

        await configurationService.InitializeAsync(CancellationToken.None);
        await configurationService.SaveConfigurationAsync(
            new HelgrindConfigurationDto
            {
                Routes =
                [
                    new RouteDto
                    {
                        RouteId = "route1",
                        ClusterId = "cluster1",
                        Path = "{**catch-all}",
                        Hosts = ["api.example.com"]
                    }
                ],
                Clusters =
                [
                    new ClusterDto
                    {
                        ClusterId = "cluster1",
                        Destinations =
                        [
                            new DestinationDto
                            {
                                DestinationId = "destination1",
                                Address = "https://backend.internal:5001"
                            }
                        ]
                    }
                ]
            },
            CancellationToken.None);

        var result = await configurationService.ApplyAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(result.CertificateReloaded);
        Assert.False(result.RequiresRestart);
        Assert.Equal("Proxy settings applied.", result.StatusMessage);
    }

    [Fact]
    public void Normalize_TrimsAndDeduplicatesRouteHosts()
    {
        var normalized = ConfigurationService.Normalize(new HelgrindConfigurationDto
        {
            Routes =
            [
                new RouteDto
                {
                    RouteId = " route1 ",
                    ClusterId = " cluster1 ",
                    Path = " ",
                    Hosts = [" api.example.com ", "api.example.com", " www.example.com "]
                }
            ],
            Clusters =
            [
                new ClusterDto
                {
                    ClusterId = " cluster1 ",
                    Destinations =
                    [
                        new DestinationDto
                        {
                            DestinationId = " destination1 ",
                            Address = " https://backend.internal:5001 "
                        }
                    ]
                }
            ]
        });

        Assert.Equal("route1", normalized.Routes[0].RouteId);
        Assert.Equal("cluster1", normalized.Routes[0].ClusterId);
        Assert.Equal("{**catch-all}", normalized.Routes[0].Path);
        Assert.Equal(2, normalized.Routes[0].Hosts.Count);
        Assert.Equal("destination1", normalized.Clusters[0].Destinations[0].DestinationId);
        Assert.Equal("https://backend.internal:5001", normalized.Clusters[0].Destinations[0].Address);
    }

    [Fact]
    public async Task SaveConfigurationAsync_AddsRouteAndCluster()
    {
        await using var dbContext = CreateDbContext();
        var hostEnvironment = new TestWebHostEnvironment(_contentRootPath);
        var options = Microsoft.Extensions.Options.Options.Create(new HelgrindOptions
        {
            PublicHttpsPort = 443,
            AdminHttpsPort = 8444,
            DatabasePath = "App_Data/helgrind.db",
            CertificateStoragePath = "App_Data/certificates"
        });
        var runtimeState = new CertificateRuntimeState();
        var certificateService = new CertificateService(dbContext, runtimeState, hostEnvironment, options);
        var selfUpdateService = new SelfUpdateService(options, hostEnvironment, NullLogger<SelfUpdateService>.Instance);
        var configurationService = new ConfigurationService(
            dbContext,
            new ProxyConfigFactory(),
            new InMemoryProxyConfigProvider(),
            certificateService,
            options,
            hostEnvironment,
            new AdminAccessService(options),
            selfUpdateService);

        await configurationService.InitializeAsync(CancellationToken.None);

        await configurationService.SaveConfigurationAsync(
            new HelgrindConfigurationDto
            {
                Routes =
                [
                    new RouteDto
                    {
                        RouteId = "route-create",
                        ClusterId = "cluster-create",
                        Path = "/api/{**catch-all}",
                        Hosts = ["api.example.com"],
                        Order = 5,
                    }
                ],
                Clusters =
                [
                    new ClusterDto
                    {
                        ClusterId = "cluster-create",
                        LoadBalancingPolicy = "RoundRobin",
                        HealthCheck = new HealthCheckDto
                        {
                            Enabled = true,
                            Interval = "00:00:15",
                            Timeout = "00:00:05",
                            Policy = "ConsecutiveFailures",
                            Path = "/healthz",
                            Query = "?full=true",
                        },
                        ConsecutiveFailuresThreshold = 3,
                        Destinations =
                        [
                            new DestinationDto
                            {
                                DestinationId = "destination-create",
                                Address = "https://backend-create.internal:5001"
                            }
                        ]
                    }
                ]
            },
            CancellationToken.None);

        var configuration = await configurationService.GetConfigurationAsync(CancellationToken.None);

        var route = Assert.Single(configuration.Routes);
        Assert.Equal("route-create", route.RouteId);
        Assert.Equal("cluster-create", route.ClusterId);
        Assert.Equal("/api/{**catch-all}", route.Path);
        Assert.Equal(["api.example.com"], route.Hosts);
        Assert.Equal(5, route.Order);

        var cluster = Assert.Single(configuration.Clusters);
        Assert.Equal("cluster-create", cluster.ClusterId);
        Assert.Equal("RoundRobin", cluster.LoadBalancingPolicy);
        Assert.True(cluster.HealthCheck.Enabled);
        Assert.Equal("00:00:15", cluster.HealthCheck.Interval);
        Assert.Equal("00:00:05", cluster.HealthCheck.Timeout);
        Assert.Equal("/healthz", cluster.HealthCheck.Path);
        Assert.Equal("?full=true", cluster.HealthCheck.Query);
        Assert.Equal(3, cluster.ConsecutiveFailuresThreshold);
        var destination = Assert.Single(cluster.Destinations);
        Assert.Equal("destination-create", destination.DestinationId);
        Assert.Equal("https://backend-create.internal:5001", destination.Address);
    }

    [Fact]
    public async Task SaveConfigurationAsync_UpdatesExistingRouteAndCluster()
    {
        await using var dbContext = CreateDbContext();
        var hostEnvironment = new TestWebHostEnvironment(_contentRootPath);
        var options = Microsoft.Extensions.Options.Options.Create(new HelgrindOptions
        {
            PublicHttpsPort = 443,
            AdminHttpsPort = 8444,
            DatabasePath = "App_Data/helgrind.db",
            CertificateStoragePath = "App_Data/certificates"
        });
        var runtimeState = new CertificateRuntimeState();
        var certificateService = new CertificateService(dbContext, runtimeState, hostEnvironment, options);
        var selfUpdateService = new SelfUpdateService(options, hostEnvironment, NullLogger<SelfUpdateService>.Instance);
        var configurationService = new ConfigurationService(
            dbContext,
            new ProxyConfigFactory(),
            new InMemoryProxyConfigProvider(),
            certificateService,
            options,
            hostEnvironment,
            new AdminAccessService(options),
            selfUpdateService);

        await configurationService.InitializeAsync(CancellationToken.None);
        await configurationService.SaveConfigurationAsync(
            new HelgrindConfigurationDto
            {
                Routes =
                [
                    new RouteDto
                    {
                        RouteId = "route-update",
                        ClusterId = "cluster-update",
                        Path = "/api/{**catch-all}",
                        Hosts = ["api.example.com"],
                        Order = 1,
                    }
                ],
                Clusters =
                [
                    new ClusterDto
                    {
                        ClusterId = "cluster-update",
                        Destinations =
                        [
                            new DestinationDto
                            {
                                DestinationId = "destination-update",
                                Address = "https://backend-old.internal:5001"
                            }
                        ]
                    }
                ]
            },
            CancellationToken.None);

        await configurationService.SaveConfigurationAsync(
            new HelgrindConfigurationDto
            {
                Routes =
                [
                    new RouteDto
                    {
                        RouteId = "route-update",
                        ClusterId = "cluster-update",
                        Path = "/v2/{**catch-all}",
                        Hosts = ["api.example.com", "admin.example.com"],
                        Order = 9,
                    }
                ],
                Clusters =
                [
                    new ClusterDto
                    {
                        ClusterId = "cluster-update",
                        LoadBalancingPolicy = "LeastRequests",
                        HealthCheck = new HealthCheckDto
                        {
                            Enabled = true,
                            Interval = "00:00:20",
                            Timeout = "00:00:04",
                            Policy = "ConsecutiveFailures",
                            Path = "/ready",
                            Query = "?probe=1",
                        },
                        ConsecutiveFailuresThreshold = 2,
                        Destinations =
                        [
                            new DestinationDto
                            {
                                DestinationId = "destination-update",
                                Address = "https://backend-new.internal:5443"
                            },
                            new DestinationDto
                            {
                                DestinationId = "destination-update-2",
                                Address = "https://backend-secondary.internal:5444"
                            }
                        ]
                    }
                ]
            },
            CancellationToken.None);

        var configuration = await configurationService.GetConfigurationAsync(CancellationToken.None);

        var route = Assert.Single(configuration.Routes);
        Assert.Equal("/v2/{**catch-all}", route.Path);
        Assert.Equal(["api.example.com", "admin.example.com"], route.Hosts);
        Assert.Equal(9, route.Order);

        var cluster = Assert.Single(configuration.Clusters);
        Assert.Equal("LeastRequests", cluster.LoadBalancingPolicy);
        Assert.True(cluster.HealthCheck.Enabled);
        Assert.Equal("00:00:20", cluster.HealthCheck.Interval);
        Assert.Equal("00:00:04", cluster.HealthCheck.Timeout);
        Assert.Equal("/ready", cluster.HealthCheck.Path);
        Assert.Equal("?probe=1", cluster.HealthCheck.Query);
        Assert.Equal(2, cluster.ConsecutiveFailuresThreshold);
        Assert.Equal(2, cluster.Destinations.Count);
        Assert.Contains(cluster.Destinations, destination =>
            destination.DestinationId == "destination-update"
            && destination.Address == "https://backend-new.internal:5443");
        Assert.Contains(cluster.Destinations, destination =>
            destination.DestinationId == "destination-update-2"
            && destination.Address == "https://backend-secondary.internal:5444");
    }

    [Fact]
    public async Task SaveConfigurationAsync_DeletesRemovedRoutesAndClusters()
    {
        await using var dbContext = CreateDbContext();
        var hostEnvironment = new TestWebHostEnvironment(_contentRootPath);
        var options = Microsoft.Extensions.Options.Options.Create(new HelgrindOptions
        {
            PublicHttpsPort = 443,
            AdminHttpsPort = 8444,
            DatabasePath = "App_Data/helgrind.db",
            CertificateStoragePath = "App_Data/certificates"
        });
        var runtimeState = new CertificateRuntimeState();
        var certificateService = new CertificateService(dbContext, runtimeState, hostEnvironment, options);
        var selfUpdateService = new SelfUpdateService(options, hostEnvironment, NullLogger<SelfUpdateService>.Instance);
        var configurationService = new ConfigurationService(
            dbContext,
            new ProxyConfigFactory(),
            new InMemoryProxyConfigProvider(),
            certificateService,
            options,
            hostEnvironment,
            new AdminAccessService(options),
            selfUpdateService);

        await configurationService.InitializeAsync(CancellationToken.None);
        await configurationService.SaveConfigurationAsync(
            new HelgrindConfigurationDto
            {
                Routes =
                [
                    new RouteDto
                    {
                        RouteId = "route-keep",
                        ClusterId = "cluster-keep",
                        Path = "/keep/{**catch-all}",
                        Hosts = ["keep.example.com"],
                    },
                    new RouteDto
                    {
                        RouteId = "route-delete",
                        ClusterId = "cluster-delete",
                        Path = "/delete/{**catch-all}",
                        Hosts = ["delete.example.com"],
                    }
                ],
                Clusters =
                [
                    new ClusterDto
                    {
                        ClusterId = "cluster-keep",
                        Destinations =
                        [
                            new DestinationDto
                            {
                                DestinationId = "destination-keep",
                                Address = "https://backend-keep.internal:5001"
                            }
                        ]
                    },
                    new ClusterDto
                    {
                        ClusterId = "cluster-delete",
                        Destinations =
                        [
                            new DestinationDto
                            {
                                DestinationId = "destination-delete",
                                Address = "https://backend-delete.internal:5002"
                            }
                        ]
                    }
                ]
            },
            CancellationToken.None);

        await configurationService.SaveConfigurationAsync(
            new HelgrindConfigurationDto
            {
                Routes =
                [
                    new RouteDto
                    {
                        RouteId = "route-keep",
                        ClusterId = "cluster-keep",
                        Path = "/keep/{**catch-all}",
                        Hosts = ["keep.example.com"],
                    }
                ],
                Clusters =
                [
                    new ClusterDto
                    {
                        ClusterId = "cluster-keep",
                        Destinations =
                        [
                            new DestinationDto
                            {
                                DestinationId = "destination-keep",
                                Address = "https://backend-keep.internal:5001"
                            }
                        ]
                    }
                ]
            },
            CancellationToken.None);

        var configuration = await configurationService.GetConfigurationAsync(CancellationToken.None);

        var route = Assert.Single(configuration.Routes);
        Assert.Equal("route-keep", route.RouteId);
        var cluster = Assert.Single(configuration.Clusters);
        Assert.Equal("cluster-keep", cluster.ClusterId);
        Assert.DoesNotContain(configuration.Routes, item => item.RouteId == "route-delete");
        Assert.DoesNotContain(configuration.Clusters, item => item.ClusterId == "cluster-delete");
        Assert.DoesNotContain(dbContext.Routes, item => item.RouteId == "route-delete");
        Assert.DoesNotContain(dbContext.Clusters, item => item.ClusterId == "cluster-delete");
        Assert.DoesNotContain(dbContext.Destinations, item => item.ClusterId == "cluster-delete");
    }

    [Fact]
    public async Task SaveConfigurationAsync_RejectsReplacingPopulatedConfigurationWithEmptyDraft()
    {
        await using var dbContext = CreateDbContext();
        var hostEnvironment = new TestWebHostEnvironment(_contentRootPath);
        var options = Microsoft.Extensions.Options.Options.Create(new HelgrindOptions
        {
            PublicHttpsPort = 443,
            AdminHttpsPort = 8444,
            DatabasePath = "App_Data/helgrind.db",
            CertificateStoragePath = "App_Data/certificates"
        });
        var runtimeState = new CertificateRuntimeState();
        var certificateService = new CertificateService(dbContext, runtimeState, hostEnvironment, options);
        var selfUpdateService = new SelfUpdateService(options, hostEnvironment, NullLogger<SelfUpdateService>.Instance);
        var configurationService = new ConfigurationService(
            dbContext,
            new ProxyConfigFactory(),
            new InMemoryProxyConfigProvider(),
            certificateService,
            options,
            hostEnvironment,
            new AdminAccessService(options),
            selfUpdateService);

        await configurationService.InitializeAsync(CancellationToken.None);
        await configurationService.SaveConfigurationAsync(
            new HelgrindConfigurationDto
            {
                Routes =
                [
                    new RouteDto
                    {
                        RouteId = "route1",
                        ClusterId = "cluster1",
                        Path = "{**catch-all}",
                        Hosts = ["api.example.com"]
                    }
                ],
                Clusters =
                [
                    new ClusterDto
                    {
                        ClusterId = "cluster1",
                        Destinations =
                        [
                            new DestinationDto
                            {
                                DestinationId = "destination1",
                                Address = "https://backend.internal:5001"
                            }
                        ]
                    }
                ]
            },
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            configurationService.SaveConfigurationAsync(new HelgrindConfigurationDto(), CancellationToken.None));

        var configuration = await configurationService.GetConfigurationAsync(CancellationToken.None);

        Assert.Equal("Refusing to replace a populated configuration with an empty draft.", exception.Message);
        Assert.Single(configuration.Routes);
        Assert.Single(configuration.Clusters);
    }

    [Fact]
    public async Task SaveConfigurationAsync_AllowsReplacingPopulatedConfigurationWithEmptyDraft_WhenExplicitlyPermitted()
    {
        await using var dbContext = CreateDbContext();
        var hostEnvironment = new TestWebHostEnvironment(_contentRootPath);
        var options = Microsoft.Extensions.Options.Options.Create(new HelgrindOptions
        {
            PublicHttpsPort = 443,
            AdminHttpsPort = 8444,
            DatabasePath = "App_Data/helgrind.db",
            CertificateStoragePath = "App_Data/certificates"
        });
        var runtimeState = new CertificateRuntimeState();
        var certificateService = new CertificateService(dbContext, runtimeState, hostEnvironment, options);
        var selfUpdateService = new SelfUpdateService(options, hostEnvironment, NullLogger<SelfUpdateService>.Instance);
        var configurationService = new ConfigurationService(
            dbContext,
            new ProxyConfigFactory(),
            new InMemoryProxyConfigProvider(),
            certificateService,
            options,
            hostEnvironment,
            new AdminAccessService(options),
            selfUpdateService);

        await configurationService.InitializeAsync(CancellationToken.None);
        await configurationService.SaveConfigurationAsync(
            new HelgrindConfigurationDto
            {
                Routes =
                [
                    new RouteDto
                    {
                        RouteId = "route1",
                        ClusterId = "cluster1",
                        Path = "{**catch-all}",
                        Hosts = ["api.example.com"]
                    }
                ],
                Clusters =
                [
                    new ClusterDto
                    {
                        ClusterId = "cluster1",
                        Destinations =
                        [
                            new DestinationDto
                            {
                                DestinationId = "destination1",
                                Address = "https://backend.internal:5001"
                            }
                        ]
                    }
                ]
            },
            CancellationToken.None);

        await configurationService.SaveConfigurationAsync(new HelgrindConfigurationDto(), CancellationToken.None, allowEmpty: true);

        var configuration = await configurationService.GetConfigurationAsync(CancellationToken.None);

        Assert.Empty(configuration.Routes);
        Assert.Empty(configuration.Clusters);
    }

    [Fact]
    public async Task SaveConfigurationAsync_AllowsSameDestinationIdAcrossDifferentClusters()
    {
        await using var dbContext = CreateDbContext();
        var hostEnvironment = new TestWebHostEnvironment(_contentRootPath);
        var options = Microsoft.Extensions.Options.Options.Create(new HelgrindOptions
        {
            PublicHttpsPort = 443,
            AdminHttpsPort = 8444,
            DatabasePath = "App_Data/helgrind.db",
            CertificateStoragePath = "App_Data/certificates"
        });
        var runtimeState = new CertificateRuntimeState();
        var certificateService = new CertificateService(dbContext, runtimeState, hostEnvironment, options);
        var selfUpdateService = new SelfUpdateService(options, hostEnvironment, NullLogger<SelfUpdateService>.Instance);
        var configurationService = new ConfigurationService(
            dbContext,
            new ProxyConfigFactory(),
            new InMemoryProxyConfigProvider(),
            certificateService,
            options,
            hostEnvironment,
            new AdminAccessService(options),
            selfUpdateService);

        await configurationService.InitializeAsync(CancellationToken.None);

        await configurationService.SaveConfigurationAsync(
            new HelgrindConfigurationDto
            {
                Routes =
                [
                    new RouteDto
                    {
                        RouteId = "route1",
                        ClusterId = "cluster1",
                        Path = "{**catch-all}",
                        Hosts = ["api.example.com"]
                    },
                    new RouteDto
                    {
                        RouteId = "route2",
                        ClusterId = "cluster2",
                        Path = "{**catch-all}",
                        Hosts = ["www.example.com"]
                    }
                ],
                Clusters =
                [
                    new ClusterDto
                    {
                        ClusterId = "cluster1",
                        Destinations =
                        [
                            new DestinationDto
                            {
                                DestinationId = "destination1",
                                Address = "https://backend-one.internal:5001"
                            }
                        ]
                    },
                    new ClusterDto
                    {
                        ClusterId = "cluster2",
                        Destinations =
                        [
                            new DestinationDto
                            {
                                DestinationId = "destination1",
                                Address = "https://backend-two.internal:5002"
                            }
                        ]
                    }
                ]
            },
            CancellationToken.None);

        var configuration = await configurationService.GetConfigurationAsync(CancellationToken.None);

        Assert.Equal(2, configuration.Clusters.Count);
        Assert.Equal(2, configuration.Routes.Count);
        Assert.All(configuration.Clusters, cluster => Assert.Single(cluster.Destinations));
        Assert.Contains(configuration.Clusters, cluster =>
            cluster.ClusterId == "cluster1"
            && cluster.Destinations.Single().DestinationId == "destination1"
            && cluster.Destinations.Single().Address == "https://backend-one.internal:5001");
        Assert.Contains(configuration.Clusters, cluster =>
            cluster.ClusterId == "cluster2"
            && cluster.Destinations.Single().DestinationId == "destination1"
            && cluster.Destinations.Single().Address == "https://backend-two.internal:5002");
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(_contentRootPath))
        {
            Directory.Delete(_contentRootPath, true);
        }
    }

    private HelgrindDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HelgrindDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new HelgrindDbContext(options);
    }

    private sealed class TestWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Helgrind.Tests";

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string WebRootPath { get; set; } = string.Empty;

        public string EnvironmentName { get; set; } = "Development";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
    }

}