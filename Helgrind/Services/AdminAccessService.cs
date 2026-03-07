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
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    private sealed class NetworkRange(IPAddress networkAddress, int prefixLength)
    {
        public static NetworkRange Parse(string value)
        {
            var parts = value.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                throw new InvalidOperationException($"Invalid CIDR value '{value}'.");
            }

            var address = Normalize(IPAddress.Parse(parts[0]));
            if (!int.TryParse(parts[1], out var prefixLength))
            {
                throw new InvalidOperationException($"Invalid prefix length in CIDR value '{value}'.");
            }

            return new NetworkRange(address, prefixLength);
        }

        public bool Contains(IPAddress address)
        {
            var normalizedAddress = Normalize(address);
            if (normalizedAddress.AddressFamily != networkAddress.AddressFamily)
            {
                return false;
            }

            var addressBytes = normalizedAddress.GetAddressBytes();
            var networkBytes = networkAddress.GetAddressBytes();
            var fullBytes = prefixLength / 8;
            var remainingBits = prefixLength % 8;

            for (var index = 0; index < fullBytes; index++)
            {
                if (addressBytes[index] != networkBytes[index])
                {
                    return false;
                }
            }

            if (remainingBits == 0)
            {
                return true;
            }

            var mask = (byte)(0xFF << (8 - remainingBits));
            return (addressBytes[fullBytes] & mask) == (networkBytes[fullBytes] & mask);
        }
    }
}