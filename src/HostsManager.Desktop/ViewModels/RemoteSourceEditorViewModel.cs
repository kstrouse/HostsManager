using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HostsManager.Desktop.Models;
using HostsManager.Desktop.Services;

namespace HostsManager.Desktop.ViewModels;

public sealed partial class RemoteSourceEditorViewModel : ViewModelBase
{
    private readonly MainWindowViewModel owner;
    private readonly IProfileSelectionService profileSelectionService;
    private readonly IRemoteSourceSyncService remoteSourceSyncService;
    private readonly IRemoteSyncWorkflowService remoteSyncWorkflowService;
    private readonly IAzureProfileCommandService azureProfileCommandService;
    private readonly Func<Task> persistConfigurationAsync;
    private readonly Func<Task> runBackgroundManagementAsync;
    private readonly Action notifySelectedProfileChanged;
    private HostProfile? observedProfile;
    private bool isSyncingSelectedAzureSubscription;

    [ObservableProperty]
    private AzureSubscriptionOption? selectedAzureSubscription;

    [ObservableProperty]
    private bool isAzureSubscriptionsLoading;

    [ObservableProperty]
    private bool isAzureZonesLoading;

    [ObservableProperty]
    private bool isSelectedRemoteSyncRunning;

    public RemoteSourceEditorViewModel(
        MainWindowViewModel owner,
        IProfileSelectionService profileSelectionService,
        IRemoteSourceSyncService remoteSourceSyncService,
        IRemoteSyncWorkflowService remoteSyncWorkflowService,
        IAzureProfileCommandService azureProfileCommandService,
        Func<Task> persistConfigurationAsync,
        Func<Task> runBackgroundManagementAsync,
        Action notifySelectedProfileChanged)
    {
        this.owner = owner;
        this.profileSelectionService = profileSelectionService;
        this.remoteSourceSyncService = remoteSourceSyncService;
        this.remoteSyncWorkflowService = remoteSyncWorkflowService;
        this.azureProfileCommandService = azureProfileCommandService;
        this.persistConfigurationAsync = persistConfigurationAsync;
        this.runBackgroundManagementAsync = runBackgroundManagementAsync;
        this.notifySelectedProfileChanged = notifySelectedProfileChanged;
        owner.PropertyChanged += OnOwnerPropertyChanged;
        UpdateObservedProfile(owner.SelectedProfile);
    }

    public HostProfile? SelectedProfile => owner.SelectedProfile;

    public bool IsVisible => owner.SelectedProfile?.SourceType == SourceType.Remote;

    public bool IsHttpRemoteSelected =>
        owner.SelectedProfile?.SourceType == SourceType.Remote &&
        owner.SelectedProfile.RemoteTransport is RemoteTransport.Http or RemoteTransport.Https;

    public bool IsAzurePrivateDnsRemoteSelected =>
        owner.SelectedProfile?.SourceType == SourceType.Remote &&
        owner.SelectedProfile.RemoteTransport == RemoteTransport.AzurePrivateDns;

    public bool CanLoadAzureSubscriptions => !IsAzureSubscriptionsLoading;

    public bool CanRefreshAzureZones => !IsAzureZonesLoading &&
        owner.SelectedProfile is not null &&
        owner.SelectedProfile.SourceType == SourceType.Remote &&
        owner.SelectedProfile.RemoteTransport == RemoteTransport.AzurePrivateDns &&
        !string.IsNullOrWhiteSpace(owner.SelectedProfile.AzureSubscriptionId);

    public bool IsSelectedRemoteSyncIdle => !IsSelectedRemoteSyncRunning;

    public ObservableCollection<AzureSubscriptionOption> AzureSubscriptions { get; } = [];

    public ObservableCollection<AzureZoneSelectionItem> AzureZones { get; } = [];

