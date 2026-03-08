using System.Text;
using Helgrind.Options;
using Microsoft.Extensions.Options;

namespace Helgrind.Services;

public sealed class PublicTelemetryMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IOptions<HelgrindOptions> options,
        TelemetryRouteMatcher routeMatcher,
        TelemetryClassifierService classifier,
        TelemetryEventSink eventSink)
    {
        if (!options.Value.TelemetryEnabled)
        {
            await next(context);
            return;
        }

        var host = NormalizeHost(context.Request.Host.Host);
        var path = NormalizePath(context.Request.Path.Value);
        var routeMatch = routeMatcher.Match(host, path);
        var occurredUtc = DateTimeOffset.UtcNow;
        var statusCode = StatusCodes.Status500InternalServerError;

        try
        {
            await next(context);
            statusCode = context.Response.StatusCode;
        }
        catch
        {
            statusCode = StatusCodes.Status500InternalServerError;
            throw;
        }
        finally
        {
            var observation = new SuspiciousRequestObservation(
                occurredUtc,
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                host,
                context.Request.Method,
                path,
                SummarizeQuery(context.Request.Query),
                statusCode,
                routeMatch.RouteId,
                routeMatch.ClusterId,
                routeMatch.Matched,
                routeMatch.PathMatched,
                routeMatch.HostMatched);

            var telemetryEvent = classifier.Classify(observation);
            if (telemetryEvent is not null)
            {
                eventSink.Enqueue(telemetryEvent);
            }
        }
    }

    private static string NormalizeHost(string? host)
    {
        return string.IsNullOrWhiteSpace(host) ? "unknown" : host.Trim().ToLowerInvariant();
    }

    private static string NormalizePath(string? path)
    {
        return TelemetryPathUtility.NormalizePath(path);
    }

    private static string SummarizeQuery(IQueryCollection query)
    {
        if (query.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var key in query.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
        {
            if (builder.Length > 0)
            {
                builder.Append('&');
            }

            builder.Append(key);
            if (builder.Length >= 120)
            {
                break;
            }
        }

        return builder.ToString();
    }
}