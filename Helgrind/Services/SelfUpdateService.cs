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
                : "Self-update is disabled. Configure Helgrind:SelfUpdateCommand for your production deployment flow."
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

        return string.IsNullOrWhiteSpace(options.Value.SelfUpdateCommand)
            ? null
            : new ResolvedUpdateCommand(
                CreateShellStartInfo(options.Value.SelfUpdateCommand!, ResolveWorkingDirectory()),
                "configured production self-update command");
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

    private void ApplyEnvironment(ProcessStartInfo startInfo)
    {
        startInfo.Environment["HELGRIND_PID"] = Environment.ProcessId.ToString();
        startInfo.Environment["HELGRIND_CONTENT_ROOT"] = environment.ContentRootPath;
        startInfo.Environment["HELGRIND_REPO_ROOT"] = ResolveRepositoryRoot();
    }

    private sealed record ResolvedUpdateCommand(ProcessStartInfo StartInfo, string Description);
}