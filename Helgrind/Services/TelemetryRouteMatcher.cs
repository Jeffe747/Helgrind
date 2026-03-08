using Yarp.ReverseProxy.Configuration;

namespace Helgrind.Services;

public sealed class TelemetryRouteMatcher(InMemoryProxyConfigProvider proxyConfigProvider)
{
    public RouteMatchResult Match(string host, string path)
    {
        var routes = proxyConfigProvider.GetConfig().Routes
            .OrderBy(route => route.Order ?? 0)
            .ThenBy(route => route.RouteId, StringComparer.OrdinalIgnoreCase);

        var anyHostMatched = false;
        var anyPathMatched = false;

        foreach (var route in routes)
        {
            var hostMatched = HostsMatch(route.Match?.Hosts, host);
            var pathMatched = PathMatches(route.Match?.Path, path);
            anyHostMatched |= hostMatched;
            anyPathMatched |= pathMatched;

            if (hostMatched && pathMatched)
            {
                return new RouteMatchResult(true, route.RouteId, route.ClusterId, true, true);
            }
        }

        return new RouteMatchResult(false, null, null, anyHostMatched, anyPathMatched);
    }

    private static bool HostsMatch(IReadOnlyList<string>? routeHosts, string requestHost)
    {
        if (routeHosts is null || routeHosts.Count == 0)
        {
            return true;
        }

        return routeHosts.Any(host => string.Equals(host, requestHost, StringComparison.OrdinalIgnoreCase));
    }

    private static bool PathMatches(string? routePattern, string requestPath)
    {
        if (string.IsNullOrWhiteSpace(routePattern) || routePattern == "{**catch-all}")
        {
            return true;
        }

        var normalizedPath = NormalizePath(requestPath);
        var normalizedPattern = NormalizePath(routePattern);
        const string catchAllToken = "/{**catch-all}";

        if (normalizedPattern.EndsWith(catchAllToken, StringComparison.OrdinalIgnoreCase))
        {
            var prefix = normalizedPattern[..^catchAllToken.Length];
            if (string.IsNullOrEmpty(prefix))
            {
                return true;
            }

            return normalizedPath.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || normalizedPath.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
        }

        return normalizedPath.Equals(normalizedPattern, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "/";
        }

        var normalized = value.StartsWith('/') ? value : "/" + value;
        return normalized.Length > 1 ? normalized.TrimEnd('/') : normalized;
    }
}