using System.Net;
using System.Text;
using Helgrind.Options;
using Helgrind.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Helgrind.Tests;

public sealed class TelemetryAlertServiceTests
{
    [Fact]
    public async Task TrySendAlertAsync_SendsWebhook_ForHighRiskBatch()
    {
        var handler = new RecordingHandler();
        var service = CreateService(handler, new HelgrindOptions
        {
            TelemetryAlertWebhookUrl = "https://alerts.example.test/webhook",
            TelemetryAlertMinimumRiskScore = 3,
            TelemetryAlertCooldownMinutes = 10,
        });

        var sent = await service.TrySendAlertAsync(
        [
            new SuspiciousRequestEventRecord(
                DateTimeOffset.UtcNow,
                "203.0.113.10",
                "edge.example.com",
                "GET",
                "/.env",
                string.Empty,
                404,
                null,
                null,
                "ExploitPath",
                "High",
                3,
                "probe")
        ],
        CancellationToken.None);

        Assert.True(sent);
        Assert.Single(handler.Requests);
        Assert.Contains("Helgrind detected 1 high-risk public probe", handler.Requests[0], StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(service.GetSnapshot().LastAlertSentUtc);
    }

    [Fact]
    public async Task TrySendAlertAsync_RespectsCooldown()
    {
        var handler = new RecordingHandler();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var service = CreateService(handler, new HelgrindOptions
        {
            TelemetryAlertWebhookUrl = "https://alerts.example.test/webhook",
            TelemetryAlertMinimumRiskScore = 3,
            TelemetryAlertCooldownMinutes = 10,
        }, timeProvider);
        var batch = new[]
        {
            new SuspiciousRequestEventRecord(
                timeProvider.GetUtcNow(),
                "203.0.113.10",
                "edge.example.com",
                "GET",
                "/.env",
                string.Empty,
                404,
                null,
                null,
                "ExploitPath",
                "High",
                3,
                "probe")
        };

        var firstSent = await service.TrySendAlertAsync(batch, CancellationToken.None);
        var secondSent = await service.TrySendAlertAsync(batch, CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromMinutes(11));
        var thirdSent = await service.TrySendAlertAsync(batch, CancellationToken.None);

        Assert.True(firstSent);
        Assert.False(secondSent);
        Assert.True(thirdSent);
        Assert.Equal(2, handler.Requests.Count);
        Assert.NotNull(service.GetSnapshot().CooldownUntilUtc);
    }

    private static TelemetryAlertService CreateService(RecordingHandler handler, HelgrindOptions options, TimeProvider? timeProvider = null)
        => new(
            new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(options),
            timeProvider ?? TimeProvider.System,
            NullLogger<TelemetryAlertService>.Instance);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.NoContent)
            {
                Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration)
        {
            _utcNow = _utcNow.Add(duration);
        }
    }
}