using Helgrind.Options;
using Helgrind.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Helgrind.Tests;

public sealed class SelfUpdateServiceTests : IDisposable
{
    private readonly string _contentRootPath = Path.Combine(Path.GetTempPath(), $"helgrind-self-update-{Guid.NewGuid():N}");

    public SelfUpdateServiceTests()
    {
        Directory.CreateDirectory(_contentRootPath);
    }

    [Fact]
    public void IsConfigured_ReturnsFalse_InDevelopment_EvenWhenCommandExists()
    {
        var service = CreateService(new HelgrindOptions
        {
            SelfUpdateCommand = "echo updating"
        }, environmentName: "Development");

        Assert.False(service.IsConfigured);
        Assert.Contains("disabled in Development", service.GetStatusMessage(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsConfigured_ReturnsTrue_InProduction_WhenConfiguredCommandExists()
    {
        var service = CreateService(new HelgrindOptions
        {
            SelfUpdateCommand = "echo updating"
        }, environmentName: "Production");

        Assert.True(service.IsConfigured);
        Assert.Contains("configured production self-update command", service.GetStatusMessage(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsConfigured_ReturnsFalse_WhenNoCommandExists()
    {
        var service = CreateService(new HelgrindOptions(), environmentName: "Production");

        Assert.False(service.IsConfigured);
    }

    public void Dispose()
    {
        if (Directory.Exists(_contentRootPath))
        {
            Directory.Delete(_contentRootPath, true);
        }
    }

    private SelfUpdateService CreateService(HelgrindOptions options, string environmentName)
    {
        var environment = new TestWebHostEnvironment(_contentRootPath)
        {
            EnvironmentName = environmentName
        };
        return new SelfUpdateService(Microsoft.Extensions.Options.Options.Create(options), environment, new TestHostApplicationLifetime(), NullLogger<SelfUpdateService>.Instance);
    }

    private sealed class TestWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Helgrind.Tests";

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string WebRootPath { get; set; } = string.Empty;

        public string EnvironmentName { get; set; } = "Development";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
    }

    private sealed class TestHostApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }
}