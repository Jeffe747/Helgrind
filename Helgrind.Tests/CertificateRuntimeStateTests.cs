using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Helgrind.Services;

namespace Helgrind.Tests;

public sealed class CertificateRuntimeStateTests
{
    [Fact]
    public void FallbackCertificate_HasPrivateKey_AndServerAuthenticationEku()
    {
        var runtimeState = new CertificateRuntimeState();

        var certificate = runtimeState.GetServerCertificate();
        var ekuExtension = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().Single();
        var ekuValues = ekuExtension.EnhancedKeyUsages.Cast<Oid>().Select(oid => oid.Value).ToList();

        Assert.True(certificate.HasPrivateKey);
        Assert.Contains("1.3.6.1.5.5.7.3.1", ekuValues);
    }

    [Fact]
    public void GetServerCertificate_UsesFallbackUntilActiveCertificateIsSet()
    {
        var runtimeState = new CertificateRuntimeState();

        var fallbackSubject = runtimeState.GetServerCertificate().Subject;

        runtimeState.SetActiveCertificate(CreateCertificate("CN=Uploaded"));

        Assert.Equal("CN=Uploaded", runtimeState.GetServerCertificate().Subject);
        runtimeState.ClearActiveCertificate();
        Assert.Equal(fallbackSubject, runtimeState.GetServerCertificate().Subject);
    }

    private static X509Certificate2 CreateCertificate(string subject)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }
}