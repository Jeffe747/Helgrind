namespace Helgrind.Options;

public sealed class HelgrindOptions
{
    public const string SectionName = "Helgrind";

    public int HttpsPort
    {
        get => PublicHttpsPort;
        set => PublicHttpsPort = value;
    }

    public int PublicHttpsPort { get; set; } = 443;

    public int AdminHttpsPort { get; set; } = 8444;

    public List<string> AllowedAdminNetworks { get; set; } =
    [
        "127.0.0.0/8",
        "::1/128",
        "10.0.0.0/8",
        "172.16.0.0/12",
        "192.168.0.0/16",
        "fc00::/7",
        "fe80::/10"
    ];

    public string DatabasePath { get; set; } = Path.Combine("App_Data", "helgrind.db");

    public string CertificateStoragePath { get; set; } = Path.Combine("App_Data", "certificates");

    public string SelfUpdateRepoUrl { get; set; } = "https://github.com/Jeffe747/Helgrind.git";

    public string SelfUpdateBranch { get; set; } = "master";

    public string SelfUpdateLogPath { get; set; } = "/var/log/helgrind-self-update.log";

    public bool TelemetryEnabled { get; set; } = true;

    public int TelemetryRetentionDays { get; set; } = 30;

    public int TelemetryBurstWindowSeconds { get; set; } = 30;

    public int TelemetryBurstThreshold { get; set; } = 20;

    public int TelemetryMaxEventPageSize { get; set; } = 100;

    public string TelemetrySmokePath { get; set; } = "/__helgrind/telemetry/smoke";

    public string? TelemetryAlertWebhookUrl { get; set; }

    public int TelemetryAlertMinimumRiskScore { get; set; } = 3;

    public int TelemetryAlertCooldownMinutes { get; set; } = 10;
}