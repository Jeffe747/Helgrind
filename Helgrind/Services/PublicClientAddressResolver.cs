using System.Net;

namespace Helgrind.Services;

public sealed class PublicClientAddressResolver
{
    private const string CloudflareConnectingIpHeader = "CF-Connecting-IP";
    private const string XForwardedForHeader = "X-Forwarded-For";

    public IPAddress? Resolve(HttpContext context)
    {
        var remoteAddress = context.Connection.RemoteIpAddress;
        if (remoteAddress is null)
        {
            return null;
        }

        var normalizedRemoteAddress = NetworkRange.Normalize(remoteAddress);
        if (!YarpConfiguration.CloudflareNetworks.Any(range => range.Contains(normalizedRemoteAddress)))
        {
            return normalizedRemoteAddress;
        }

        if (TryResolveForwardedAddress(context.Request.Headers[CloudflareConnectingIpHeader].ToString(), out var parsedForwardedAddress))
        {
            return NetworkRange.Normalize(parsedForwardedAddress);
        }

        if (TryResolveForwardedAddress(context.Request.Headers[XForwardedForHeader].ToString(), out parsedForwardedAddress))
        {
            return NetworkRange.Normalize(parsedForwardedAddress);
        }

        return normalizedRemoteAddress;
    }

    private static bool TryResolveForwardedAddress(string headerValue, out IPAddress parsedAddress)
    {
        parsedAddress = IPAddress.None;
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return false;
        }

        var firstValue = headerValue
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(firstValue))
        {
            return false;
        }

        if (!IPAddress.TryParse(firstValue, out var parsedCandidate) || parsedCandidate is null)
        {
            return false;
        }

        parsedAddress = parsedCandidate;
        return true;
    }
}