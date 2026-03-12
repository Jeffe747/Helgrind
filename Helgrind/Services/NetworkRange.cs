using System.Net;
using System.Net.Sockets;

namespace Helgrind.Services;

public sealed class NetworkRange(IPAddress networkAddress, int prefixLength)
{
    public static NetworkRange Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Network range cannot be empty.");
        }

        var trimmedValue = value.Trim();
        if (!trimmedValue.Contains('/', StringComparison.Ordinal))
        {
            var address = Normalize(IPAddress.Parse(trimmedValue));
            return new NetworkRange(address, GetMaxPrefixLength(address.AddressFamily));
        }

        var parts = trimmedValue.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            throw new InvalidOperationException($"Invalid CIDR value '{value}'.");
        }

        var parsedAddress = Normalize(IPAddress.Parse(parts[0]));
        if (!int.TryParse(parts[1], out var parsedPrefixLength))
        {
            throw new InvalidOperationException($"Invalid prefix length in CIDR value '{value}'.");
        }

        var maxPrefixLength = GetMaxPrefixLength(parsedAddress.AddressFamily);
        if (parsedPrefixLength < 0 || parsedPrefixLength > maxPrefixLength)
        {
            throw new InvalidOperationException($"CIDR prefix length '{parsedPrefixLength}' is out of range for '{value}'.");
        }

        return new NetworkRange(parsedAddress, parsedPrefixLength);
    }

    public static bool TryParse(string value, out NetworkRange? range)
    {
        try
        {
            range = Parse(value);
            return true;
        }
        catch
        {
            range = null;
            return false;
        }
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

    public override string ToString() => $"{networkAddress}/{prefixLength}";

    public static IPAddress Normalize(IPAddress address)
        => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private static int GetMaxPrefixLength(AddressFamily addressFamily) => addressFamily switch
    {
        AddressFamily.InterNetwork => 32,
        AddressFamily.InterNetworkV6 => 128,
        _ => throw new InvalidOperationException($"Unsupported address family '{addressFamily}'.")
    };
}