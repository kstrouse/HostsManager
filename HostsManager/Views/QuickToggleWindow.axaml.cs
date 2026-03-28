using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HostsManager.Models;
using HostsManager.ViewModels;

namespace HostsManager.Views;

public partial class QuickToggleWindow : Window
{
    private Action openFullEditorAction = static () => { };
    private Action exitAppAction = static () => { };

    public QuickToggleWindow()
    {
        InitializeComponent();
        WireWindowBehavior();
    }

    public QuickToggleWindow(MainWindowViewModel viewModel, Action openFullEditorAction, Action exitAppAction)
        : this()
    {
        DataContext = viewModel;
        this.openFullEditorAction = openFullEditorAction;
        this.exitAppAction = exitAppAction;
    }

    private void WireWindowBehavior()
    {
        Deactivated += (_, _) =>
        {
            if (IsVisible)
            {
                Hide();
            }
        };
    }

    private async void SourceEnabledChanged(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (sender is Control control)
        {
            await vm.HandleRemoteSourceToggledAsync(control.DataContext as HostProfile);
        }

        await vm.RequestImmediateReconcileAsync();
    }

    private void OpenFullEditorClick(object? sender, RoutedEventArgs e)
    {
        Hide();
        openFullEditorAction();
    }

    private void ExitAppClick(object? sender, RoutedEventArgs e)
    {
        exitAppAction();
    }
}
