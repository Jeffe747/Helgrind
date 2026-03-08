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

        group.MapPut("/configuration", async (HelgrindConfigurationDto configuration, ConfigurationService configurationService, CancellationToken cancellationToken) =>
        {
            await configurationService.SaveConfigurationAsync(configuration, cancellationToken);
            return Results.Ok(await configurationService.GetConfigurationAsync(cancellationToken));
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

        group.MapPost("/update", async (SelfUpdateService selfUpdateService, CancellationToken cancellationToken) =>
            Results.Ok(await selfUpdateService.TriggerUpdateAsync(cancellationToken)));

        return endpoints;
    }
}