using System.Text.Json;
using Yarp.ReverseProxy.Configuration;

namespace Helgrind.Services;

public sealed class PublicRouteAccessMiddleware(RequestDelegate next)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(
        HttpContext context,
        TelemetryRouteMatcher routeMatcher,
        PublicClientAddressResolver clientAddressResolver)
    {
        var matchedRoute = routeMatcher.MatchRoute(context.Request.Host.Host, context.Request.Path.Value);
        if (matchedRoute is null || IsAllowed(matchedRoute, clientAddressResolver.Resolve(context)))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsync("This route is restricted to configured client networks.");
    }

    private static bool IsAllowed(RouteConfig route, System.Net.IPAddress? clientAddress)
    {
        var allowedNetworks = GetAllowedNetworks(route);
        if (allowedNetworks.Count == 0)
        {
            return true;
        }

        if (clientAddress is null)
        {
            return false;
        }

        return allowedNetworks.Any(range => range.Contains(clientAddress));
    }

    private static IReadOnlyList<NetworkRange> GetAllowedNetworks(RouteConfig route)
    {
        if (route.Metadata is null
            || !route.Metadata.TryGetValue(ProxyMetadataKeys.AllowedClientNetworks, out var metadataValue)
            || string.IsNullOrWhiteSpace(metadataValue))
        {
            return [];
        }

        var configuredNetworks = JsonSerializer.Deserialize<List<string>>(metadataValue, JsonOptions) ?? [];
        return configuredNetworks
            .Select(NetworkRange.Parse)
            .ToList();
    }
}