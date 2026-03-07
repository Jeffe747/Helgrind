using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Net;

namespace Helgrind.Services;

public sealed class CertificateRuntimeState
{
    private readonly X509Certificate2 _fallbackCertificate = CreateFallbackCertificate();
    private readonly string _fallbackThumbprint;
    private volatile X509Certificate2? _activeCertificate;

    public CertificateRuntimeState()
    {
        _fallbackThumbprint = _fallbackCertificate.Thumbprint;
    }

    public X509Certificate2 GetServerCertificate() => _activeCertificate ?? _fallbackCertificate;

    public string CurrentThumbprint => GetServerCertificate().Thumbprint;

    public string FallbackThumbprint => _fallbackThumbprint;

    public bool UsingFallbackCertificate => _activeCertificate is null;

    public void SetActiveCertificate(X509Certificate2 certificate)
    {
        _activeCertificate = CloneForServerUsage(certificate);
    }

    public void ClearActiveCertificate()
    {
        _activeCertificate = null;
    }

    private static X509Certificate2 CreateFallbackCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Helgrind Temporary Certificate",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                [new Oid("1.3.6.1.5.5.7.3.1")],
                critical: true));
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var subjectAlternativeNameBuilder = new SubjectAlternativeNameBuilder();
        subjectAlternativeNameBuilder.AddDnsName("localhost");
        subjectAlternativeNameBuilder.AddIpAddress(IPAddress.Loopback);
        subjectAlternativeNameBuilder.AddIpAddress(IPAddress.IPv6Loopback);
        request.CertificateExtensions.Add(subjectAlternativeNameBuilder.Build());

        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return CloneForServerUsage(certificate);
    }

    public static X509Certificate2 CloneForServerUsage(X509Certificate2 certificate)
    {
        var pfxBytes = certificate.Export(X509ContentType.Pkcs12);
        return X509CertificateLoader.LoadPkcs12(
            pfxBytes,
            string.Empty,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet);
    }
}