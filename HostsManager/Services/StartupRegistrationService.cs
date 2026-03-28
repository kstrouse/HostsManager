using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace HostsManager.Services;

public class StartupRegistrationService
{
    private const string TaskName = "Hosts Manager Startup";

    public bool IsSupported =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
        {
            return false;
        }

        var result = await RunSchtasksAsync($"/Query /TN {QuoteArg(TaskName)}", cancellationToken);
        return result.ExitCode == 0;
    }

    public async Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
        {
            return;
        }

        if (enabled)
        {
            var executablePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new InvalidOperationException("Unable to determine the current executable path.");
            }

            var taskAction = $"{QuoteForTaskAction(executablePath)} --background";
            var createArguments =
                $"/Create /F /TN {QuoteArg(TaskName)} /SC ONLOGON /RL HIGHEST /IT /TR \"{taskAction}\"";

            var createResult = await RunSchtasksAsync(createArguments, cancellationToken);
            if (createResult.ExitCode != 0)
            {
                throw new InvalidOperationException(GetSchtasksError(createResult));
            }

            return;
        }

        var deleteResult = await RunSchtasksAsync($"/Delete /F /TN {QuoteArg(TaskName)}", cancellationToken);
        if (deleteResult.ExitCode != 0 &&
            deleteResult.StandardError.IndexOf("cannot find the file specified", StringComparison.OrdinalIgnoreCase) < 0 &&
            deleteResult.StandardOutput.IndexOf("cannot find the file specified", StringComparison.OrdinalIgnoreCase) < 0)
        {
            throw new InvalidOperationException(GetSchtasksError(deleteResult));
        }
    }

    private static async Task<ProcessResult> RunSchtasksAsync(string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new ProcessResult(
            process.ExitCode,
            await outputTask,
            await errorTask);
    }

    private static string GetSchtasksError(ProcessResult result)
    {
        var message = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;

        return string.IsNullOrWhiteSpace(message)
            ? "Task Scheduler command failed."
            : message.Trim();
    }

    private static string QuoteArg(string value)
    {
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string QuoteForTaskAction(string value)
    {
        return $"\\\"{value.Replace("\"", "\\\"")}\\\"";
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
