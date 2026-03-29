using System.ComponentModel;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using HostsManager.Desktop.Models;

namespace HostsManager.Desktop.ViewModels;

public sealed class RemoteSourceEditorViewModel : ViewModelBase
{
    private readonly MainWindowViewModel owner;

    public RemoteSourceEditorViewModel(MainWindowViewModel owner)
    {
        this.owner = owner;
        owner.PropertyChanged += OnOwnerPropertyChanged;
    }

    public HostProfile? SelectedProfile => owner.SelectedProfile;

    public bool IsVisible => owner.IsRemoteSelected;

    public bool IsHttpRemoteSelected => owner.IsHttpRemoteSelected;

    public bool IsAzurePrivateDnsRemoteSelected => owner.IsAzurePrivateDnsRemoteSelected;

    public bool CanLoadAzureSubscriptions => owner.CanLoadAzureSubscriptions;

    public bool CanRefreshAzureZones => owner.CanRefreshAzureZones;

    public bool IsAzureSubscriptionsLoading => owner.IsAzureSubscriptionsLoading;

    public bool IsAzureZonesLoading => owner.IsAzureZonesLoading;

    public bool IsSelectedRemoteSyncRunning => owner.IsSelectedRemoteSyncRunning;

    public bool IsSelectedRemoteSyncIdle => owner.IsSelectedRemoteSyncIdle;

    public ObservableCollection<AzureSubscriptionOption> AzureSubscriptions => owner.AzureSubscriptions;

    public ObservableCollection<AzureZoneSelectionItem> AzureZones => owner.AzureZones;

    public AzureSubscriptionOption? SelectedAzureSubscription
    {
        get => owner.SelectedAzureSubscription;
        set => owner.SelectedAzureSubscription = value;
    }

    public IAsyncRelayCommand LoadAzureSubscriptionsCommand => owner.LoadAzureSubscriptionsCommand;

    public IAsyncRelayCommand RefreshAzureZonesCommand => owner.RefreshAzureZonesCommand;

    public IAsyncRelayCommand ReadSelectedRemoteHostsCommand => owner.ReadSelectedRemoteHostsCommand;

    private void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainWindowViewModel.SelectedProfile):
                OnPropertyChanged(nameof(SelectedProfile));
                OnPropertyChanged(nameof(IsVisible));
                OnPropertyChanged(nameof(IsHttpRemoteSelected));
                OnPropertyChanged(nameof(IsAzurePrivateDnsRemoteSelected));
                OnPropertyChanged(nameof(CanRefreshAzureZones));
                break;
            case nameof(MainWindowViewModel.SelectedAzureSubscription):
                OnPropertyChanged(nameof(SelectedAzureSubscription));
                break;
            case nameof(MainWindowViewModel.IsAzureSubscriptionsLoading):
                OnPropertyChanged(nameof(IsAzureSubscriptionsLoading));
                OnPropertyChanged(nameof(CanLoadAzureSubscriptions));
                break;
            case nameof(MainWindowViewModel.IsAzureZonesLoading):
                OnPropertyChanged(nameof(IsAzureZonesLoading));
                OnPropertyChanged(nameof(CanRefreshAzureZones));
                break;
            case nameof(MainWindowViewModel.IsSelectedRemoteSyncRunning):
                OnPropertyChanged(nameof(IsSelectedRemoteSyncRunning));
                OnPropertyChanged(nameof(IsSelectedRemoteSyncIdle));
                break;
            case nameof(MainWindowViewModel.CanLoadAzureSubscriptions):
                OnPropertyChanged(nameof(CanLoadAzureSubscriptions));
                break;
            case nameof(MainWindowViewModel.CanRefreshAzureZones):
                OnPropertyChanged(nameof(CanRefreshAzureZones));
                break;
            case nameof(MainWindowViewModel.IsHttpRemoteSelected):
                OnPropertyChanged(nameof(IsHttpRemoteSelected));
                break;
            case nameof(MainWindowViewModel.IsAzurePrivateDnsRemoteSelected):
                OnPropertyChanged(nameof(IsAzurePrivateDnsRemoteSelected));
                break;
        }
    }
}
