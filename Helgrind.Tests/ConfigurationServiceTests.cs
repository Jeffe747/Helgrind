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