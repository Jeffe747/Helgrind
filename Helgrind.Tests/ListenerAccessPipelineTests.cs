using System.Net;
using Helgrind.Contracts;
using Helgrind.Options;
using Helgrind.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Helgrind.Tests;

public sealed class ListenerAccessPipelineTests
{
    [Fact]
    public async Task AdminListener_AllowsLanAndBlocksNonLan()
    {
        var (pipeline, serviceProvider) = CreatePipeline(new HelgrindConfigurationDto());

        var allowedContext = CreateContext(serviceProvider, localPort: 8444, remoteIp: "192.168.1.50");
        await pipeline(allowedContext);

        Assert.Equal(StatusCodes.Status200OK, allowedContext.Response.StatusCode);
        Assert.Equal("admin-ok", await ReadBodyAsync(allowedContext));

        var blockedContext = CreateContext(serviceProvider, localPort: 8444, remoteIp: "203.0.113.20");
        await pipeline(blockedContext);

        Assert.Equal(StatusCodes.Status403Forbidden, blockedContext.Response.StatusCode);
        Assert.Equal("The Helgrind management UI is restricted to configured LAN networks.", await ReadBodyAsync(blockedContext));
    }

    [Fact]
    public async Task PublicListener_AllowsUnrestrictedRoute()
    {
        var (pipeline, serviceProvider) = CreatePipeline(new HelgrindConfigurationDto
        {
            Routes =
            [
                new RouteDto
                {
                    RouteId = "route1",
                    ClusterId = "cluster1",
                    Hosts = ["assistant.icicle.dk"],
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
            ]
        });

        var context = CreateContext(serviceProvider, localPort: 443, remoteIp: "203.0.113.20", host: "assistant.icicle.dk");
        await pipeline(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("public-ok", await ReadBodyAsync(context));
    }

    [Fact]
    public async Task PublicListener_RestrictsRouteToConfiguredNetworks()
    {
        var (pipeline, serviceProvider) = CreatePipeline(new HelgrindConfigurationDto
        {
            Routes =
            [
                new RouteDto
                {
                    RouteId = "route1",
                    ClusterId = "cluster1",
                    Hosts = ["assistant.icicle.dk"],
                    Path = "{**catch-all}",
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

        var allowedContext = CreateContext(serviceProvider, localPort: 443, remoteIp: "85.184.162.188", host: "assistant.icicle.dk");
        await pipeline(allowedContext);

        Assert.Equal(StatusCodes.Status200OK, allowedContext.Response.StatusCode);
        Assert.Equal("public-ok", await ReadBodyAsync(allowedContext));

        var blockedContext = CreateContext(serviceProvider, localPort: 443, remoteIp: "203.0.113.20", host: "assistant.icicle.dk");
        await pipeline(blockedContext);

        Assert.Equal(StatusCodes.Status403Forbidden, blockedContext.Response.StatusCode);
        Assert.Equal("This route is restricted to configured client networks.", await ReadBodyAsync(blockedContext));
    }

    private static (RequestDelegate Pipeline, IServiceProvider Services) CreatePipeline(HelgrindConfigurationDto configuration)
    {
        var options = new HelgrindOptions
        {
            PublicHttpsPort = 443,
            AdminHttpsPort = 8444,
        };

        var services = new ServiceCollection();
        services.AddSingleton<IOptions<HelgrindOptions>>(Microsoft.Extensions.Options.Options.Create(options));
        services.AddSingleton<AdminAccessService>();
        services.AddSingleton<PublicClientAddressResolver>();

        var provider = new InMemoryProxyConfigProvider();
        var buildResult = new ProxyConfigFactory().Build(configuration);
        Assert.Empty(buildResult.Errors);
        provider.Update(buildResult.Routes, buildResult.Clusters);

        services.AddSingleton(provider);
        services.AddSingleton<TelemetryRouteMatcher>();

        var serviceProvider = services.BuildServiceProvider();
        var app = new ApplicationBuilder(serviceProvider);

        app.MapWhen(context => context.Connection.LocalPort == options.AdminHttpsPort, adminApp =>
        {
            adminApp.Use(async (context, next) =>
            {
                var adminAccessService = context.RequestServices.GetRequiredService<AdminAccessService>();
                if (!adminAccessService.IsAllowed(context.Connection.RemoteIpAddress))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsync("The Helgrind management UI is restricted to configured LAN networks.");
                    return;
                }

                await next();
            });

            adminApp.Run(async context =>
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                await context.Response.WriteAsync("admin-ok");
            });
        });

        app.MapWhen(context => context.Connection.LocalPort == options.PublicHttpsPort, publicApp =>
        {
            publicApp.UseMiddleware<PublicRouteAccessMiddleware>();
            publicApp.Run(async context =>
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                await context.Response.WriteAsync("public-ok");
            });
        });

        app.Run(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync("not-found");
        });

        return (app.Build(), serviceProvider);
    }

    private static DefaultHttpContext CreateContext(IServiceProvider serviceProvider, int localPort, string remoteIp, string host = "localhost", string path = "/")
    {
        var context = new DefaultHttpContext();
        context.RequestServices = serviceProvider;
        context.Request.Host = new HostString(host);
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        context.Features.Set<IHttpConnectionFeature>(new HttpConnectionFeature
        {
            LocalPort = localPort,
            RemoteIpAddress = IPAddress.Parse(remoteIp)
        });
        return context;
    }

    private static async Task<string> ReadBodyAsync(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }
}