    [RelayCommand]
    private async Task LoadAzureSubscriptionsAsync()
    {
        if (IsAzureSubscriptionsLoading)
            return;

        IsAzureSubscriptionsLoading = true;
        owner.StatusMessage = "Loading Azure subscriptions...";

        try
        {
            var result = await azureProfileCommandService.LoadSubscriptionsAsync(
                owner.SelectedProfile,
                owner.IsSystemHostsEditingEnabled);

            ReplaceAzureSubscriptions(result.Subscriptions);
            if (result.SelectedProfileChange is not null)
                ApplySelectedProfileChange(result.SelectedProfileChange);

            if (!string.IsNullOrWhiteSpace(result.StatusMessage))
                owner.StatusMessage = result.StatusMessage;
        }
        finally
        {
            IsAzureSubscriptionsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAzureZonesAsync()
    {
        await LoadAndApplyAzureZonesAsync(
            () => azureProfileCommandService.RefreshZonesAsync(owner.SelectedProfile));
    }

    [RelayCommand(CanExecute = nameof(CanReadSelectedRemoteHosts))]
    private async Task ReadSelectedRemoteHostsAsync()
    {
        IsSelectedRemoteSyncRunning = true;
        try
        {
            var result = await remoteSyncWorkflowService.SyncSelectedSourceAsync(
                owner.SelectedProfile,
                PrepareProfileForRemoteSyncAsync);
            await ApplyRemoteSyncCommandResultAsync(result);
        }
        finally
        {
            IsSelectedRemoteSyncRunning = false;
        }
    }

    public Task ApplyRemoteProfilesSyncResultAsync(RemoteProfilesSyncResult result)
    {
        return ApplyRemoteSyncResultAsync(
            result.ShouldPersistConfiguration,
            result.ShouldNotifySelectedProfileChanged,
            result.ShouldRunBackgroundManagement,
            result.StatusMessage);
    }

    public Task ApplyRemoteSyncCommandResultAsync(RemoteSourceSyncCommandResult result)
    {
        return ApplyRemoteSyncResultAsync(
            result.ShouldPersistConfiguration,
            result.ShouldNotifySelectedProfileChanged,
            result.ShouldRunBackgroundManagement,
            result.StatusMessage);
    }

    public async Task PrepareProfileForRemoteSyncAsync(HostProfile profile, CancellationToken cancellationToken)
    {
        if (profile.RemoteTransport == RemoteTransport.AzurePrivateDns &&
            ReferenceEquals(profile, owner.SelectedProfile))
        {
            await LoadAndApplyAzureZonesAsync(
                () => azureProfileCommandService.RefreshZonesForSelectionAsync(profile, cancellationToken));
        }
    }

    private bool CanReadSelectedRemoteHosts() =>
        owner.SelectedProfile is { SourceType: SourceType.Remote } && !IsSelectedRemoteSyncRunning;

    partial void OnSelectedAzureSubscriptionChanged(AzureSubscriptionOption? value)
    {
        if (isSyncingSelectedAzureSubscription)
            return;

        var change = profileSelectionService.CreateAzureSubscriptionSelectionChange(owner.SelectedProfile, value);
        if (change is null)
            return;

        var selectedProfile = owner.SelectedProfile;
        if (selectedProfile is null)
            return;

        selectedProfile.AzureSubscriptionId = change.SubscriptionId;
        selectedProfile.AzureSubscriptionName = change.SubscriptionName;
        _ = RefreshAzureZonesForCurrentSelectionAsync();
        OnPropertyChanged(nameof(CanRefreshAzureZones));
    }

    partial void OnIsAzureSubscriptionsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanLoadAzureSubscriptions));
    }

    partial void OnIsAzureZonesLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRefreshAzureZones));
    }

    partial void OnIsSelectedRemoteSyncRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSelectedRemoteSyncIdle));
        ReadSelectedRemoteHostsCommand.NotifyCanExecuteChanged();
    }

    private async Task ApplyRemoteSyncResultAsync(
        bool shouldPersistConfiguration,
        bool shouldNotifySelectedProfileChanged,
        bool shouldRunBackgroundManagement,
        string? statusMessage)
    {
        if (shouldPersistConfiguration)
            await persistConfigurationAsync();

        if (shouldNotifySelectedProfileChanged)
            notifySelectedProfileChanged();

        if (shouldRunBackgroundManagement)
            await runBackgroundManagementAsync();

        if (!string.IsNullOrWhiteSpace(statusMessage))
            owner.StatusMessage = statusMessage;
    }

    private async Task LoadAndApplyAzureZonesAsync(Func<Task<AzureZonesLoadResult>> loadAsync)
    {
        IsAzureZonesLoading = true;
        try
        {
            var result = await loadAsync();
            if (result.ShouldReplaceZones)
                ReplaceAzureZones(result.Zones);

            if (!string.IsNullOrWhiteSpace(result.StatusMessage))
                owner.StatusMessage = result.StatusMessage;
        }
        finally
        {
            IsAzureZonesLoading = false;
        }
    }

    private void ReplaceAzureSubscriptions(IEnumerable<AzureSubscriptionOption> subscriptions)
    {
        AzureSubscriptions.Clear();
        foreach (var subscription in subscriptions)
            AzureSubscriptions.Add(subscription);
    }

    private void ReplaceAzureZones(IEnumerable<AzureZoneSelectionItem> zones)
    {
        foreach (var zone in AzureZones)
            zone.PropertyChanged -= OnAzureZoneSelectionChanged;

        AzureZones.Clear();

        foreach (var zone in zones)
        {
            zone.PropertyChanged += OnAzureZoneSelectionChanged;
            AzureZones.Add(zone);
        }

        UpdateSelectedProfileExcludedZonesFromSelection();
    }

    private void OnAzureZoneSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AzureZoneSelectionItem.IsEnabled))
            UpdateSelectedProfileExcludedZonesFromSelection();
    }

    private void UpdateSelectedProfileExcludedZonesFromSelection()
    {
        if (owner.SelectedProfile is null ||
            owner.SelectedProfile.SourceType != SourceType.Remote ||
            owner.SelectedProfile.RemoteTransport != RemoteTransport.AzurePrivateDns)
            return;

        owner.SelectedProfile.AzureExcludedZones = remoteSourceSyncService.BuildExcludedZones(AzureZones);
    }

    private async Task RefreshAzureZonesForCurrentSelectionAsync()
    {
        await LoadAndApplyAzureZonesAsync(
            () => azureProfileCommandService.RefreshZonesForSelectionAsync(owner.SelectedProfile));
    }

    private void ApplySelectedProfileChange(SelectedProfileChange change)
    {
        if (change.ClearAzureZones)
            ReplaceAzureZones([]);

        if (change.SubscriptionToInsert is not null)
            AzureSubscriptions.Insert(0, change.SubscriptionToInsert);

        if (change.ShouldUpdateSelectedAzureSubscription)
        {
            isSyncingSelectedAzureSubscription = true;
            try
            {
                SelectedAzureSubscription = change.SelectedAzureSubscription;
            }
            finally
            {
                isSyncingSelectedAzureSubscription = false;
            }
        }

        if (change.RefreshAzureZones)
            _ = RefreshAzureZonesForCurrentSelectionAsync();
    }

    private void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedProfile))
        {
            UpdateObservedProfile(owner.SelectedProfile);
            ApplySelectedProfileChange(profileSelectionService.EvaluateSelectedProfile(
                owner.SelectedProfile,
                owner.IsSystemHostsEditingEnabled,
                AzureSubscriptions));
            RaiseRemoteSelectionStateChanged();
        }
    }

    private void OnSelectedProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(HostProfile.SourceType) or nameof(HostProfile.RemoteTransport) or nameof(HostProfile.AzureSubscriptionId)))
            return;

        RaiseRemoteSelectionStateChanged();
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

    private void RaiseRemoteSelectionStateChanged()
    {
        OnPropertyChanged(nameof(SelectedProfile));
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(IsHttpRemoteSelected));
        OnPropertyChanged(nameof(IsAzurePrivateDnsRemoteSelected));
        OnPropertyChanged(nameof(CanRefreshAzureZones));
        ReadSelectedRemoteHostsCommand.NotifyCanExecuteChanged();
    }
}
