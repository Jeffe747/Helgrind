using System.Net;

namespace Helgrind.Services;

public sealed class PublicClientAddressResolver
{
    private const string CloudflareConnectingIpHeader = "CF-Connecting-IP";

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

        var forwardedAddress = context.Request.Headers[CloudflareConnectingIpHeader].ToString();
        if (IPAddress.TryParse(forwardedAddress, out var parsedForwardedAddress))
        {
            return NetworkRange.Normalize(parsedForwardedAddress);
        }

        return normalizedRemoteAddress;
    }
}