using System.Net;
using Helgrind.Contracts;
using Helgrind.Services;
using Microsoft.AspNetCore.Http;

namespace Helgrind.Tests;

public sealed class PublicRouteAccessMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_AllowsRequest_WhenClientNetworkIsWhitelisted()
    {
        var (routeMatcher, resolver) = CreateServices(new HelgrindConfigurationDto
        {
            Routes =
            [
                new RouteDto
                {
                    RouteId = "route1",
                    ClusterId = "cluster1",
                    Hosts = ["assistant.icicle.dk"],
                    AllowedClientNetworks = ["85.184.162.188"]
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
        });

        var context = CreateContext("assistant.icicle.dk", "/", "104.16.0.10", "85.184.162.188");
        var nextCalled = false;
        var middleware = new PublicRouteAccessMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, routeMatcher, resolver);

        Assert.True(nextCalled);
        Assert.NotEqual(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_BlocksRequest_WhenClientNetworkIsNotWhitelisted()
    {
        var (routeMatcher, resolver) = CreateServices(new HelgrindConfigurationDto
        {
            Routes =
            [
                new RouteDto
                {
                    RouteId = "route1",
                    ClusterId = "cluster1",
                    Hosts = ["assistant.icicle.dk"],
                    AllowedClientNetworks = ["85.184.162.188"]
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
        });

        var context = CreateContext("assistant.icicle.dk", "/", "104.16.0.10", "198.51.100.40");
        var middleware = new PublicRouteAccessMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, routeMatcher, resolver);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_IgnoresForgedCloudflareHeader_FromUntrustedPeer()
    {
        var (routeMatcher, resolver) = CreateServices(new HelgrindConfigurationDto
        {
            Routes =
            [
                new RouteDto
                {
                    RouteId = "route1",
                    ClusterId = "cluster1",
                    Hosts = ["assistant.icicle.dk"],
                    AllowedClientNetworks = ["85.184.162.188"]
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
        });

        var context = CreateContext("assistant.icicle.dk", "/", "203.0.113.77", "85.184.162.188");
        var middleware = new PublicRouteAccessMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, routeMatcher, resolver);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_AllowsUnrestrictedRoute()
    {
        var (routeMatcher, resolver) = CreateServices(new HelgrindConfigurationDto
        {
            Routes =
            [
                new RouteDto
                {
                    RouteId = "route1",
                    ClusterId = "cluster1",
                    Hosts = ["assistant.icicle.dk"]
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
        });

        var context = CreateContext("assistant.icicle.dk", "/", "198.51.100.40");
        var nextCalled = false;
        var middleware = new PublicRouteAccessMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, routeMatcher, resolver);

        Assert.True(nextCalled);
    }

    private static (TelemetryRouteMatcher RouteMatcher, PublicClientAddressResolver Resolver) CreateServices(HelgrindConfigurationDto configuration)
    {
        var factory = new ProxyConfigFactory();
        var buildResult = factory.Build(configuration);
        Assert.Empty(buildResult.Errors);

        var provider = new InMemoryProxyConfigProvider();
        provider.Update(buildResult.Routes, buildResult.Clusters);

        return (new TelemetryRouteMatcher(provider), new PublicClientAddressResolver());
    }

    private static DefaultHttpContext CreateContext(string host, string path, string remoteIp, string? forwardedIp = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        context.Response.Body = new MemoryStream();

        if (!string.IsNullOrWhiteSpace(forwardedIp))
        {
            context.Request.Headers["CF-Connecting-IP"] = forwardedIp;
        }

        return context;
    }
}