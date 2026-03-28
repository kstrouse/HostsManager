using Avalonia;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System;

namespace HostsManager;

sealed class Program
{
    public static bool StartInBackground { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        StartInBackground = args.Any(arg => string.Equals(arg, "--background", StringComparison.OrdinalIgnoreCase));

        if (NeedsElevation())
        {
            TryRestartElevated();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static bool NeedsElevation()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return !principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void TryRestartElevated()
    {
        var commandArgs = Environment.GetCommandLineArgs().Skip(1).Select(QuoteArg);
        var argumentString = string.Join(" ", commandArgs);

        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName,
            Arguments = argumentString,
            UseShellExecute = true,
            Verb = "runas"
        };

        if (string.IsNullOrWhiteSpace(startInfo.FileName))
        {
            return;
        }

        try
        {
            Process.Start(startInfo);
        }
        catch
        {
        }
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
