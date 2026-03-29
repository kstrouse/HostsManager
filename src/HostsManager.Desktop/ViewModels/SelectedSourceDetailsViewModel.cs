using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HostsManager.Desktop.Models;

namespace HostsManager.Desktop.ViewModels;

public sealed class SelectedSourceDetailsViewModel : ViewModelBase
{
    private readonly MainWindowViewModel owner;
    private HostProfile? observedProfile;

    public SelectedSourceDetailsViewModel(MainWindowViewModel owner)
    {
        this.owner = owner;
        owner.PropertyChanged += OnOwnerPropertyChanged;
        UpdateObservedProfile(owner.SelectedProfile);
    }

    public HostProfile? SelectedProfile => owner.SelectedProfile;

    public bool HasPendingElevatedHostsUpdate => owner.HasPendingElevatedHostsUpdate;

    public IAsyncRelayCommand ApplyToSystemHostsCommand => owner.ApplyToSystemHostsCommand;

    public string HostsPath => owner.HostsPath;

    public bool IsSelectedSourceReadOnly => owner.SelectedProfile?.IsReadOnly == true;

    public string SelectedSourceTypeDisplay => owner.SelectedProfile switch
    {
        null => string.Empty,
        { SourceType: SourceType.Remote } profile => $"Remote ({GetRemoteTransportDisplay(profile.RemoteTransport)})",
        { SourceType: SourceType.Local } => "Local",
        { SourceType: SourceType.System } => "System",
        _ => owner.SelectedProfile.SourceType.ToString()
    };

    public bool IsSystemSelected => owner.SelectedProfile?.SourceType == SourceType.System;

    public bool IsSystemHostsEditingEnabled
    {
        get => owner.IsSystemHostsEditingEnabled;
        set => owner.IsSystemHostsEditingEnabled = value;
    }

    private void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainWindowViewModel.SelectedProfile):
                UpdateObservedProfile(owner.SelectedProfile);
                RaiseSourceStateChanged();
                break;
            case nameof(MainWindowViewModel.HasPendingElevatedHostsUpdate):
                OnPropertyChanged(nameof(HasPendingElevatedHostsUpdate));
                break;
            case nameof(MainWindowViewModel.IsSystemHostsEditingEnabled):
                OnPropertyChanged(nameof(IsSystemHostsEditingEnabled));
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
        OnPropertyChanged(nameof(SelectedProfile));
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
