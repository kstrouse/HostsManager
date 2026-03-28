using System;
using System.Diagnostics;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace HostsManager.Services;

public class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "HostsManager";
    private const string LegacyTaskName = "Hosts Manager Startup";

    public bool IsSupported =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return await IsEnabledWindowsAsync();
    }

    public async Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await SetEnabledWindowsAsync(enabled, cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private static Task<bool> IsEnabledWindowsAsync()
    {
        using var key = OpenStartupKey(writable: false);
        var value = key?.GetValue(RunValueName) as string;
        return Task.FromResult(!string.IsNullOrWhiteSpace(value));
    }

    [SupportedOSPlatform("windows")]
    private static async Task SetEnabledWindowsAsync(bool enabled, CancellationToken cancellationToken)
    {
        if (enabled)
        {
            var executablePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new InvalidOperationException("Unable to determine the current executable path.");
            }

            using var key = OpenStartupKey(writable: true) ??
                            throw new InvalidOperationException("Unable to open the startup registry key for the logged-in user.");
            key.SetValue(RunValueName, $"{QuoteCommandValue(executablePath)} --background", RegistryValueKind.String);
            await TryDeleteLegacyTaskAsync(cancellationToken);
            return;
        }

        using (var key = OpenStartupKey(writable: true))
        {
            key?.DeleteValue(RunValueName, throwOnMissingValue: false);
        }

        await TryDeleteLegacyTaskAsync(cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private static RegistryKey? OpenStartupKey(bool writable)
    {
        var interactiveUserSid = TryGetInteractiveUserSid();
        if (string.IsNullOrWhiteSpace(interactiveUserSid))
        {
            return writable
                ? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                : Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        }

        var fullPath = $"{interactiveUserSid}\\{RunKeyPath}";
        return writable
            ? Registry.Users.CreateSubKey(fullPath, writable: true)
            : Registry.Users.OpenSubKey(fullPath, writable: false);
    }

    [SupportedOSPlatform("windows")]
    private static string? TryGetInteractiveUserSid()
    {
        try
        {
            var currentIdentity = WindowsIdentity.GetCurrent();
            var currentSid = currentIdentity.User?.Value;
            var currentSessionId = Process.GetCurrentProcess().SessionId;

            if (!TryGetSessionUserAccount(currentSessionId, out var accountName))
            {
                return currentSid;
            }

            var interactiveSid = ((SecurityIdentifier)new NTAccount(accountName).Translate(typeof(SecurityIdentifier))).Value;
            return string.IsNullOrWhiteSpace(interactiveSid)
                ? currentSid
                : interactiveSid;
        }
        catch
        {
            try
            {
                return WindowsIdentity.GetCurrent().User?.Value;
            }
            catch
            {
                return null;
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool TryGetSessionUserAccount(int sessionId, out string accountName)
    {
        accountName = string.Empty;

        if (!TryQuerySessionString(sessionId, WtsInfoClass.WTSUserName, out var userName) ||
            string.IsNullOrWhiteSpace(userName))
        {
            return false;
        }

        TryQuerySessionString(sessionId, WtsInfoClass.WTSDomainName, out var domainName);
        accountName = string.IsNullOrWhiteSpace(domainName)
            ? userName
            : $"{domainName}\\{userName}";

        return true;
    }

    [SupportedOSPlatform("windows")]
    private static bool TryQuerySessionString(int sessionId, WtsInfoClass infoClass, out string value)
    {
        value = string.Empty;
        if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, infoClass, out var buffer, out var bytesReturned) ||
            buffer == IntPtr.Zero ||
            bytesReturned <= 1)
        {
            return false;
        }

        try
        {
            value = Marshal.PtrToStringUni(buffer)?.TrimEnd('\0') ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    private static async Task TryDeleteLegacyTaskAsync(CancellationToken cancellationToken)
    {
        var deleteResult = await RunSchtasksAsync($"/Delete /F /TN {QuoteArg(LegacyTaskName)}", cancellationToken);
        if (deleteResult.ExitCode == 0)
        {
            return;
        }

        if (deleteResult.StandardError.IndexOf("cannot find the file specified", StringComparison.OrdinalIgnoreCase) >= 0 ||
            deleteResult.StandardOutput.IndexOf("cannot find the file specified", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return;
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

    private static string QuoteArg(string value)
    {
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string QuoteCommandValue(string value)
    {
        return $"\"{value.Replace("\"", "\\\"")}\"";
    }

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool WTSQuerySessionInformation(
        IntPtr hServer,
        int sessionId,
        WtsInfoClass wtsInfoClass,
        out IntPtr ppBuffer,
        out int pBytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);

    private enum WtsInfoClass
    {
        WTSUserName = 5,
        WTSDomainName = 7
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
