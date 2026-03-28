using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace HostsManager.Services;

public class WindowsElevationService
{
    public bool IsSupported =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public bool IsProcessElevated()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return IsProcessElevatedWindows();
    }

    public bool TryRelaunchElevated(StartupAction action, string? payloadPath = null, bool startInBackground = false)
    {
        if (!IsSupported)
        {
            return false;
        }

        var executablePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        var args = new List<string>();
        switch (action)
        {
            case StartupAction.ApplyManagedHosts:
                args.Add("--apply-managed-hosts");
                break;
            case StartupAction.RestoreBackup:
                args.Add("--restore-backup");
                break;
            case StartupAction.SaveRawHosts:
                args.Add("--save-raw-hosts");
                break;
        }

        if (startInBackground)
        {
            args.Add("--background");
        }

        if (!string.IsNullOrWhiteSpace(payloadPath))
        {
            args.Add("--payload-path");
            args.Add(payloadPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = string.Join(" ", args.ConvertAll(QuoteArg)),
            UseShellExecute = true,
            Verb = "runas"
        };

        try
        {
            Process.Start(startInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool IsProcessElevatedWindows()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string QuoteArg(string arg)
    {
        if (string.IsNullOrEmpty(arg))
        {
            return "\"\"";
        }

        return arg.Contains(' ') || arg.Contains('"')
            ? $"\"{arg.Replace("\"", "\\\"")}\""
            : arg;
    }
}
