using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace HostsManager.Desktop.ViewModels;

public sealed class StatusActionBarViewModel : ViewModelBase
{
    private readonly MainWindowViewModel owner;

    public StatusActionBarViewModel(MainWindowViewModel owner, bool canConfigureRunAtStartup)
    {
        this.owner = owner;
        CanConfigureRunAtStartup = canConfigureRunAtStartup;
        owner.PropertyChanged += OnOwnerPropertyChanged;
    }

    public string StatusMessage => owner.StatusMessage;

    public IAsyncRelayCommand SyncAllFromUrlCommand => owner.SyncAllFromUrlCommand;

    public IAsyncRelayCommand ApplyToSystemHostsCommand => owner.ApplyToSystemHostsCommand;

    public bool MinimizeToTrayOnClose
    {
        get => owner.MinimizeToTrayOnClose;
        set => owner.MinimizeToTrayOnClose = value;
    }

    public bool RunAtStartup
    {
        get => owner.RunAtStartup;
        set => owner.RunAtStartup = value;
    }

    public bool CanConfigureRunAtStartup { get; }

    private void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainWindowViewModel.StatusMessage):
                OnPropertyChanged(nameof(StatusMessage));
                break;
            case nameof(MainWindowViewModel.MinimizeToTrayOnClose):
                OnPropertyChanged(nameof(MinimizeToTrayOnClose));
                break;
            case nameof(MainWindowViewModel.RunAtStartup):
                OnPropertyChanged(nameof(RunAtStartup));
                break;
        }
    }
}
