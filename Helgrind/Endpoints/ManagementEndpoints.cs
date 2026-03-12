using Helgrind.Contracts;
using Helgrind.Services;

namespace Helgrind.Endpoints;

public static class ManagementEndpoints
{
    public static IEndpointRouteBuilder MapHelgrindManagementApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin");

        group.MapGet("/configuration", async (ConfigurationService configurationService, CancellationToken cancellationToken) =>
            Results.Ok(await configurationService.GetConfigurationAsync(cancellationToken)));

        group.MapGet("/status", async (ConfigurationService configurationService, CancellationToken cancellationToken) =>
            Results.Ok(await configurationService.GetAdminHealthStatusAsync(cancellationToken)));

        group.MapPut("/configuration", async (HelgrindConfigurationDto configuration, bool? allowEmpty, ConfigurationService configurationService, CancellationToken cancellationToken) =>
        {
            try
            {
                await configurationService.SaveConfigurationAsync(configuration, cancellationToken, allowEmpty ?? false);
                return Results.Ok(await configurationService.GetConfigurationAsync(cancellationToken));
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });

        group.MapPost("/apply", async (ConfigurationService configurationService, CancellationToken cancellationToken) =>
            Results.Ok(await configurationService.ApplyAsync(cancellationToken)));

        group.MapGet("/export", async (ConfigurationService configurationService, CancellationToken cancellationToken) =>
            Results.Ok(await configurationService.ExportAsync(cancellationToken)));

        group.MapPost("/import", async (HelgrindExportPackageDto package, ConfigurationService configurationService, CancellationToken cancellationToken) =>
            Results.Ok(await configurationService.ImportAsync(package, cancellationToken)));

        group.MapPost("/certificate", async (HttpRequest request, CertificateService certificateService, CancellationToken cancellationToken) =>
        {
            var form = await request.ReadFormAsync(cancellationToken);
            var pemFile = form.Files.GetFile("pemFile");
            var keyFile = form.Files.GetFile("keyFile");
            var displayName = form["displayName"].ToString();

            if (pemFile is null || keyFile is null)
            {
                return Results.BadRequest(new CertificateUploadResultDto
                {
                    Success = false,
                    UsingFallbackCertificate = certificateService.UsingFallbackCertificate,
                    StatusMessage = "Upload both a PEM file and a key file."
                });
            }

            return Results.Ok(await certificateService.UploadAndActivateAsync(pemFile, keyFile, displayName, cancellationToken));
        }).DisableAntiforgery();

        group.MapPost("/update", async (ISelfUpdateService selfUpdateService, CancellationToken cancellationToken) =>
            Results.Ok(await selfUpdateService.TriggerUpdateAsync(cancellationToken)));

        group.MapGet("/telemetry/summary", async (TelemetryQueryService telemetryQueryService, int? hours, CancellationToken cancellationToken) =>
            Results.Ok(await telemetryQueryService.GetSummaryAsync(hours ?? 24, cancellationToken)));

        group.MapGet("/telemetry/events", async (TelemetryQueryService telemetryQueryService, int? hours, int? page, int? pageSize, string? riskLevel, string? category, CancellationToken cancellationToken) =>
            Results.Ok(await telemetryQueryService.GetEventsAsync(hours ?? 24, page ?? 1, pageSize ?? 25, riskLevel, category, cancellationToken)));

        group.MapGet("/telemetry/top-sources", async (TelemetryQueryService telemetryQueryService, int? hours, int? limit, CancellationToken cancellationToken) =>
            Results.Ok(await telemetryQueryService.GetTopSourcesAsync(hours ?? 24, limit ?? 10, cancellationToken)));

        group.MapGet("/telemetry/top-targets", async (TelemetryQueryService telemetryQueryService, int? hours, int? limit, CancellationToken cancellationToken) =>
            Results.Ok(await telemetryQueryService.GetTopTargetsAsync(hours ?? 24, limit ?? 10, cancellationToken)));

        group.MapGet("/telemetry/trends", async (TelemetryQueryService telemetryQueryService, int? hours, int? bucketMinutes, CancellationToken cancellationToken) =>
            Results.Ok(await telemetryQueryService.GetTrendAsync(hours ?? 24, bucketMinutes ?? 60, cancellationToken)));

        return endpoints;
    }
}