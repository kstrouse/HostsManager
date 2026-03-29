using System;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace HostsManager.Desktop.ViewModels;

public sealed partial class StatusActionBarViewModel : ViewModelBase
{
    private readonly MainWindowViewModel owner;
    private readonly Func<Task> syncAllFromUrlAsync;
    private readonly Func<Task> applyToSystemHostsAsync;
    private readonly Action<bool> setMinimizeToTrayOnClose;
    private readonly Action<bool> setRunAtStartup;
    private bool isSyncingMinimizeToTrayOnClose;
    private bool isSyncingRunAtStartup;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool minimizeToTrayOnClose;

    [ObservableProperty]
    private bool runAtStartup;

    public StatusActionBarViewModel(
        MainWindowViewModel owner,
        bool canConfigureRunAtStartup,
        Func<Task> syncAllFromUrlAsync,
        Func<Task> applyToSystemHostsAsync,
        Action<bool> setMinimizeToTrayOnClose,
        Action<bool> setRunAtStartup)
    {
        this.owner = owner;
        CanConfigureRunAtStartup = canConfigureRunAtStartup;
        this.syncAllFromUrlAsync = syncAllFromUrlAsync;
        this.applyToSystemHostsAsync = applyToSystemHostsAsync;
        this.setMinimizeToTrayOnClose = setMinimizeToTrayOnClose;
        this.setRunAtStartup = setRunAtStartup;
        StatusMessage = owner.StatusMessage;
        isSyncingMinimizeToTrayOnClose = true;
        MinimizeToTrayOnClose = owner.MinimizeToTrayOnClose;
        isSyncingMinimizeToTrayOnClose = false;
        isSyncingRunAtStartup = true;
        RunAtStartup = owner.RunAtStartup;
        isSyncingRunAtStartup = false;
        owner.PropertyChanged += OnOwnerPropertyChanged;
    }

    public bool CanConfigureRunAtStartup { get; }

    [RelayCommand]
    private Task SyncAllFromUrlAsync()
    {
        return syncAllFromUrlAsync();
    }

    [RelayCommand]
    private Task ApplyToSystemHostsAsync()
    {
        return applyToSystemHostsAsync();
    }

    partial void OnMinimizeToTrayOnCloseChanged(bool value)
    {
        if (isSyncingMinimizeToTrayOnClose)
            return;

        setMinimizeToTrayOnClose(value);
    }

    partial void OnRunAtStartupChanged(bool value)
    {
        if (isSyncingRunAtStartup)
            return;

        setRunAtStartup(value);
    }

    private void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainWindowViewModel.StatusMessage):
                StatusMessage = owner.StatusMessage;
                break;
            case nameof(MainWindowViewModel.MinimizeToTrayOnClose):
                isSyncingMinimizeToTrayOnClose = true;
                try
                {
                    MinimizeToTrayOnClose = owner.MinimizeToTrayOnClose;
                }
                finally
                {
                    isSyncingMinimizeToTrayOnClose = false;
                }

                break;
            case nameof(MainWindowViewModel.RunAtStartup):
                isSyncingRunAtStartup = true;
                try
                {
                    RunAtStartup = owner.RunAtStartup;
                }
                finally
                {
                    isSyncingRunAtStartup = false;
                }

                break;
        }
    }
}
