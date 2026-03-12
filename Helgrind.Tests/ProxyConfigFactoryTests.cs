using Helgrind.Contracts;
using Helgrind.Services;
using System.Text.Json;

namespace Helgrind.Tests;

public sealed class ProxyConfigFactoryTests
{
    private readonly ProxyConfigFactory _factory = new();

    [Fact]
    public void Build_WithValidQuickSetup_ProducesProxySnapshot()
    {
        var configuration = new HelgrindConfigurationDto
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
                    ConsecutiveFailuresThreshold = 1,
                    HealthCheck = new HealthCheckDto
                    {
                        Enabled = true,
                        Interval = "00:00:10",
                        Timeout = "00:00:03",
                        Policy = "ConsecutiveFailures",
                        Path = "/api-up",
                        Query = "?foo=bar"
                    },
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
        };

        var result = _factory.Build(configuration);

        Assert.Empty(result.Errors);
        Assert.Single(result.Routes);
        Assert.Single(result.Clusters);
        Assert.Equal("cluster1", result.Routes[0].ClusterId);
        Assert.Equal("api.example.com", Assert.Single(result.Routes[0].Match.Hosts!));
        Assert.Equal("1", result.Clusters[0].Metadata!["ConsecutiveFailuresHealthPolicy.Threshold"]);
        Assert.Equal("https://backend.internal:5001", result.Clusters[0].Destinations!["destination1"].Address);
    }

    [Fact]
    public void Build_WithMissingClusterReference_ReturnsValidationError()
    {
        var configuration = new HelgrindConfigurationDto
        {
            Routes =
            [
                new RouteDto
                {
                    RouteId = "route1",
                    ClusterId = "cluster-missing",
                    Hosts = ["api.example.com"]
                }
            ]
        };

        var result = _factory.Build(configuration);

        Assert.Contains(result.Errors, error => error.Contains("cluster-missing", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(result.Routes);
    }

    [Fact]
    public void Build_WithInvalidHealthCheckInterval_ReturnsValidationError()
    {
        var configuration = new HelgrindConfigurationDto
        {
            Clusters =
            [
                new ClusterDto
                {
                    ClusterId = "cluster1",
                    HealthCheck = new HealthCheckDto
                    {
                        Enabled = true,
                        Interval = "bad-value",
                        Timeout = "00:00:03"
                    },
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
        };

        var result = _factory.Build(configuration);

        Assert.Contains(result.Errors, error => error.Contains("invalid health check interval", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_WithAllowedClientNetworks_EmitsRouteMetadata()
    {
        var configuration = new HelgrindConfigurationDto
        {
            Routes =
            [
                new RouteDto
                {
                    RouteId = "route1",
                    ClusterId = "cluster1",
                    Hosts = ["assistant.icicle.dk"],
                    AllowedClientNetworks = ["85.184.162.188", "185.50.193.0/24"]
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
        };

        var result = _factory.Build(configuration);

        Assert.Empty(result.Errors);
        var route = Assert.Single(result.Routes);
        var serializedNetworks = route.Metadata![ProxyMetadataKeys.AllowedClientNetworks];
        var parsedNetworks = JsonSerializer.Deserialize<List<string>>(serializedNetworks);
        Assert.Equal(["85.184.162.188/32", "185.50.193.0/24"], parsedNetworks);
    }

    [Fact]
    public void Build_WithInvalidAllowedClientNetwork_ReturnsValidationError()
    {
        var configuration = new HelgrindConfigurationDto
        {
            Routes =
            [
                new RouteDto
                {
                    RouteId = "route1",
                    ClusterId = "cluster1",
                    Hosts = ["assistant.icicle.dk"],
                    AllowedClientNetworks = ["not-an-ip"]
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
        };

        var result = _factory.Build(configuration);

        Assert.Contains(result.Errors, error => error.Contains("allowed client network", StringComparison.OrdinalIgnoreCase));
    }
}