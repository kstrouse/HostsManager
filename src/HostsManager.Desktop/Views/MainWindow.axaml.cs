using System;
using System.ComponentModel;
using Avalonia.Controls;
using HostsManager.Desktop.ViewModels;

namespace HostsManager.Desktop.Views;

public partial class MainWindow : Window
{
    public bool CloseToTrayEnabled { get; set; }
    private MainWindowViewModel? currentViewModel;
    private bool isInitialized;
    private bool startHiddenToTray;

    public MainWindow()
    {
        InitializeComponent();

        DataContextChanged += (_, _) => BindCloseBehaviorToViewModel();
        BindCloseBehaviorToViewModel();

        Closing += (_, e) =>
        {
            if (!CloseToTrayEnabled)
                return;

            e.Cancel = true;
            Hide();
        };

        Opened += async (_, _) =>
        {
            if (!isInitialized && DataContext is MainWindowViewModel vm)
            {
                await vm.InitializeAsync();
                isInitialized = true;
            }

            if (startHiddenToTray)
            {
                startHiddenToTray = false;
                Hide();
                ShowInTaskbar = true;
                ShowActivated = true;
                Opacity = 1;
                WindowState = WindowState.Normal;
            }
        };
    }

    public void ConfigureStartupHiddenToTray()
    {
        startHiddenToTray = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Opacity = 0;
        WindowState = WindowState.Minimized;
    }

    public void ShowFromTray()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Show();
        Activate();
    }

    private void BindCloseBehaviorToViewModel()
    {
        if (currentViewModel is not null)
            currentViewModel.PropertyChanged -= OnViewModelPropertyChanged;

        currentViewModel = DataContext as MainWindowViewModel;

        if (currentViewModel is null)
        {
            CloseToTrayEnabled = false;
            return;
        }

        currentViewModel.PropertyChanged += OnViewModelPropertyChanged;
        CloseToTrayEnabled = currentViewModel.MinimizeToTrayOnClose;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.MinimizeToTrayOnClose) &&
            sender is MainWindowViewModel vm)
        {
            CloseToTrayEnabled = vm.MinimizeToTrayOnClose;
        }
    }
}
