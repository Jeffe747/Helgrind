using Helgrind.Options;
using Microsoft.Extensions.Options;

namespace Helgrind.Services;

public sealed class PublicTelemetrySmokeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IOptions<HelgrindOptions> options)
    {
        if (TelemetryPathUtility.Matches(context.Request.Path.Value, options.Value.TelemetrySmokePath))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync("Helgrind telemetry smoke test request received on the public listener.");
            return;
        }

        await next(context);
    }
}