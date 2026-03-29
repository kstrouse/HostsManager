using System;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HostsManager.Desktop.Models;

namespace HostsManager.Desktop.ViewModels;

public sealed partial class SelectedSourceDetailsViewModel : ViewModelBase
{
    private readonly MainWindowViewModel owner;
    private readonly Func<Task> applyToSystemHostsAsync;
    private readonly Action<bool> setSystemHostsEditingEnabled;
    private HostProfile? observedProfile;
    private bool isSyncingSystemHostsEditingEnabled;

    [ObservableProperty]
    private HostProfile? selectedProfile;

    [ObservableProperty]
    private bool hasPendingElevatedHostsUpdate;

    [ObservableProperty]
    private bool isSystemHostsEditingEnabled;

    public SelectedSourceDetailsViewModel(
        MainWindowViewModel owner,
        string hostsPath,
        Func<Task> applyToSystemHostsAsync,
        Action<bool> setSystemHostsEditingEnabled)
    {
        this.owner = owner;
        HostsPath = hostsPath;
        this.applyToSystemHostsAsync = applyToSystemHostsAsync;
        this.setSystemHostsEditingEnabled = setSystemHostsEditingEnabled;
        SelectedProfile = owner.SelectedProfile;
        HasPendingElevatedHostsUpdate = owner.HasPendingElevatedHostsUpdate;
        isSyncingSystemHostsEditingEnabled = true;
        IsSystemHostsEditingEnabled = owner.IsSystemHostsEditingEnabled;
        isSyncingSystemHostsEditingEnabled = false;
        owner.PropertyChanged += OnOwnerPropertyChanged;
        UpdateObservedProfile(SelectedProfile);
    }

    public string HostsPath { get; }

    public bool IsSelectedSourceReadOnly => SelectedProfile?.IsReadOnly == true;

    public string SelectedSourceTypeDisplay => SelectedProfile switch
    {
        null => string.Empty,
        { SourceType: SourceType.Remote } profile => $"Remote ({GetRemoteTransportDisplay(profile.RemoteTransport)})",
        { SourceType: SourceType.Local } => "Local",
        { SourceType: SourceType.System } => "System",
        _ => SelectedProfile.SourceType.ToString()
    };

    public bool IsSystemSelected => SelectedProfile?.SourceType == SourceType.System;

    [RelayCommand]
    private Task ApplyToSystemHostsAsync()
    {
        return applyToSystemHostsAsync();
    }

    partial void OnSelectedProfileChanged(HostProfile? value)
    {
        UpdateObservedProfile(value);
        RaiseSourceStateChanged();
    }

    partial void OnIsSystemHostsEditingEnabledChanged(bool value)
    {
        if (isSyncingSystemHostsEditingEnabled)
            return;

        setSystemHostsEditingEnabled(value);
    }

    private void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainWindowViewModel.SelectedProfile):
                SelectedProfile = owner.SelectedProfile;
                break;
            case nameof(MainWindowViewModel.HasPendingElevatedHostsUpdate):
                HasPendingElevatedHostsUpdate = owner.HasPendingElevatedHostsUpdate;
                break;
            case nameof(MainWindowViewModel.IsSystemHostsEditingEnabled):
                isSyncingSystemHostsEditingEnabled = true;
                try
                {
                    IsSystemHostsEditingEnabled = owner.IsSystemHostsEditingEnabled;
                }
                finally
                {
                    isSyncingSystemHostsEditingEnabled = false;
                }

                break;
        }
    }

    private void OnSelectedProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HostProfile.SourceType) or nameof(HostProfile.RemoteTransport) or nameof(HostProfile.IsReadOnly))
            RaiseSourceStateChanged();
    }

    private void UpdateObservedProfile(HostProfile? profile)
    {
        if (ReferenceEquals(observedProfile, profile))
            return;

        if (observedProfile is not null)
            observedProfile.PropertyChanged -= OnSelectedProfilePropertyChanged;

        observedProfile = profile;

        if (observedProfile is not null)
            observedProfile.PropertyChanged += OnSelectedProfilePropertyChanged;
    }

    private void RaiseSourceStateChanged()
    {
        OnPropertyChanged(nameof(IsSelectedSourceReadOnly));
        OnPropertyChanged(nameof(SelectedSourceTypeDisplay));
        OnPropertyChanged(nameof(IsSystemSelected));
    }

    private static string GetRemoteTransportDisplay(RemoteTransport remoteTransport) =>
        remoteTransport switch
        {
            RemoteTransport.Http or RemoteTransport.Https => "HTTP/HTTPS",
            RemoteTransport.AzurePrivateDns => "Azure Private DNS",
            _ => remoteTransport.ToString()
        };
}
