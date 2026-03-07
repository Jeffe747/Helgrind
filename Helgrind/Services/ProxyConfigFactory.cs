using Helgrind.Contracts;
using Yarp.ReverseProxy.Configuration;

namespace Helgrind.Services;

public sealed class ProxyConfigFactory
{
    public ProxyConfigBuildResult Build(HelgrindConfigurationDto configuration)
    {
        var errors = new List<string>();
        var routes = new List<RouteConfig>();
        var clusters = new List<ClusterConfig>();

        var clusterLookup = configuration.Clusters
            .GroupBy(cluster => cluster.ClusterId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var duplicateCluster in configuration.Clusters.GroupBy(cluster => cluster.ClusterId, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            errors.Add($"Duplicate cluster id '{duplicateCluster.Key}'.");
        }

        foreach (var duplicateRoute in configuration.Routes.GroupBy(route => route.RouteId, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            errors.Add($"Duplicate route id '{duplicateRoute.Key}'.");
        }

        foreach (var cluster in configuration.Clusters)
        {
            if (string.IsNullOrWhiteSpace(cluster.ClusterId))
            {
                errors.Add("Cluster id is required.");
                continue;
            }

            if (cluster.Destinations.Count == 0)
            {
                errors.Add($"Cluster '{cluster.ClusterId}' must contain at least one destination.");
                continue;
            }

            if (cluster.Destinations.GroupBy(destination => destination.DestinationId, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            {
                errors.Add($"Cluster '{cluster.ClusterId}' contains duplicate destination ids.");
            }

            ActiveHealthCheckConfig? activeHealthCheck = null;
            Dictionary<string, string>? metadata = null;

            if (cluster.HealthCheck.Enabled)
            {
                if (!TimeSpan.TryParse(cluster.HealthCheck.Interval, out var interval))
                {
                    errors.Add($"Cluster '{cluster.ClusterId}' has an invalid health check interval.");
                }

                if (!TimeSpan.TryParse(cluster.HealthCheck.Timeout, out var timeout))
                {
                    errors.Add($"Cluster '{cluster.ClusterId}' has an invalid health check timeout.");
                }

                if (errors.Count == 0 || (TimeSpan.TryParse(cluster.HealthCheck.Interval, out interval) && TimeSpan.TryParse(cluster.HealthCheck.Timeout, out timeout)))
                {
                    activeHealthCheck = new ActiveHealthCheckConfig
                    {
                        Enabled = true,
                        Interval = interval,
                        Timeout = timeout,
                        Policy = string.IsNullOrWhiteSpace(cluster.HealthCheck.Policy) ? "ConsecutiveFailures" : cluster.HealthCheck.Policy,
                        Path = string.IsNullOrWhiteSpace(cluster.HealthCheck.Path) ? "/" : cluster.HealthCheck.Path,
                        Query = cluster.HealthCheck.Query,
                    };
                }

                if (cluster.ConsecutiveFailuresThreshold is { } threshold)
                {
                    metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["ConsecutiveFailuresHealthPolicy.Threshold"] = threshold.ToString()
                    };
                }
            }

            clusters.Add(new ClusterConfig
            {
                ClusterId = cluster.ClusterId,
                LoadBalancingPolicy = string.IsNullOrWhiteSpace(cluster.LoadBalancingPolicy) ? null : cluster.LoadBalancingPolicy,
                HealthCheck = activeHealthCheck is null ? null : new HealthCheckConfig { Active = activeHealthCheck },
                Metadata = metadata,
                Destinations = cluster.Destinations.ToDictionary(
                    destination => destination.DestinationId,
                    destination => new DestinationConfig { Address = destination.Address },
                    StringComparer.OrdinalIgnoreCase)
            });
        }

        foreach (var route in configuration.Routes)
        {
            if (string.IsNullOrWhiteSpace(route.RouteId))
            {
                errors.Add("Route id is required.");
                continue;
            }

            if (!clusterLookup.ContainsKey(route.ClusterId))
            {
                errors.Add($"Route '{route.RouteId}' references missing cluster '{route.ClusterId}'.");
                continue;
            }

            if (route.Hosts.Count == 0)
            {
                errors.Add($"Route '{route.RouteId}' must contain at least one host.");
                continue;
            }

            routes.Add(new RouteConfig
            {
                RouteId = route.RouteId,
                ClusterId = route.ClusterId,
                Order = route.Order,
                Match = new RouteMatch
                {
                    Hosts = route.Hosts,
                    Path = string.IsNullOrWhiteSpace(route.Path) ? "{**catch-all}" : route.Path,
                }
            });
        }

        return new ProxyConfigBuildResult(routes, clusters, errors);
    }
}

public sealed record ProxyConfigBuildResult(
    IReadOnlyList<RouteConfig> Routes,
    IReadOnlyList<ClusterConfig> Clusters,
    IReadOnlyList<string> Errors);