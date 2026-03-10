using System.Diagnostics;
using Helgrind.Contracts;
using Helgrind.Options;
using Microsoft.Extensions.Options;

namespace Helgrind.Services;

public interface ISelfUpdateService
{
    string ButtonLabel { get; }
    bool IsConfigured { get; }
    string GetStatusMessage();
    Task<SelfUpdateResultDto> TriggerUpdateAsync(CancellationToken cancellationToken);
}

public sealed class SelfUpdateService(
    IOptions<HelgrindOptions> options,
    IWebHostEnvironment environment,
    ILogger<SelfUpdateService> logger) : ISelfUpdateService
{
    public string ButtonLabel => "Update Helgrind";

    public bool IsConfigured => !environment.IsDevelopment() && File.Exists(ScriptPath);

    private string ScriptPath => Path.Combine(environment.ContentRootPath, "update.sh");

    public string GetStatusMessage()
    {
        if (environment.IsDevelopment())
        {
            return "Self-update is disabled in Development. Stop the app from Visual Studio or your local runner and redeploy manually.";
        }

        var scriptPath = ScriptPath;
        if (!File.Exists(scriptPath))
        {
            return $"Self-update is not available. Expected update.sh at {scriptPath}.";
        }

        return $"Runs {scriptPath} to pull from {options.Value.SelfUpdateBranch} and restart the Helgrind service.";
    }

    public async Task<SelfUpdateResultDto> TriggerUpdateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsConfigured)
        {
            return new SelfUpdateResultDto
            {
                Success = false,
                Accepted = false,
                StatusMessage = "Self-update is not configured for this Helgrind instance."
            };
        }

        var scriptPath = ScriptPath;
        var logPath = options.Value.SelfUpdateLogPath;
        var repoUrl = EscapeSingleQuoted(options.Value.SelfUpdateRepoUrl);
        var branch = EscapeSingleQuoted(options.Value.SelfUpdateBranch);
        var command = $"nohup '{EscapeSingleQuoted(scriptPath)}' --repo-url '{repoUrl}' --branch '{branch}' > '{EscapeSingleQuoted(logPath)}' 2>&1 &";

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-lc \"{command}\"",
                WorkingDirectory = "/",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        process.StartInfo.Environment["HELGRIND_PID"] = Environment.ProcessId.ToString();
        process.StartInfo.Environment["HELGRIND_CONTENT_ROOT"] = environment.ContentRootPath;

        process.Start();
        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var standardError = await stdErrTask;
        _ = await stdOutTask;

        if (process.ExitCode != 0)
        {
            var errorMessage = $"Failed to start the updater script: {standardError}".Trim();
            logger.LogError("Helgrind self-update failed to launch: {Error}", standardError);
            return new SelfUpdateResultDto
            {
                Success = false,
                Accepted = false,
                StatusMessage = errorMessage
            };
        }

        logger.LogInformation("Helgrind self-update handed off to {ScriptPath}. Log: {LogPath}", scriptPath, logPath);
        return new SelfUpdateResultDto
        {
            Success = true,
            Accepted = true,
            StatusMessage = "Self-update handoff completed. Helgrind will restart if the updater succeeds."
        };
    }

    private static string EscapeSingleQuoted(string value)
    {
        return value.Replace("'", "'\\''", StringComparison.Ordinal);
    }
}
