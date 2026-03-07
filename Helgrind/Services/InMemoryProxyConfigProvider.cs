using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace Helgrind.Services;

public sealed class InMemoryProxyConfigProvider : IProxyConfigProvider
{
    private volatile InMemoryProxyConfig _current = new([], []);

    public IProxyConfig GetConfig() => _current;

    public void Update(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters)
    {
        var next = new InMemoryProxyConfig(routes, clusters);
        var previous = Interlocked.Exchange(ref _current, next);
        previous.SignalChange();
    }

    private sealed class InMemoryProxyConfig : IProxyConfig
    {
        private readonly CancellationTokenSource _cancellationTokenSource = new();

        public IReadOnlyList<RouteConfig> Routes { get; }

        public IReadOnlyList<ClusterConfig> Clusters { get; }

        public IChangeToken ChangeToken { get; }

        public InMemoryProxyConfig(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters)
        {
            Routes = routes;
            Clusters = clusters;
            ChangeToken = new CancellationChangeToken(_cancellationTokenSource.Token);
        }

        public void SignalChange() => _cancellationTokenSource.Cancel();
    }
}