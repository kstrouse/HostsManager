using Avalonia;
using System;
using System.Linq;

namespace HostsManager.Desktop;

public enum StartupAction
{
    None,
    ApplyManagedHosts,
    RestoreBackup,
    SaveRawHosts
}

sealed class Program
{
    public static bool StartInBackground { get; private set; }
    public static StartupAction PendingStartupAction { get; private set; }
    public static string? StartupActionPayloadPath { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        StartInBackground = args.Any(arg => string.Equals(arg, "--background", StringComparison.OrdinalIgnoreCase));
        ParseStartupAction(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    public static void ConsumeStartupAction()
    {
        PendingStartupAction = StartupAction.None;
        StartupActionPayloadPath = null;
    }

    private static void ParseStartupAction(string[] args)
    {
        PendingStartupAction = StartupAction.None;
        StartupActionPayloadPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--apply-managed-hosts", StringComparison.OrdinalIgnoreCase))
            {
                PendingStartupAction = StartupAction.ApplyManagedHosts;
            }
            else if (string.Equals(arg, "--restore-backup", StringComparison.OrdinalIgnoreCase))
            {
                PendingStartupAction = StartupAction.RestoreBackup;
            }
            else if (string.Equals(arg, "--save-raw-hosts", StringComparison.OrdinalIgnoreCase))
            {
                PendingStartupAction = StartupAction.SaveRawHosts;
            }
            else if (string.Equals(arg, "--payload-path", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                StartupActionPayloadPath = args[++i];
            }
        }
    }
}
