using System.Net;
using Helgrind.Services;
using Microsoft.AspNetCore.Http;

namespace Helgrind.Tests;

public sealed class PublicClientAddressResolverTests
{
    private readonly PublicClientAddressResolver _resolver = new();

    [Fact]
    public void Resolve_ReturnsRemoteAddress_WhenPeerIsNotTrustedCloudflare()
    {
        var context = CreateContext("203.0.113.9");
        context.Request.Headers["CF-Connecting-IP"] = "85.184.162.188";

        var result = _resolver.Resolve(context);

        Assert.Equal(IPAddress.Parse("203.0.113.9"), result);
    }

    [Fact]
    public void Resolve_UsesCfConnectingIp_WhenPeerIsTrustedCloudflare()
    {
        var context = CreateContext("104.16.0.10");
        context.Request.Headers["CF-Connecting-IP"] = "85.184.162.188";

        var result = _resolver.Resolve(context);

        Assert.Equal(IPAddress.Parse("85.184.162.188"), result);
    }

    [Fact]
    public void Resolve_UsesXForwardedFor_WhenCfConnectingIpIsMissing()
    {
        var context = CreateContext("104.16.0.10");
        context.Request.Headers["X-Forwarded-For"] = "85.184.162.188, 104.16.0.10";

        var result = _resolver.Resolve(context);

        Assert.Equal(IPAddress.Parse("85.184.162.188"), result);
    }

    [Fact]
    public void Resolve_TrustsCloudflareIpv6Peers()
    {
        var context = CreateContext("2606:4700::6810:85e5");
        context.Request.Headers["CF-Connecting-IP"] = "85.184.162.188";

        var result = _resolver.Resolve(context);

        Assert.Equal(IPAddress.Parse("85.184.162.188"), result);
    }

    private static DefaultHttpContext CreateContext(string remoteIp)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        return context;
    }
}