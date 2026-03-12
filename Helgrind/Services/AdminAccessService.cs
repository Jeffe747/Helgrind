using System.Net;
using System.Net.Sockets;
using Helgrind.Options;
using Microsoft.Extensions.Options;

namespace Helgrind.Services;

public sealed class AdminAccessService
{
    private readonly IReadOnlyList<NetworkRange> _allowedNetworks;
    private readonly string _summary;

    public AdminAccessService(IOptions<HelgrindOptions> options)
    {
        var configuredRanges = options.Value.AllowedAdminNetworks
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _allowedNetworks = configuredRanges
            .Select(NetworkRange.Parse)
            .ToList();
        _summary = string.Join(", ", configuredRanges);
    }

    public bool IsAllowed(IPAddress? address)
    {
        if (address is null)
        {
            return false;
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        var normalizedAddress = Normalize(address);
        return _allowedNetworks.Any(range => range.Contains(normalizedAddress));
    }

    public string GetSummary() => _summary;

    private static IPAddress Normalize(IPAddress address)
    {
        return NetworkRange.Normalize(address);
    }
}