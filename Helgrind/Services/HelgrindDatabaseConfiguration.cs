using System.Data.Common;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Helgrind.Options;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Helgrind.Services;

internal enum HelgrindDatabaseProvider
{
    Sqlite,
    PostgreSql
}

internal static class HelgrindDatabaseConfiguration
{
    internal static HelgrindDatabaseProvider ResolveProvider(IConfiguration configuration)
    {
        var configuredProvider = configuration["Database:Provider"];
        if (string.IsNullOrWhiteSpace(configuredProvider))
        {
            return HelgrindDatabaseProvider.Sqlite;
        }

        if (configuredProvider.Equals("postgres", StringComparison.OrdinalIgnoreCase)
            || configuredProvider.Equals("postgresql", StringComparison.OrdinalIgnoreCase)
            || configuredProvider.Equals("npgsql", StringComparison.OrdinalIgnoreCase))
        {
            return HelgrindDatabaseProvider.PostgreSql;
        }

        return HelgrindDatabaseProvider.Sqlite;
    }

    internal static string ResolveConnectionString(
        IConfiguration configuration,
        string contentRootPath,
        HelgrindOptions options,
        HelgrindDatabaseProvider provider)
    {
        if (provider == HelgrindDatabaseProvider.PostgreSql)
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured when Database:Provider is PostgreSql.");
            }

            return connectionString;
        }

        var databasePath = ResolveSqliteDatabasePath(contentRootPath, options);
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        return $"Data Source={databasePath}";
    }

    internal static string ResolveSqliteDatabasePath(string contentRootPath, HelgrindOptions options)
    {
        return Path.IsPathRooted(options.DatabasePath)
            ? options.DatabasePath
            : Path.Combine(contentRootPath, options.DatabasePath);
    }

    internal static void ConfigureDbContext(DbContextOptionsBuilder optionsBuilder, HelgrindDatabaseProvider provider, string connectionString)
    {
        switch (provider)
        {
            case HelgrindDatabaseProvider.PostgreSql:
                optionsBuilder.UseNpgsql(connectionString);
                break;

            default:
                optionsBuilder.UseSqlite(connectionString);
                break;
        }
    }

    internal static void TryLoadStartupCertificate(
        IConfiguration configuration,
        string contentRootPath,
        HelgrindOptions options,
        CertificateRuntimeState runtimeState)
    {
        var provider = ResolveProvider(configuration);

        try
        {
            using var connection = CreateConnection(configuration, contentRootPath, options, provider);
            if (connection is null)
            {
                return;
            }

            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = provider switch
            {
                HelgrindDatabaseProvider.PostgreSql => "SELECT \"PemFilePath\", \"KeyFilePath\" FROM \"Certificates\" WHERE \"IsActive\" = TRUE LIMIT 1",
                _ => "SELECT \"PemFilePath\", \"KeyFilePath\" FROM \"Certificates\" WHERE \"IsActive\" = 1 LIMIT 1"
            };

            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return;
            }

            var pemFilePath = reader.GetString(0);
            var keyFilePath = reader.GetString(1);
            if (!File.Exists(pemFilePath) || !File.Exists(keyFilePath))
            {
                return;
            }

            using var certificate = X509Certificate2.CreateFromPemFile(pemFilePath, keyFilePath);
            runtimeState.SetActiveCertificate(certificate);
        }
        catch (DbException)
        {
        }
        catch (CryptographicException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static DbConnection? CreateConnection(
        IConfiguration configuration,
        string contentRootPath,
        HelgrindOptions options,
        HelgrindDatabaseProvider provider)
    {
        return provider switch
        {
            HelgrindDatabaseProvider.PostgreSql => CreatePostgreSqlConnection(configuration),
            _ => CreateSqliteConnection(contentRootPath, options)
        };
    }

    private static DbConnection? CreatePostgreSqlConnection(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        return string.IsNullOrWhiteSpace(connectionString)
            ? null
            : new NpgsqlConnection(connectionString);
    }

    private static DbConnection? CreateSqliteConnection(string contentRootPath, HelgrindOptions options)
    {
        var databasePath = ResolveSqliteDatabasePath(contentRootPath, options);
        return File.Exists(databasePath)
            ? new SqliteConnection($"Data Source={databasePath}")
            : null;
    }
}