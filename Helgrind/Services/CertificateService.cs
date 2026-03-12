using System.Security.Cryptography.X509Certificates;
using Helgrind.Contracts;
using Helgrind.Data;
using Helgrind.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Helgrind.Services;

public sealed class CertificateService(
    HelgrindDbContext dbContext,
    CertificateRuntimeState runtimeState,
    IWebHostEnvironment environment,
    IOptions<HelgrindOptions> options) 
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(GetCertificateRoot());

        var activeCertificate = await dbContext.Certificates
            .SingleOrDefaultAsync(certificate => certificate.IsActive, cancellationToken);

        if (activeCertificate is null)
        {
            runtimeState.ClearActiveCertificate();
            return;
        }

        if (!CertificatePathResolver.TryResolveStoredCertificatePaths(
            environment.ContentRootPath,
            options.Value,
            activeCertificate.Id,
            activeCertificate.PemFilePath,
            activeCertificate.KeyFilePath,
            out var pemPath,
            out var keyPath))
        {
            runtimeState.ClearActiveCertificate();
            return;
        }

        if (!string.Equals(activeCertificate.PemFilePath, pemPath, StringComparison.Ordinal)
            || !string.Equals(activeCertificate.KeyFilePath, keyPath, StringComparison.Ordinal))
        {
            activeCertificate.PemFilePath = pemPath;
            activeCertificate.KeyFilePath = keyPath;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        using var certificate = X509Certificate2.CreateFromPemFile(pemPath, keyPath);
        runtimeState.SetActiveCertificate(certificate);
    }

    public bool UsingFallbackCertificate => runtimeState.UsingFallbackCertificate;

    public bool RequiresRestart(CertificateMetadataDto? storedCertificate)
    {
        if (storedCertificate is null)
        {
            return false;
        }

        return !string.Equals(runtimeState.CurrentThumbprint, storedCertificate.Thumbprint, StringComparison.OrdinalIgnoreCase);
    }

    public string GetCertificateStatus(CertificateMetadataDto? storedCertificate) => storedCertificate switch
    {
        null => "Using the built-in temporary certificate until a PEM and key are uploaded.",
        _ when runtimeState.UsingFallbackCertificate => "A certificate is stored, but Helgrind must be restarted before it will be served for HTTPS.",
        _ when !string.Equals(runtimeState.CurrentThumbprint, storedCertificate.Thumbprint, StringComparison.OrdinalIgnoreCase) => "A newer certificate is stored. Restart Helgrind to switch the public HTTPS certificate.",
        _ => "Using the uploaded PEM certificate and key for HTTPS.",
    };

    public async Task<CertificateMetadataDto?> GetActiveCertificateMetadataAsync(CancellationToken cancellationToken)
    {
        var entity = await dbContext.Certificates
            .AsNoTracking()
            .SingleOrDefaultAsync(certificate => certificate.IsActive, cancellationToken);

        return entity is null ? null : ToMetadata(entity);
    }

    public async Task<CertificateUploadResultDto> UploadAndActivateAsync(
        IFormFile pemFile,
        IFormFile keyFile,
        string? displayName,
        CancellationToken cancellationToken)
    {
        if (pemFile.Length == 0 || keyFile.Length == 0)
        {
            return new CertificateUploadResultDto
            {
                Success = false,
                UsingFallbackCertificate = runtimeState.UsingFallbackCertificate,
                StatusMessage = "Both PEM and key files are required."
            };
        }

        var certificateId = Guid.NewGuid();
        var certificateDirectory = CertificatePathResolver.GetCertificateDirectory(environment.ContentRootPath, options.Value, certificateId);
        Directory.CreateDirectory(certificateDirectory);

        var pemPath = Path.Combine(certificateDirectory, "certificate.pem");
        var keyPath = Path.Combine(certificateDirectory, "certificate.key");

        await using (var pemStream = File.Create(pemPath))
        {
            await pemFile.CopyToAsync(pemStream, cancellationToken);
        }

        await using (var keyStream = File.Create(keyPath))
        {
            await keyFile.CopyToAsync(keyStream, cancellationToken);
        }

        X509Certificate2 certificate;
        try
        {
            using var loadedCertificate = X509Certificate2.CreateFromPemFile(pemPath, keyPath);
            certificate = CertificateRuntimeState.CloneForServerUsage(loadedCertificate);
        }
        catch
        {
            Directory.Delete(certificateDirectory, true);
            return new CertificateUploadResultDto
            {
                Success = false,
                UsingFallbackCertificate = runtimeState.UsingFallbackCertificate,
                StatusMessage = "The uploaded PEM and key could not be loaded as an X509 certificate."
            };
        }

        foreach (var storedCertificate in await dbContext.Certificates.ToListAsync(cancellationToken))
        {
            storedCertificate.IsActive = false;
        }

        var entity = new StoredCertificateEntity
        {
            Id = certificateId,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? pemFile.FileName : displayName.Trim(),
            Thumbprint = certificate.Thumbprint,
            PemFilePath = pemPath,
            KeyFilePath = keyPath,
            OriginalPemFileName = pemFile.FileName,
            OriginalKeyFileName = keyFile.FileName,
            UploadedUtc = DateTimeOffset.UtcNow,
            IsActive = true,
        };

        dbContext.Certificates.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CertificateUploadResultDto
        {
            Success = true,
            UsingFallbackCertificate = runtimeState.UsingFallbackCertificate,
            StatusMessage = "The certificate was stored successfully. Restart Helgrind to serve the new HTTPS certificate.",
            ActiveCertificate = ToMetadata(entity)
        };
    }

    private string GetCertificateRoot() => CertificatePathResolver.GetCertificateRoot(environment.ContentRootPath, options.Value);

    private static CertificateMetadataDto ToMetadata(StoredCertificateEntity entity) => new()
    {
        Id = entity.Id,
        DisplayName = entity.DisplayName,
        Thumbprint = entity.Thumbprint,
        UploadedUtc = entity.UploadedUtc,
    };
}