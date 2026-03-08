using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Helgrind.Options;
using Microsoft.Extensions.Options;

namespace Helgrind.Services;

public sealed class TelemetryAlertService(
    HttpClient httpClient,
    IOptions<HelgrindOptions> options,
    TimeProvider timeProvider,
    ILogger<TelemetryAlertService> logger)
{
    private readonly Lock _sync = new();
    private DateTimeOffset _nextAlertUtc = DateTimeOffset.MinValue;
    private DateTimeOffset? _lastAlertSentUtc;
    private string _lastStatus = "Webhook alerts inactive.";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.TelemetryAlertWebhookUrl);

    public TelemetryAlertSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new TelemetryAlertSnapshot(
                IsConfigured,
                TelemetryRiskLevels.FromScore(Math.Max(TelemetryRiskLevels.LowScore, options.Value.TelemetryAlertMinimumRiskScore)),
                _lastAlertSentUtc,
                _nextAlertUtc == DateTimeOffset.MinValue ? null : _nextAlertUtc,
                _lastStatus);
        }
    }

    public async Task<bool> TrySendAlertAsync(IReadOnlyCollection<SuspiciousRequestEventRecord> batch, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            UpdateStatus("Webhook alerts inactive.");
            return false;
        }

        var minRiskScore = Math.Max(TelemetryRiskLevels.LowScore, options.Value.TelemetryAlertMinimumRiskScore);
        var highRiskEvents = batch
            .Where(eventRecord => eventRecord.RiskScore >= minRiskScore)
            .OrderByDescending(eventRecord => eventRecord.RiskScore)
            .ThenByDescending(eventRecord => eventRecord.OccurredUtc)
            .ToList();

        if (highRiskEvents.Count == 0)
        {
            UpdateStatus($"Webhook armed for {TelemetryRiskLevels.FromScore(minRiskScore)} risk events.");
            return false;
        }

        var now = timeProvider.GetUtcNow();
        if (now < _nextAlertUtc)
        {
            UpdateStatus($"Webhook cooling down until {_nextAlertUtc.LocalDateTime:t}.");
            return false;
        }

        var primaryEvent = highRiskEvents[0];
        var distinctSources = highRiskEvents.Select(eventRecord => eventRecord.RemoteAddress).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var payload = JsonSerializer.Serialize(new
        {
            content = $"Helgrind detected {highRiskEvents.Count} high-risk public probe(s). Top source: {primaryEvent.RemoteAddress}. Example: {primaryEvent.Method} {primaryEvent.Host}{primaryEvent.Path} -> {primaryEvent.Category} ({primaryEvent.RiskLevel}). Distinct sources: {distinctSources}."
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, options.Value.TelemetryAlertWebhookUrl)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                UpdateStatus($"Webhook error: HTTP {(int)response.StatusCode}.");
                logger.LogWarning("Telemetry alert webhook returned status code {StatusCode}.", response.StatusCode);
                return false;
            }

            lock (_sync)
            {
                _lastAlertSentUtc = now;
                _nextAlertUtc = now.AddMinutes(Math.Max(1, options.Value.TelemetryAlertCooldownMinutes));
                _lastStatus = $"Last alert sent {now.LocalDateTime:t}.";
            }
            logger.LogInformation("Sent telemetry alert for {Count} high-risk events.", highRiskEvents.Count);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            UpdateStatus($"Webhook error: {exception.Message}");
            logger.LogError(exception, "Failed to send telemetry alert webhook.");
            return false;
        }
    }

    private void UpdateStatus(string status)
    {
        lock (_sync)
        {
            _lastStatus = status;
        }
    }
}

public sealed record TelemetryAlertSnapshot(
    bool IsConfigured,
    string MinimumRiskLevel,
    DateTimeOffset? LastAlertSentUtc,
    DateTimeOffset? CooldownUntilUtc,
    string Status);