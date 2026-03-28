using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using HostsManager.Desktop.Services;
using HostsManager.Desktop.ViewModels;
using HostsManager.Desktop.Views;

namespace HostsManager.Desktop;

public partial class App : Application
{
    private const string DefaultTrayToolTip = "Hosts Manager is running";
    private const string PendingElevationTrayToolTip = "Hosts Manager needs administrator approval to apply pending hosts changes";
    private TrayIcon? trayIcon;
    private MainWindow? mainWindow;
    private QuickToggleWindow? quickToggleWindow;
    private IClassicDesktopStyleApplicationLifetime? desktopLifetime;
    private MainWindowViewModel? viewModel;
    private readonly DesktopNotificationService desktopNotificationService = new();
    private EventWaitHandle? showQuickToggleEvent;
    private RegisteredWaitHandle? showQuickToggleRegisteredWait;

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
            RegisterQuickToggleActivationSignal(desktop);
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
            viewModel = vm;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
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
            ToolTipText = DefaultTrayToolTip,
            Menu = menu,
            IsVisible = true,
            Icon = mainWindow.Icon
        };

        UpdateTrayToolTip();

        trayIcon.Clicked += (_, _) => ShowQuickToggle();
        desktop.Exit += (_, _) =>
        {
            if (viewModel is not null)
            {
                viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            showQuickToggleRegisteredWait?.Unregister(null);
            showQuickToggleEvent?.Dispose();
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

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.HasPendingElevatedHostsUpdate))
        {
            UpdateTrayToolTip();
            if (viewModel?.HasPendingElevatedHostsUpdate == true && !IsMainEditorForeground())
            {
                desktopNotificationService.ShowPendingApplyNotification();
            }
        }
    }

    private void UpdateTrayToolTip()
    {
        if (trayIcon is null)
        {
            return;
        }

        trayIcon.ToolTipText = viewModel?.HasPendingElevatedHostsUpdate == true
            ? PendingElevationTrayToolTip
            : DefaultTrayToolTip;
    }

    private void RegisterQuickToggleActivationSignal(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        showQuickToggleEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            DesktopNotificationService.ShowQuickToggleEventName);

        showQuickToggleRegisteredWait = ThreadPool.RegisterWaitForSingleObject(
            showQuickToggleEvent,
            (_, _) => Dispatcher.UIThread.Post(ShowQuickToggle),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    private bool IsMainEditorForeground()
    {
        if (mainWindow is null)
        {
            return false;
        }

        return mainWindow.IsVisible &&
               mainWindow.WindowState != WindowState.Minimized &&
               mainWindow.IsActive;
    }
}
