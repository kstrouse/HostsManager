using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace HostsManager.Services;

public class DesktopNotificationService
{
    public void ShowPendingApplyNotification()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            ShowWindowsNotification();
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            ShowMacNotification();
        }
    }

    private static void ShowWindowsNotification()
    {
        var executablePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        var escapedExecutablePath = executablePath.Replace("'", "''");
        var script = $@"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$notifyIcon = New-Object System.Windows.Forms.NotifyIcon
$notifyIcon.Icon = [System.Drawing.Icon]::ExtractAssociatedIcon('{escapedExecutablePath}')
$notifyIcon.Visible = $true
$notifyIcon.BalloonTipTitle = 'Hosts Manager'
$notifyIcon.BalloonTipText = 'Hosts changes are pending. Click Apply in Hosts Manager to approve and update the hosts file.'
$notifyIcon.ShowBalloonTip(5000)
Start-Sleep -Seconds 6
$notifyIcon.Dispose()
";

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-WindowStyle");
        startInfo.ArgumentList.Add("Hidden");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));

        TryStart(startInfo);
    }

    private static void ShowMacNotification()
    {
        var script = "display notification \"Hosts changes are pending. Open Hosts Manager and click Apply to approve the update.\" with title \"Hosts Manager\"";
        var startInfo = new ProcessStartInfo
        {
            FileName = "/usr/bin/osascript",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add(script);

        TryStart(startInfo);
    }

    private static void TryStart(ProcessStartInfo startInfo)
    {
        try
        {
            Process.Start(startInfo);
        }
        catch
        {
        }
    }
}
