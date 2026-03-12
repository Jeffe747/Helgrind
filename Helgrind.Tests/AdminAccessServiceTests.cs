using System.Net;
using Helgrind.Options;
using Helgrind.Services;

namespace Helgrind.Tests;

public sealed class AdminAccessServiceTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("192.168.1.25")]
    [InlineData("10.0.4.99")]
    [InlineData("172.16.8.11")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    public void IsAllowed_ReturnsTrue_ForLoopbackAndPrivateNetworks(string address)
    {
        var service = CreateService();

        Assert.True(service.IsAllowed(IPAddress.Parse(address)));
    }

    [Fact]
    public void IsAllowed_SupportsSingleIpEntries()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new Helgrind.Options.HelgrindOptions
        {
            AllowedAdminNetworks = ["203.0.113.14"]
        });
        var service = new AdminAccessService(options);

        Assert.True(service.IsAllowed(IPAddress.Parse("203.0.113.14")));
        Assert.False(service.IsAllowed(IPAddress.Parse("203.0.113.15")));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("2606:4700:4700::1111")]
    public void IsAllowed_ReturnsFalse_ForPublicNetworks(string address)
    {
        var service = CreateService();

        Assert.False(service.IsAllowed(IPAddress.Parse(address)));
    }

    private static AdminAccessService CreateService()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new HelgrindOptions());
        return new AdminAccessService(options);
    }
}