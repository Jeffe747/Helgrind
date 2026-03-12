using Helgrind.Data;
using Helgrind.Endpoints;
using Helgrind.Options;
using Helgrind.Services;
using Microsoft.EntityFrameworkCore;

var contentRootPath = ResolveContentRoot();
var webRootPath = Path.Combine(contentRootPath, "wwwroot");
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
	Args = args,
	ContentRootPath = contentRootPath,
	WebRootPath = Directory.Exists(webRootPath) ? webRootPath : null,
});
var configuredOptions = builder.Configuration.GetSection(HelgrindOptions.SectionName).Get<HelgrindOptions>() ?? new HelgrindOptions();
var databaseProvider = HelgrindDatabaseConfiguration.ResolveProvider(builder.Configuration);
var certificateRuntimeState = new CertificateRuntimeState();
HelgrindDatabaseConfiguration.TryLoadStartupCertificate(builder.Configuration, contentRootPath, configuredOptions, certificateRuntimeState);

if (configuredOptions.PublicHttpsPort == configuredOptions.AdminHttpsPort)
{
	throw new InvalidOperationException("Helgrind requires different PublicHttpsPort and AdminHttpsPort values when admin and proxy traffic are split.");
}

builder.Services.Configure<HelgrindOptions>(builder.Configuration.GetSection(HelgrindOptions.SectionName));
builder.Services.AddSingleton(certificateRuntimeState);
builder.Services.AddSingleton<AdminAccessService>();
builder.Services.AddSingleton<ISelfUpdateService, SelfUpdateService>();
builder.Services.AddSingleton<InMemoryProxyConfigProvider>();
builder.Services.AddSingleton<TelemetryRateTracker>();
builder.Services.AddSingleton<TelemetryEventSink>();
builder.Services.AddSingleton<TelemetryRouteMatcher>();
builder.Services.AddSingleton<TelemetryClassifierService>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<Yarp.ReverseProxy.Configuration.IProxyConfigProvider>(static serviceProvider => serviceProvider.GetRequiredService<InMemoryProxyConfigProvider>());
builder.Services.AddHttpClient<TelemetryAlertService>();
builder.Services.AddHostedService<TelemetryBackgroundService>();
builder.Services.AddScoped<ProxyConfigFactory>();
builder.Services.AddScoped<CertificateService>();
builder.Services.AddScoped<ConfigurationService>();
builder.Services.AddScoped<TelemetryQueryService>();
builder.Services.AddScoped<TelemetryRetentionService>();
var defaultConnectionString = HelgrindDatabaseConfiguration.ResolveConnectionString(
	builder.Configuration,
	contentRootPath,
	configuredOptions,
	databaseProvider);
builder.Services.AddDbContext<HelgrindDbContext>((serviceProvider, options) =>
{
	HelgrindDatabaseConfiguration.ConfigureDbContext(options, databaseProvider, defaultConnectionString);
});
builder.Services
	.AddReverseProxy()
	.LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
	.LoadFromMemory([], []);

builder.WebHost.ConfigureKestrel((context, kestrel) =>
{
	var publicPort = context.Configuration.GetValue<int?>("Helgrind:PublicHttpsPort")
		?? context.Configuration.GetValue<int?>("Helgrind:HttpsPort")
		?? 443;
	var adminPort = context.Configuration.GetValue<int?>("Helgrind:AdminHttpsPort") ?? 8444;

	kestrel.ListenAnyIP(publicPort, listenOptions =>
	{
		listenOptions.UseHttps(certificateRuntimeState.GetServerCertificate());
	});

	kestrel.ListenAnyIP(adminPort, listenOptions =>
	{
		listenOptions.UseHttps(certificateRuntimeState.GetServerCertificate());
	});
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
	var configurationService = scope.ServiceProvider.GetRequiredService<ConfigurationService>();
	await configurationService.InitializeAsync(app.Lifetime.ApplicationStopping);
}

var helgrindOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<HelgrindOptions>>().Value;

app.MapWhen(context => context.Connection.LocalPort == helgrindOptions.AdminHttpsPort, adminApp =>
{
	adminApp.UseExceptionHandler(errorApp =>
	{
		errorApp.Run(async context =>
		{
			var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
			var exception = feature?.Error;
			context.Response.ContentType = "application/json";
			context.Response.StatusCode = exception is InvalidOperationException or ArgumentException
				? StatusCodes.Status400BadRequest
				: StatusCodes.Status500InternalServerError;
			await context.Response.WriteAsJsonAsync(new
			{
				Error = exception?.Message ?? "An unexpected server error occurred."
			});
		});
	});

	adminApp.Use(async (context, next) =>
	{
		var adminAccessService = context.RequestServices.GetRequiredService<AdminAccessService>();
		if (!adminAccessService.IsAllowed(context.Connection.RemoteIpAddress))
		{
			context.Response.StatusCode = StatusCodes.Status403Forbidden;
			await context.Response.WriteAsync("The Helgrind management UI is restricted to configured LAN networks.");
			return;
		}

		await next();
	});

	adminApp.UseDefaultFiles();
	adminApp.UseStaticFiles();
	adminApp.UseRouting();
	adminApp.UseEndpoints(endpoints =>
	{
		endpoints.MapHelgrindManagementApi();
		endpoints.MapFallbackToFile("index.html");
	});

	adminApp.Run(async context =>
	{
		context.Response.StatusCode = StatusCodes.Status404NotFound;
		await context.Response.WriteAsync("Helgrind admin route not found.");
	});
});

app.MapWhen(context => context.Connection.LocalPort == helgrindOptions.PublicHttpsPort, publicApp =>
{
	publicApp.UseRouting();
	publicApp.UseMiddleware<PublicTelemetryMiddleware>();
	publicApp.UseMiddleware<PublicTelemetrySmokeMiddleware>();
	publicApp.UseEndpoints(endpoints =>
	{
		endpoints.MapReverseProxy();
	});

	publicApp.Run(async context =>
	{
		context.Response.StatusCode = StatusCodes.Status404NotFound;
		await context.Response.WriteAsync("No public proxy route matched this request.");
	});
});

app.Run(async context =>
{
	context.Response.StatusCode = StatusCodes.Status404NotFound;
	await context.Response.WriteAsync("Helgrind listener not configured for this request.");
});

app.Run();

static string ResolveContentRoot()
{
	var candidates = new[]
	{
		Directory.GetCurrentDirectory(),
		AppContext.BaseDirectory,
	};

	foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
	{
		var resolved = FindContentRoot(candidate);
		if (resolved is not null)
		{
			return resolved;
		}
	}

	return Directory.GetCurrentDirectory();
}

static string? FindContentRoot(string startPath)
{
	var directory = new DirectoryInfo(startPath);
	while (directory is not null)
	{
		var hasProjectRootShape = Directory.Exists(Path.Combine(directory.FullName, "wwwroot"))
			&& File.Exists(Path.Combine(directory.FullName, "Helgrind.csproj"));
		var hasPublishedOutputShape = Directory.Exists(Path.Combine(directory.FullName, "wwwroot"))
			&& File.Exists(Path.Combine(directory.FullName, "appsettings.json"));

		if (hasProjectRootShape || hasPublishedOutputShape)
		{
			return directory.FullName;
		}

		directory = directory.Parent;
	}

	return null;
}
