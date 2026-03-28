using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia.Markup.Xaml;
using HostsManager.ViewModels;
using HostsManager.Views;

namespace HostsManager;

public partial class App : Application
{
    private TrayIcon? trayIcon;
    private MainWindow? mainWindow;
    private QuickToggleWindow? quickToggleWindow;
    private IClassicDesktopStyleApplicationLifetime? desktopLifetime;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktopLifetime = desktop;
            DisableAvaloniaDataAnnotationValidation();
            mainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };

            if (Program.StartInBackground)
            {
                mainWindow.ConfigureStartupHiddenToTray();
            }

            desktop.MainWindow = mainWindow;
            ConfigureTray(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }

    private void ConfigureTray(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (mainWindow is null)
        {
            return;
        }

        if (mainWindow.DataContext is MainWindowViewModel vm)
        {
            quickToggleWindow = new QuickToggleWindow(vm, () => ShowMainWindow(), ExitApplication);
        }

        var showItem = new NativeMenuItem("Open Full Editor")
        {
            ToolTip = "Show Hosts Manager window"
        };
        showItem.Click += (_, _) => ShowMainWindow();

        var applyItem = new NativeMenuItem("Apply Hosts Now")
        {
            ToolTip = "Apply enabled sources to hosts file"
        };
        applyItem.Click += (_, _) =>
        {
            if (mainWindow.DataContext is MainWindowViewModel vm && vm.ApplyToSystemHostsCommand.CanExecute(null))
            {
                vm.ApplyToSystemHostsCommand.Execute(null);
            }
        };

        var exitItem = new NativeMenuItem("Exit")
        {
            ToolTip = "Exit application"
        };
        exitItem.Click += (_, _) =>
        {
            ExitApplication();
        };

        var menu = new NativeMenu();
        menu.Add(showItem);
        menu.Add(applyItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(exitItem);

        trayIcon = new TrayIcon
        {
            ToolTipText = "Hosts Manager is running",
            Menu = menu,
            IsVisible = true,
            Icon = mainWindow.Icon
        };

        trayIcon.Clicked += (_, _) => ShowQuickToggle();
        desktop.Exit += (_, _) =>
        {
            quickToggleWindow?.Close();
            trayIcon?.Dispose();
        };
    }

    private void ShowMainWindow()
    {
        quickToggleWindow?.Hide();
        mainWindow?.ShowFromTray();
    }

    private void ShowQuickToggle()
    {
        if (quickToggleWindow is null)
        {
            if (mainWindow?.DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            quickToggleWindow = new QuickToggleWindow(vm, () => ShowMainWindow(), ExitApplication);
        }

        if (quickToggleWindow.WindowState == WindowState.Minimized)
        {
            quickToggleWindow.WindowState = WindowState.Normal;
        }

        PositionQuickToggleNearTray();

        quickToggleWindow.Show();
        quickToggleWindow.Activate();
    }

    private void PositionQuickToggleNearTray()
    {
        if (quickToggleWindow is null)
        {
            return;
        }

        var screen = quickToggleWindow.Screens?.Primary;
        if (screen is null)
        {
            return;
        }

        const int margin = 10;
        var width = (int)Math.Ceiling(quickToggleWindow.Width > 0 ? quickToggleWindow.Width : 340);
        var height = (int)Math.Ceiling(quickToggleWindow.Height > 0 ? quickToggleWindow.Height : 420);

        var workingArea = screen.WorkingArea;
        var x = workingArea.X + Math.Max(margin, workingArea.Width - width - margin);
        var y = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? workingArea.Y + margin
            : workingArea.Y + Math.Max(margin, workingArea.Height - height - margin);

        quickToggleWindow.Position = new PixelPoint(x, y);
    }

    private void ExitApplication()
    {
        quickToggleWindow?.Close();

        if (mainWindow is not null)
        {
            mainWindow.CloseToTrayEnabled = false;
            mainWindow.Close();
        }

        trayIcon?.Dispose();
        desktopLifetime?.Shutdown();
    }
}
