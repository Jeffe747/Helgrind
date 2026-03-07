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
}