using System.Diagnostics;
using Helgrind.Contracts;
using Helgrind.Options;
using Microsoft.Extensions.Options;

namespace Helgrind.Services;

public sealed class SelfUpdateService(
    IOptions<HelgrindOptions> options,
    IWebHostEnvironment environment,
    IHostApplicationLifetime applicationLifetime,
    ILogger<SelfUpdateService> logger)
{
    public string ButtonLabel => "Update Helgrind";

    public bool IsConfigured => ResolveCommand() is not null;

    public string GetStatusMessage()
    {
        var command = ResolveCommand();
        return command is null
            ? environment.IsDevelopment()
                ? "Self-update is disabled in Development. Stop the app from Visual Studio or your local runner and redeploy manually."
                : "Self-update is disabled. Helgrind could not find a production update command or the standard Linux update script."
            : $"Runs {command.Description}.";
    }

    public Task<SelfUpdateResultDto> TriggerUpdateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var command = ResolveCommand();
        if (command is null)
        {
            return Task.FromResult(new SelfUpdateResultDto
            {
                Success = false,
                Accepted = false,
                StatusMessage = "Self-update is not configured for this Helgrind instance."
            });
        }

        try
        {
            var process = Process.Start(command.StartInfo);
            if (process is null)
            {
                return Task.FromResult(new SelfUpdateResultDto
                {
                    Success = false,
                    Accepted = false,
                    StatusMessage = "Failed to launch the Helgrind update command."
                });
            }

            logger.LogInformation("Started Helgrind self-update command using {Description}.", command.Description);
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1.5));
                applicationLifetime.StopApplication();
            });

            return Task.FromResult(new SelfUpdateResultDto
            {
                Success = true,
                Accepted = true,
                StatusMessage = "Helgrind started the update command. The admin UI will disconnect while the process restarts."
            });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to launch Helgrind self-update command.");
            return Task.FromResult(new SelfUpdateResultDto
            {
                Success = false,
                Accepted = false,
                StatusMessage = $"Could not launch the update command: {exception.Message}"
            });
        }
    }

    private ResolvedUpdateCommand? ResolveCommand()
    {
        if (environment.IsDevelopment())
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(options.Value.SelfUpdateCommand))
        {
            return new ResolvedUpdateCommand(
                CreateShellStartInfo(options.Value.SelfUpdateCommand!, ResolveWorkingDirectory()),
                "configured production self-update command");
        }

        var defaultScriptPath = ResolveDefaultProductionScriptPath();
        return defaultScriptPath is null
            ? null
            : new ResolvedUpdateCommand(
                CreateShellStartInfo($"sudo /bin/bash {QuoteForShell(defaultScriptPath)}", ResolveWorkingDirectory()),
                "default Ubuntu update script");
    }

    private string ResolveWorkingDirectory()
    {
        var configuredPath = options.Value.SelfUpdateWorkingDirectory;
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configuredPath));
        }

        return ResolveRepositoryRoot();
    }

    private string ResolveRepositoryRoot()
    {
        var parent = Directory.GetParent(environment.ContentRootPath);
        if (parent is not null && File.Exists(Path.Combine(parent.FullName, "Helgrind.slnx")))
        {
            return parent.FullName;
        }

        return environment.ContentRootPath;
    }

    private string? ResolveDefaultProductionScriptPath()
    {
        var candidates = new List<string>();

        var configuredWorkingDirectory = options.Value.SelfUpdateWorkingDirectory;
        if (!string.IsNullOrWhiteSpace(configuredWorkingDirectory))
        {
            candidates.Add(Path.IsPathRooted(configuredWorkingDirectory)
                ? configuredWorkingDirectory
                : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configuredWorkingDirectory)));
        }

        var sourceDirectoryFromEnvironment = Environment.GetEnvironmentVariable("HELGRIND_SOURCE_DIR");
        if (!string.IsNullOrWhiteSpace(sourceDirectoryFromEnvironment))
        {
            candidates.Add(sourceDirectoryFromEnvironment);
        }

        candidates.Add("/opt/helgrind-src");
        candidates.Add(ResolveRepositoryRoot());

        foreach (var directory in candidates
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var candidate = Path.Combine(directory, "deploy", "linux", "update.sh");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private ProcessStartInfo CreateShellStartInfo(string command, string workingDirectory)
    {
        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = $"/c {command}",
            }
            : new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-lc \"{command.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"",
            };

        startInfo.WorkingDirectory = workingDirectory;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        ApplyEnvironment(startInfo);
        return startInfo;
    }

    private static string QuoteForShell(string value)
    {
        return OperatingSystem.IsWindows()
            ? value
            : $"'{value.Replace("'", "'\\''")}'";
    }

    private void ApplyEnvironment(ProcessStartInfo startInfo)
    {
        startInfo.Environment["HELGRIND_PID"] = Environment.ProcessId.ToString();
        startInfo.Environment["HELGRIND_CONTENT_ROOT"] = environment.ContentRootPath;
        startInfo.Environment["HELGRIND_REPO_ROOT"] = ResolveRepositoryRoot();
    }

    private sealed record ResolvedUpdateCommand(ProcessStartInfo StartInfo, string Description);
}