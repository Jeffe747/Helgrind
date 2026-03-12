using Helgrind.Options;

namespace Helgrind.Services;

internal static class CertificatePathResolver
{
    internal static string GetCertificateRoot(string contentRootPath, HelgrindOptions options)
    {
        return Path.IsPathRooted(options.CertificateStoragePath)
            ? options.CertificateStoragePath
            : Path.Combine(contentRootPath, options.CertificateStoragePath);
    }

    internal static string GetCertificateDirectory(string contentRootPath, HelgrindOptions options, Guid certificateId)
        => Path.Combine(GetCertificateRoot(contentRootPath, options), certificateId.ToString("N"));

    internal static string GetPemPath(string contentRootPath, HelgrindOptions options, Guid certificateId)
        => Path.Combine(GetCertificateDirectory(contentRootPath, options, certificateId), "certificate.pem");

    internal static string GetKeyPath(string contentRootPath, HelgrindOptions options, Guid certificateId)
        => Path.Combine(GetCertificateDirectory(contentRootPath, options, certificateId), "certificate.key");

    internal static bool TryResolveStoredCertificatePaths(
        string contentRootPath,
        HelgrindOptions options,
        Guid certificateId,
        string? storedPemPath,
        string? storedKeyPath,
        out string pemPath,
        out string keyPath)
    {
        foreach (var candidate in EnumerateCandidates(contentRootPath, options, certificateId, storedPemPath, storedKeyPath))
        {
            if (File.Exists(candidate.PemPath) && File.Exists(candidate.KeyPath))
            {
                pemPath = candidate.PemPath;
                keyPath = candidate.KeyPath;
                return true;
            }
        }

        pemPath = string.Empty;
        keyPath = string.Empty;
        return false;
    }

    private static IEnumerable<(string PemPath, string KeyPath)> EnumerateCandidates(
        string contentRootPath,
        HelgrindOptions options,
        Guid certificateId,
        string? storedPemPath,
        string? storedKeyPath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in new[]
        {
            CreateCandidate(storedPemPath, storedKeyPath),
            CreateCandidate(ResolveRelativePath(contentRootPath, storedPemPath), ResolveRelativePath(contentRootPath, storedKeyPath)),
            CreateCandidate(ResolveRelativePath(GetCertificateRoot(contentRootPath, options), storedPemPath), ResolveRelativePath(GetCertificateRoot(contentRootPath, options), storedKeyPath)),
            CreateCandidate(GetPemPath(contentRootPath, options, certificateId), GetKeyPath(contentRootPath, options, certificateId))
        })
        {
            if (string.IsNullOrWhiteSpace(candidate.PemPath) || string.IsNullOrWhiteSpace(candidate.KeyPath))
            {
                continue;
            }

            var key = $"{candidate.PemPath}|{candidate.KeyPath}";
            if (seen.Add(key))
            {
                yield return candidate;
            }
        }
    }

    private static (string PemPath, string KeyPath) CreateCandidate(string? pemPath, string? keyPath)
        => (pemPath ?? string.Empty, keyPath ?? string.Empty);

    private static string? ResolveRelativePath(string rootPath, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return Path.IsPathRooted(path)
            ? path
            : Path.Combine(rootPath, path);
    }
}