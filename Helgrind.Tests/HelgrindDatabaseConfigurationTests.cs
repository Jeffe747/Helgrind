using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Helgrind.Options;
using Helgrind.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace Helgrind.Tests;

public sealed class HelgrindDatabaseConfigurationTests : IDisposable
{
    private readonly string _contentRootPath = Path.Combine(Path.GetTempPath(), $"helgrind-db-tests-{Guid.NewGuid():N}");

    public HelgrindDatabaseConfigurationTests()
    {
        Directory.CreateDirectory(_contentRootPath);
    }

    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("Postgres")]
    [InlineData("Npgsql")]
    public void ResolveProvider_UsesDatabaseSection(string configuredValue)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = configuredValue
            })
            .Build();

        var provider = HelgrindDatabaseConfiguration.ResolveProvider(configuration);

        Assert.Equal(HelgrindDatabaseProvider.PostgreSql, provider);
    }

    [Fact]
    public void ResolveProvider_IgnoresLegacyHelgrindDatabaseProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Helgrind:DatabaseProvider"] = "Postgres"
            })
            .Build();

        var provider = HelgrindDatabaseConfiguration.ResolveProvider(configuration);

        Assert.Equal(HelgrindDatabaseProvider.Sqlite, provider);
    }

    [Fact]
    public void ResolveConnectionString_UsesDefaultConnection_ForPostgreSql()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "PostgreSql",
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Port=5432;Database=helgrind;Username=helgrind;Password=secret"
            })
            .Build();

        var connectionString = HelgrindDatabaseConfiguration.ResolveConnectionString(
            configuration,
            _contentRootPath,
            new HelgrindOptions(),
            HelgrindDatabaseProvider.PostgreSql);

        Assert.Equal("Host=db;Port=5432;Database=helgrind;Username=helgrind;Password=secret", connectionString);
    }

    [Fact]
    public void TryLoadStartupCertificate_LoadsActiveCertificate_FromSqliteDatabase()
    {
        var options = new HelgrindOptions
        {
            DatabasePath = Path.Combine("App_Data", "helgrind.db")
        };
        var sqlitePath = HelgrindDatabaseConfiguration.ResolveSqliteDatabasePath(_contentRootPath, options);
        Directory.CreateDirectory(Path.GetDirectoryName(sqlitePath)!);

        var certificateDirectory = Path.Combine(_contentRootPath, "certificates");
        Directory.CreateDirectory(certificateDirectory);
        var pemPath = Path.Combine(certificateDirectory, "certificate.pem");
        var keyPath = Path.Combine(certificateDirectory, "certificate.key");
        var expectedThumbprint = WriteTestCertificateFiles(pemPath, keyPath);

        using (var connection = new SqliteConnection($"Data Source={sqlitePath}"))
        {
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE Certificates (
                    Id TEXT NOT NULL PRIMARY KEY,
                    DisplayName TEXT NOT NULL,
                    Thumbprint TEXT NOT NULL,
                    PemFilePath TEXT NOT NULL,
                    KeyFilePath TEXT NOT NULL,
                    OriginalPemFileName TEXT NOT NULL,
                    OriginalKeyFileName TEXT NOT NULL,
                    UploadedUtc TEXT NOT NULL,
                    IsActive INTEGER NOT NULL
                );
                INSERT INTO Certificates (Id, DisplayName, Thumbprint, PemFilePath, KeyFilePath, OriginalPemFileName, OriginalKeyFileName, UploadedUtc, IsActive)
                VALUES ('00000000-0000-0000-0000-000000000001', 'test-cert', @thumbprint, @pemFilePath, @keyFilePath, 'certificate.pem', 'certificate.key', '2026-03-12T00:00:00+00:00', 1);";
            command.Parameters.AddWithValue("@thumbprint", expectedThumbprint);
            command.Parameters.AddWithValue("@pemFilePath", pemPath);
            command.Parameters.AddWithValue("@keyFilePath", keyPath);
            command.ExecuteNonQuery();
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["Helgrind:DatabasePath"] = options.DatabasePath
            })
            .Build();

        var runtimeState = new CertificateRuntimeState();

        HelgrindDatabaseConfiguration.TryLoadStartupCertificate(configuration, _contentRootPath, options, runtimeState);

        Assert.False(runtimeState.UsingFallbackCertificate);
        Assert.Equal(expectedThumbprint, runtimeState.CurrentThumbprint);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_contentRootPath))
        {
            Directory.Delete(_contentRootPath, true);
        }
    }

    private static string WriteTestCertificateFiles(string pemPath, string keyPath)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Helgrind Test Certificate", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(7));

        File.WriteAllText(pemPath, certificate.ExportCertificatePem());
        File.WriteAllText(keyPath, rsa.ExportPkcs8PrivateKeyPem());
        return certificate.Thumbprint;
    }
}
