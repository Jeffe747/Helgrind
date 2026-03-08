using Helgrind.Options;
using Helgrind.Services;
using Microsoft.AspNetCore.Http;

namespace Helgrind.Tests;

public sealed class PublicTelemetrySmokeMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ReturnsNotFound_ForConfiguredSmokePath()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/__helgrind/telemetry/smoke";
        context.Response.Body = new MemoryStream();
        var nextCalled = false;
        var middleware = new PublicTelemetrySmokeMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(
            context,
            Microsoft.Extensions.Options.Options.Create(new HelgrindOptions()));

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }
}