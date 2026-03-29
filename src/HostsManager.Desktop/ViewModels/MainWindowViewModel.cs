using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HostsManager.Desktop.Models;
using HostsManager.Desktop.Services;

namespace HostsManager.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IProfileStore profileStore;
    private readonly ILocalSourceService localSourceService;
    private readonly ILocalSourceRefreshService localSourceRefreshService;
    private readonly ILocalSourceWatcherService localSourceWatcherService;
    private readonly IHostsStateTracker hostsStateTracker;
    private readonly IRemoteSourceSyncService remoteSourceSyncService;
    private readonly IStartupRegistrationService startupRegistrationService;
    private readonly ISystemHostsWorkflowService systemHostsWorkflowService;
    private readonly IUiTimer refreshTimer;
    private readonly IUiTimer manageTimer;
    private bool isRefreshRunning;
    private bool isManageRunning;
    private bool isReconcileScheduled;
    private bool reconcileRequested;
    private bool systemHostsRefreshPulseRequested;
    private bool isInitializing;
    private bool isInitialized;
    private bool isUpdatingRunAtStartup;
    private string? quickSyncProfileId;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReloadLocalSourceCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveEntriesToLocalCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveSelectedSourceCommand))]
    private HostProfile? selectedProfile;

    [ObservableProperty]
    private string statusMessage = "Ready";

    [ObservableProperty]
    private bool minimizeToTrayOnClose = false;

    [ObservableProperty]
    private bool runAtStartup;

    [ObservableProperty]
    private bool hasPendingElevatedHostsUpdate;

    [ObservableProperty]
    private AzureSubscriptionOption? selectedAzureSubscription;

    [ObservableProperty]
    private bool isAzureSubscriptionsLoading;

    [ObservableProperty]
    private bool isAzureZonesLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectedEntriesReadOnly))]
    [NotifyCanExecuteChangedFor(nameof(SaveSelectedSourceCommand))]
    private bool isSystemHostsEditingEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectedRemoteSyncIdle))]
    [NotifyCanExecuteChangedFor(nameof(ReadSelectedRemoteHostsCommand))]
    private bool isSelectedRemoteSyncRunning;

    [ObservableProperty]
    private bool selectedSourceChangedExternally;

    [ObservableProperty]
    private string selectedSourceExternalChangeName = string.Empty;

    public ObservableCollection<HostProfile> Profiles { get; } = [];
    public ObservableCollection<AzureSubscriptionOption> AzureSubscriptions { get; } = [];
    public ObservableCollection<AzureZoneSelectionItem> AzureZones { get; } = [];
    public ObservableCollection<RemoteTransport> RemoteTransports { get; } =
    [
        RemoteTransport.Https,
        RemoteTransport.Http,
        RemoteTransport.Sftp,
        RemoteTransport.AzurePrivateDns
    ];

    public string HostsPath { get; }
    public bool CanLoadAzureSubscriptions => !IsAzureSubscriptionsLoading;
    public bool CanConfigureRunAtStartup => startupRegistrationService.IsSupported;
    public bool CanRefreshAzureZones => !IsAzureZonesLoading &&
        SelectedProfile is not null &&
        SelectedProfile.SourceType == SourceType.Remote &&
        SelectedProfile.RemoteTransport == RemoteTransport.AzurePrivateDns &&
        !string.IsNullOrWhiteSpace(SelectedProfile.AzureSubscriptionId);
    public bool IsSelectedSourceReadOnly => SelectedProfile?.IsReadOnly == true;
    public bool IsSelectedRemoteSyncIdle => !IsSelectedRemoteSyncRunning;
    public bool IsSelectedEntriesReadOnly => SelectedProfile switch
    {
        null => true,
        { SourceType: SourceType.Remote } => true,
        { SourceType: SourceType.Local, IsMissingLocalFile: true } => true,
        { SourceType: SourceType.System } => !IsSystemHostsEditingEnabled,
        { IsReadOnly: true } => true,
        _ => false
    };
    public string SelectedSourceTypeDisplay => SelectedProfile switch
    {
        null => string.Empty,
        { SourceType: SourceType.Remote } profile => $"Remote ({GetRemoteTransportDisplay(profile.RemoteTransport)})",
        { SourceType: SourceType.Local } => "Local",
        { SourceType: SourceType.System } => "System",
        _ => SelectedProfile.SourceType.ToString()
    };
    public bool IsSystemSelected => SelectedProfile?.SourceType == SourceType.System;
    public bool IsLocalSelected => SelectedProfile?.SourceType == SourceType.Local;
    public bool IsRemoteSelected => SelectedProfile?.SourceType == SourceType.Remote;
    public bool IsQuickSyncRunning => !string.IsNullOrWhiteSpace(quickSyncProfileId);
    public bool IsHttpRemoteSelected =>
        SelectedProfile?.SourceType == SourceType.Remote &&
        SelectedProfile.RemoteTransport is RemoteTransport.Http or RemoteTransport.Https;
    public bool IsAzurePrivateDnsRemoteSelected =>
        SelectedProfile?.SourceType == SourceType.Remote &&
        SelectedProfile.RemoteTransport == RemoteTransport.AzurePrivateDns;
    public string SelectedLocalFilePath =>
        SelectedProfile?.SourceType == SourceType.Local
            ? SelectedProfile.LocalPath
            : string.Empty;
    public string SelectedLocalFolderPath =>
        SelectedProfile?.SourceType == SourceType.Local && !string.IsNullOrWhiteSpace(SelectedProfile.LocalPath)
            ? Path.GetDirectoryName(SelectedProfile.LocalPath) ?? string.Empty
            : string.Empty;
    public bool IsSelectedLocalFileMissing =>
        SelectedProfile is { SourceType: SourceType.Local, IsMissingLocalFile: true };

    public MainWindowViewModel()
        : this(
            new ProfileStore(),
            new HostsFileService(),
            new LocalSourceService(),
            null,
            new LocalSourceWatcherService(),
            new StartupRegistrationService(),
            new WindowsElevationService(),
            CreateDefaultHttpClient(),
            null,
            CreateTimer(TimeSpan.FromMinutes(1)),
            CreateTimer(TimeSpan.FromSeconds(2)),
            null,
            null,
            null)
    {
    }

    public MainWindowViewModel(
        IProfileStore profileStore,
        IHostsFileService hostsFileService,
        ILocalSourceService localSourceService,
        ILocalSourceRefreshService? localSourceRefreshService,
        ILocalSourceWatcherService localSourceWatcherService,
        IStartupRegistrationService startupRegistrationService,
        IWindowsElevationService windowsElevationService,
        HttpClient httpClient,
        IAzurePrivateDnsService? azurePrivateDnsService = null,
        IUiTimer? refreshTimer = null,
        IUiTimer? manageTimer = null,
        IHostsStateTracker? hostsStateTracker = null,
        ISystemHostsWorkflowService? systemHostsWorkflowService = null,
        IRemoteSourceSyncService? remoteSourceSyncService = null)
    {
        this.profileStore = profileStore;
        this.localSourceService = localSourceService;
        var resolvedSystemHostsWorkflowService = systemHostsWorkflowService ?? new SystemHostsWorkflowService(
            hostsFileService,
            localSourceService,
            profileStore,
            windowsElevationService);
        this.localSourceRefreshService = localSourceRefreshService ?? new LocalSourceRefreshService(localSourceService, resolvedSystemHostsWorkflowService);
        this.localSourceWatcherService = localSourceWatcherService;
        this.hostsStateTracker = hostsStateTracker ?? new HostsStateTracker();
        this.startupRegistrationService = startupRegistrationService;
        this.systemHostsWorkflowService = resolvedSystemHostsWorkflowService;
        var resolvedAzurePrivateDnsService = azurePrivateDnsService ?? new AzurePrivateDnsService(httpClient);
        this.remoteSourceSyncService = remoteSourceSyncService ?? new RemoteSourceSyncService(httpClient, resolvedAzurePrivateDnsService);
        this.refreshTimer = refreshTimer ?? CreateTimer(TimeSpan.FromMinutes(1));
        this.manageTimer = manageTimer ?? CreateTimer(TimeSpan.FromSeconds(2));

        this.refreshTimer.Tick += async (_, _) => await RefreshRemoteProfilesAsync(forceAll: false, userInitiated: false);
        this.manageTimer.Tick += async (_, _) => await RunBackgroundManagementTickAsync();

        HostsPath = this.systemHostsWorkflowService.GetHostsFilePath();
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        return new HttpClient();
    }

    private static IUiTimer CreateTimer(TimeSpan interval)
    {
        return new DispatcherTimerAdapter(interval);
    }
    public async Task InitializeAsync()
    {
        if (isInitialized || isInitializing)
        {
            return;
        }

        isInitializing = true;
        try
        {
            var loaded = await profileStore.LoadAsync();
            Profiles.Clear();
            MinimizeToTrayOnClose = loaded.MinimizeToTrayOnClose;
            RunAtStartup = loaded.RunAtStartup;

            var systemSource = await systemHostsWorkflowService.BuildSystemSourceAsync();
            Profiles.Add(systemSource);

            foreach (var profile in loaded.Profiles)
            {
                Profiles.Add(profile);
            }

            SelectedProfile = Profiles.FirstOrDefault();
            InitializeManagedStateFromSystemHosts();
            await EnsureRunAtStartupMatchesPreferenceAsync();
            hostsStateTracker.MarkConfigurationSaved(MinimizeToTrayOnClose, RunAtStartup, Profiles);

            refreshTimer.Start();
            manageTimer.Start();
            localSourceWatcherService.MarkDirty();
            await RunBackgroundManagementTickAsync();
            var hadPendingStartupAction = Program.PendingStartupAction != StartupAction.None;
            await ExecutePendingStartupActionAsync();

            if (!hadPendingStartupAction)
            {
                StatusMessage = $"Loaded {Profiles.Count} source(s).";
            }

            isInitialized = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Load failed: {ex.Message}";
        }
        finally
        {
            isInitializing = false;
        }
    }

    [RelayCommand]
    private async Task SaveProfilesAsync()
    {
        try
        {
            await SaveConfigurationAsync();
            hostsStateTracker.MarkConfigurationSaved(MinimizeToTrayOnClose, RunAtStartup, Profiles);
            await RunBackgroundManagementTickAsync();
            StatusMessage = "Sources saved.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanSaveSelectedSource))]
    private async Task SaveSelectedSourceAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        if (SelectedProfile.SourceType == SourceType.Local)
        {
            if (string.IsNullOrWhiteSpace(SelectedProfile.LocalPath))
            {
                StatusMessage = "Local source path is empty.";
                return;
            }

            try
            {
                await File.WriteAllTextAsync(SelectedProfile.LocalPath, SelectedProfile.Entries ?? string.Empty);
                SelectedProfile.LastLoadedFromDiskEntries = SelectedProfile.Entries ?? string.Empty;
                localSourceWatcherService.MarkDirty();
                await SaveProfilesAsync();
                StatusMessage = $"Saved source: {SelectedProfile.Name}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Save local failed: {ex.Message}";
            }

            return;
        }

        if (SelectedProfile.SourceType == SourceType.System)
        {
            if (!IsSystemHostsEditingEnabled)
            {
                StatusMessage = "Enable system hosts editing before saving direct edits.";
                return;
            }

            await SaveSystemHostsDirectAsync(SelectedProfile);
            return;
        }

        if (SelectedProfile.IsReadOnly)
        {
            return;
        }

        await SaveProfilesAsync();
        StatusMessage = $"Saved source: {SelectedProfile.Name}";
    }

    [RelayCommand]
    private void NewRemoteSource()
    {
        AddRemoteSource(RemoteTransport.Https);
    }

    public void AddRemoteSource(RemoteTransport remoteTransport)
    {
        var sourceIndex = GetPersistedSources().Count() + 1;
        var profile = new HostProfile
        {
            Name = $"Remote Source {sourceIndex}",
            IsEnabled = true,
            SourceType = SourceType.Remote,
            RemoteTransport = remoteTransport,
            RefreshIntervalMinutes = "15",
            Entries = string.Empty
        };

        Profiles.Add(profile);
        SelectedProfile = profile;
        localSourceWatcherService.MarkDirty();
        StatusMessage = $"New remote source created ({GetRemoteTransportDisplay(remoteTransport)}).";
    }

    [RelayCommand(CanExecute = nameof(CanDeleteProfile))]
    private void DeleteProfile(HostProfile? source)
    {
        var target = source ?? SelectedProfile;
        if (target is null)
        {
            return;
        }

        if (target.IsReadOnly)
        {
            StatusMessage = "Read-only source cannot be deleted.";
            return;
        }

        var current = target;
        var index = Profiles.IndexOf(current);
        if (index < 0)
        {
            return;
        }

        Profiles.Remove(current);

        SelectedProfile = Profiles.Count == 0
            ? null
            : Profiles[Math.Clamp(index, 0, Profiles.Count - 1)];

        localSourceWatcherService.MarkDirty();
        StatusMessage = "Source removed.";
    }

    [RelayCommand]
    private async Task ApplyToSystemHostsAsync()
    {
        if (await TryEscalateWindowsActionAsync(StartupAction.ApplyManagedHosts))
        {
            return;
        }

        try
        {
            await systemHostsWorkflowService.ApplyManagedHostsAsync(Profiles, allowPrivilegePrompt: true);
            await RefreshSystemHostsSourceSnapshotAsync(announceWhenChanged: false);
            hostsStateTracker.MarkManagedApplySucceeded(Profiles);
            HasPendingElevatedHostsUpdate = false;
            StatusMessage = "Applied enabled sources to system hosts file.";
        }
        catch (UnauthorizedAccessException)
        {
            SetPendingElevatedHostsUpdate(forBackgroundApply: false);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Apply failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RestoreBackupAsync()
    {
        if (await TryEscalateWindowsActionAsync(StartupAction.RestoreBackup))
        {
            return;
        }

        try
        {
            await systemHostsWorkflowService.RestoreBackupAsync(allowPrivilegePrompt: true);
            await RefreshSystemHostsSourceSnapshotAsync(announceWhenChanged: false);
            HasPendingElevatedHostsUpdate = false;
            StatusMessage = "Hosts file restored from backup.";
        }
        catch (UnauthorizedAccessException)
        {
            SetPendingElevatedHostsUpdate(forBackgroundApply: false);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Restore failed: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanReloadLocalSource))]
    private async Task ReloadLocalSourceAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        if (SelectedProfile.SourceType != SourceType.Local || string.IsNullOrWhiteSpace(SelectedProfile.LocalPath))
        {
            StatusMessage = "Select a local source with a valid file path first.";
            return;
        }

        if (SelectedProfile.IsMissingLocalFile)
        {
            StatusMessage = $"Local source file not found: {SelectedProfile.LocalPath}";
            return;
        }

        try
        {
            SelectedProfile.Entries = await File.ReadAllTextAsync(SelectedProfile.LocalPath);
            OnPropertyChanged(nameof(SelectedProfile));
            localSourceWatcherService.MarkDirty();
            StatusMessage = "Reloaded entries from local file.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import failed: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanSaveEntriesToLocal))]
    private async Task SaveEntriesToLocalAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        if (SelectedProfile.SourceType != SourceType.Local || string.IsNullOrWhiteSpace(SelectedProfile.LocalPath))
        {
            StatusMessage = "Select a local source with a valid file path first.";
            return;
        }

        if (SelectedProfile.IsMissingLocalFile)
        {
            StatusMessage = $"Local source file not found: {SelectedProfile.LocalPath}";
            return;
        }

        try
        {
            await File.WriteAllTextAsync(SelectedProfile.LocalPath, SelectedProfile.Entries ?? string.Empty);
            SelectedProfile.LastLoadedFromDiskEntries = SelectedProfile.Entries ?? string.Empty;
            localSourceWatcherService.MarkDirty();
            await RunBackgroundManagementTickAsync();
            StatusMessage = "Saved entries to local source file.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save local failed: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanReadSelectedRemoteHosts))]
    private async Task SyncFromUrlAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        IsSelectedRemoteSyncRunning = true;
        try
        {
            var synced = await SyncProfileFromUrlAsync(SelectedProfile);
            if (synced)
            {
                await SaveConfigurationAsync();
                OnPropertyChanged(nameof(SelectedProfile));
                await RunBackgroundManagementTickAsync();
                StatusMessage = "Synced selected remote source.";
                return;
            }

            StatusMessage = "Sync skipped. Configure a valid remote source first.";
        }
        finally
        {
            IsSelectedRemoteSyncRunning = false;
        }
    }

    [RelayCommand]
    private async Task ReadSelectedRemoteHostsAsync()
    {
        await SyncFromUrlAsync();
    }

    private bool CanReadSelectedRemoteHosts() =>
        SelectedProfile is { SourceType: SourceType.Remote } && !IsSelectedRemoteSyncRunning;

    [RelayCommand]
    private async Task SyncAllFromUrlAsync()
    {
        await RefreshRemoteProfilesAsync(forceAll: true, userInitiated: true);
        await RunBackgroundManagementTickAsync();
    }

    [RelayCommand]
    private async Task LoadAzureSubscriptionsAsync()
    {
        if (IsAzureSubscriptionsLoading)
        {
            return;
        }

        IsAzureSubscriptionsLoading = true;
        StatusMessage = "Loading Azure subscriptions...";

        try
        {
            var subscriptions = await remoteSourceSyncService.ListAzureSubscriptionsAsync();
            AzureSubscriptions.Clear();
            foreach (var subscription in subscriptions)
            {
                AzureSubscriptions.Add(subscription);
            }

            SyncSelectedAzureSubscription();
            StatusMessage = subscriptions.Count == 0
                ? "No enabled Azure subscriptions found."
                : $"Loaded {subscriptions.Count} Azure subscription(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Azure connect failed: {ex.Message}";
        }
        finally
        {
            IsAzureSubscriptionsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAzureZonesAsync()
    {
        if (SelectedProfile is null ||
            SelectedProfile.SourceType != SourceType.Remote ||
            SelectedProfile.RemoteTransport != RemoteTransport.AzurePrivateDns)
        {
            StatusMessage = "Select an Azure Private DNS remote source first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedProfile.AzureSubscriptionId))
        {
            StatusMessage = "Select an Azure subscription first.";
            return;
        }

        try
        {
            await RefreshAzureZonesForProfileAsync(SelectedProfile, updateSelectedUi: true);
            StatusMessage = AzureZones.Count == 0
                ? "No Azure Private DNS zones found for this subscription."
                : $"Loaded {AzureZones.Count} Azure zone(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Loading zones failed: {ex.Message}";
        }
    }

    private bool CanDeleteProfile(HostProfile? source)
    {
        var target = source ?? SelectedProfile;
        return target is not null && !target.IsReadOnly;
    }


    private bool CanSaveSelectedSource() =>
        SelectedProfile switch
        {
            null => false,
            { SourceType: SourceType.Local, IsMissingLocalFile: true } => false,
            { SourceType: SourceType.System } => IsSystemHostsEditingEnabled,
            _ => !SelectedProfile.IsReadOnly
        };


    private async Task SaveSystemHostsDirectAsync(HostProfile profile)
    {
        if (await TryEscalateWindowsActionAsync(StartupAction.SaveRawHosts, profile.Entries ?? string.Empty))
        {
            return;
        }

        try
        {
            await systemHostsWorkflowService.SaveRawHostsAsync(profile.Entries ?? string.Empty, allowPrivilegePrompt: true);
            await RefreshSystemHostsSourceSnapshotAsync(announceWhenChanged: false);
            localSourceWatcherService.MarkDirty();
            IsSystemHostsEditingEnabled = false;
            DismissSelectedSourceExternalChangeNotification();
            HasPendingElevatedHostsUpdate = false;
            StatusMessage = "System hosts file saved.";
        }
        catch (UnauthorizedAccessException)
        {
            SetPendingElevatedHostsUpdate(forBackgroundApply: false);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save system hosts failed: {ex.Message}";
        }
    }


    private async Task RefreshRemoteProfilesAsync(bool forceAll, bool userInitiated)
    {
        if (isRefreshRunning)
        {
            return;
        }

        isRefreshRunning = true;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var syncedCount = 0;
            var errorCount = 0;

            foreach (var profile in Profiles)
            {
                if (profile.SourceType != SourceType.Remote)
                {
                    continue;
                }

                if (!CanSyncRemoteSource(profile))
                {
                    continue;
                }

                if (!forceAll && !remoteSourceSyncService.ShouldAutoRefresh(profile, now))
                {
                    continue;
                }

                try
                {
                    var synced = await SyncProfileFromUrlAsync(profile);
                    if (synced)
                    {
                        syncedCount++;
                    }
                }
                catch
                {
                    errorCount++;
                }
            }

            if (syncedCount > 0)
            {
                await SaveConfigurationAsync();
                OnPropertyChanged(nameof(SelectedProfile));
                await RunBackgroundManagementTickAsync();
            }

            if (userInitiated)
            {
                if (errorCount > 0)
                {
                    StatusMessage = $"URL sync completed with {syncedCount} update(s), {errorCount} error(s).";
                }
                else
                {
                    StatusMessage = $"Remote sync completed with {syncedCount} update(s).";
                }
            }
            else if (syncedCount > 0)
            {
                StatusMessage = $"Auto-refresh synced {syncedCount} remote source(s).";
            }
        }
        finally
        {
            isRefreshRunning = false;
        }
    }

    private async Task<bool> SyncProfileFromUrlAsync(HostProfile profile)
    {
        if (profile.RemoteTransport == RemoteTransport.AzurePrivateDns &&
            ReferenceEquals(profile, SelectedProfile))
        {
            await RefreshAzureZonesForProfileAsync(profile, updateSelectedUi: true);
        }

        return await remoteSourceSyncService.SyncProfileAsync(profile);
    }

    private bool CanSyncRemoteSource(HostProfile source)
    {
        return remoteSourceSyncService.CanSyncRemoteSource(source);
    }

    public async Task HandleRemoteSourceToggledAsync(HostProfile? source)
    {
        if (source is null || source.SourceType != SourceType.Remote || !source.IsEnabled)
        {
            return;
        }

        var synced = await SyncProfileFromUrlAsync(source);
        await SaveConfigurationAsync();

        if (ReferenceEquals(source, SelectedProfile))
        {
            OnPropertyChanged(nameof(SelectedProfile));
        }

        StatusMessage = synced
            ? $"Remote source synced on enable: {source.Name}"
            : $"Remote source enabled: {source.Name}";
    }

    public async Task SyncRemoteSourceNowAsync(HostProfile? source)
    {
        if (source is null || source.SourceType != SourceType.Remote)
        {
            return;
        }

        if (IsQuickSyncRunning)
        {
            StatusMessage = "A remote sync is already running.";
            return;
        }

        quickSyncProfileId = source.Id;
        try
        {
            var synced = await SyncProfileFromUrlAsync(source);
            if (!CanSyncRemoteSource(source))
            {
                StatusMessage = "Sync skipped. Configure a valid remote source first.";
                return;
            }

            await SaveConfigurationAsync();

            if (ReferenceEquals(source, SelectedProfile))
            {
                OnPropertyChanged(nameof(SelectedProfile));
            }

            await RunBackgroundManagementTickAsync();
            StatusMessage = synced
                ? $"Synced remote source: {source.Name}"
                : $"Remote source already up to date: {source.Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Remote sync failed: {ex.Message}";
        }
        finally
        {
            quickSyncProfileId = null;
            OnPropertyChanged(nameof(IsQuickSyncRunning));
        }
    }

    private async Task<IReadOnlyList<AzureZoneSelectionItem>> RefreshAzureZonesForProfileAsync(HostProfile profile, bool updateSelectedUi)
    {
        if (string.IsNullOrWhiteSpace(profile.AzureSubscriptionId))
        {
            if (updateSelectedUi)
            {
                ReplaceAzureZones([]);
            }

            return [];
        }

        if (updateSelectedUi)
        {
            IsAzureZonesLoading = true;
        }

        try
        {
            var selections = await remoteSourceSyncService.GetAzureZoneSelectionsAsync(profile);

            if (updateSelectedUi)
            {
                ReplaceAzureZones(selections);
            }

            return selections;
        }
        finally
        {
            if (updateSelectedUi)
            {
                IsAzureZonesLoading = false;
            }
        }
    }

    private void ReplaceAzureZones(IEnumerable<AzureZoneSelectionItem> zones)
    {
        foreach (var zone in AzureZones)
        {
            zone.PropertyChanged -= OnAzureZoneSelectionChanged;
        }

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
        {
            UpdateSelectedProfileExcludedZonesFromSelection();
        }
    }

    private void UpdateSelectedProfileExcludedZonesFromSelection()
    {
        if (SelectedProfile is null ||
            SelectedProfile.SourceType != SourceType.Remote ||
            SelectedProfile.RemoteTransport != RemoteTransport.AzurePrivateDns)
        {
            return;
        }

        SelectedProfile.AzureExcludedZones = remoteSourceSyncService.BuildExcludedZones(AzureZones);
    }

    public Task RequestImmediateReconcileAsync()
    {
        reconcileRequested = true;
        ScheduleReconcile();
        return Task.CompletedTask;
    }

    private void ScheduleReconcile()
    {
        if (isReconcileScheduled)
        {
            return;
        }

        isReconcileScheduled = true;
        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await Task.Yield();
            await RunBackgroundManagementTickAsync();
        });
    }

    private async Task RunBackgroundManagementTickAsync()
    {
        if (isManageRunning)
        {
            return;
        }

        isManageRunning = true;
        isReconcileScheduled = false;
        try
        {
            do
            {
                reconcileRequested = false;
                systemHostsRefreshPulseRequested = false;

                localSourceWatcherService.SyncWatchedSources(Profiles);

                await PersistSourcesIfChangedAsync();

                var localContentChanged = await ReloadLocalSourcesFromDiskIfNeededAsync();
                if (localContentChanged)
                {
                    OnPropertyChanged(nameof(SelectedProfile));
                    await PersistSourcesIfChangedAsync();
                }

                var needsElevatedApply = NeedsElevatedApply();
                var applyEvaluation = hostsStateTracker.EvaluateManagedApply(Profiles, managedHostsMatch: !needsElevatedApply);
                if (!needsElevatedApply)
                {
                    HasPendingElevatedHostsUpdate = false;
                    continue;
                }

                if (!applyEvaluation.ShouldApply)
                {
                    continue;
                }

                try
                {
                    await systemHostsWorkflowService.ApplyManagedHostsAsync(Profiles);
                    var systemChangedAfterApply = await RefreshSystemHostsSourceSnapshotAsync(announceWhenChanged: true);
                    hostsStateTracker.MarkManagedApplySucceeded(Profiles);
                    HasPendingElevatedHostsUpdate = false;

                    if (applyEvaluation.HadAppliedChangeBefore || localContentChanged || systemChangedAfterApply)
                    {
                        StatusMessage = "Background manager applied source changes to hosts file.";
                    }

                    if (systemHostsRefreshPulseRequested)
                    {
                        StatusMessage = "System Hosts refreshed from disk.";
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    hostsStateTracker.MarkManagedApplyAttempted(Profiles);
                    if (NeedsElevatedApply())
                    {
                        SetPendingElevatedHostsUpdate(forBackgroundApply: true);
                    }
                }
                catch (Exception ex)
                {
                    hostsStateTracker.MarkManagedApplyAttempted(Profiles);
                    StatusMessage = $"Background apply failed: {ex.Message}";
                }
            }
            while (reconcileRequested);
        }
        finally
        {
            isManageRunning = false;
            if (reconcileRequested)
            {
                ScheduleReconcile();
            }
        }
    }

    private async Task PersistSourcesIfChangedAsync()
    {
        if (!hostsStateTracker.NeedsConfigurationSave(MinimizeToTrayOnClose, RunAtStartup, Profiles))
        {
            return;
        }

        try
        {
            await SaveConfigurationAsync();
            hostsStateTracker.MarkConfigurationSaved(MinimizeToTrayOnClose, RunAtStartup, Profiles);
        }
        catch
        {
        }
    }

    private async Task<bool> ReloadLocalSourcesFromDiskIfNeededAsync()
    {
        var changed = await RefreshSystemHostsSourceSnapshotAsync(announceWhenChanged: true, skipSelectedProfile: true);

        if (!localSourceWatcherService.ConsumeDirty())
        {
            return changed;
        }

        var refreshResult = await localSourceRefreshService.RefreshLocalSourcesAsync(Profiles, SelectedProfile);
        foreach (var source in refreshResult.MissingStateChangedSources)
        {
            ApplyMissingLocalSourceStateChanged(source);
        }

        if (refreshResult.SelectedSourceWithExternalChanges is not null)
        {
            SetSelectedSourceExternalChangeNotification(refreshResult.SelectedSourceWithExternalChanges);
        }

        if (refreshResult.SelectedProfileChanged)
        {
            OnPropertyChanged(nameof(SelectedProfile));
        }

        return changed || refreshResult.AnyContentChanged;
    }

    partial void OnMinimizeToTrayOnCloseChanged(bool value)
    {
        if (isInitializing)
        {
            return;
        }

        _ = PersistSourcesIfChangedAsync();
    }

    partial void OnRunAtStartupChanged(bool value)
    {
        if (isInitializing || isUpdatingRunAtStartup)
        {
            return;
        }

        _ = ApplyRunAtStartupPreferenceAsync(value);
    }

    partial void OnSelectedProfileChanged(HostProfile? value)
    {
        if (value?.SourceType != SourceType.System && IsSystemHostsEditingEnabled)
        {
            IsSystemHostsEditingEnabled = false;
        }

        DismissSelectedSourceExternalChangeNotification();

        SyncSelectedAzureSubscription();
        _ = RefreshAzureZonesForCurrentSelectionAsync();
        OnPropertyChanged(nameof(IsSelectedSourceReadOnly));
        OnPropertyChanged(nameof(IsSelectedEntriesReadOnly));
        OnPropertyChanged(nameof(SelectedSourceTypeDisplay));
        OnPropertyChanged(nameof(IsSystemSelected));
        OnPropertyChanged(nameof(IsLocalSelected));
        OnPropertyChanged(nameof(IsRemoteSelected));
        OnPropertyChanged(nameof(IsHttpRemoteSelected));
        OnPropertyChanged(nameof(IsAzurePrivateDnsRemoteSelected));
        OnPropertyChanged(nameof(SelectedLocalFilePath));
        OnPropertyChanged(nameof(SelectedLocalFolderPath));
        OnPropertyChanged(nameof(IsSelectedLocalFileMissing));
        OnPropertyChanged(nameof(CanRefreshAzureZones));
        ReloadLocalSourceCommand.NotifyCanExecuteChanged();
        SaveEntriesToLocalCommand.NotifyCanExecuteChanged();
        OpenSelectedLocalFolderCommand.NotifyCanExecuteChanged();
        ReadSelectedRemoteHostsCommand.NotifyCanExecuteChanged();
        DeleteProfileCommand.NotifyCanExecuteChanged();
        RecreateMissingLocalFileCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedAzureSubscriptionChanged(AzureSubscriptionOption? value)
    {
        if (SelectedProfile is null || SelectedProfile.SourceType != SourceType.Remote)
        {
            return;
        }

        if (value is null)
        {
            return;
        }

        SelectedProfile.AzureSubscriptionId = value.Id;
        SelectedProfile.AzureSubscriptionName = value.Name;
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

    partial void OnIsSystemHostsEditingEnabledChanged(bool value)
    {
        if (!IsSystemSelected)
        {
            return;
        }

        StatusMessage = value
            ? "System hosts editing enabled. Review changes carefully before saving."
            : "System hosts editing disabled.";
    }

    private void SyncSelectedAzureSubscription()
    {
        if (SelectedProfile is null || SelectedProfile.SourceType != SourceType.Remote)
        {
            SelectedAzureSubscription = null;
            ReplaceAzureZones([]);
            return;
        }

        if (SelectedProfile.RemoteTransport != RemoteTransport.AzurePrivateDns)
        {
            ReplaceAzureZones([]);
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedProfile.AzureSubscriptionId))
        {
            SelectedAzureSubscription = null;
            ReplaceAzureZones([]);
            return;
        }

        var match = remoteSourceSyncService.ResolveSelectedAzureSubscription(SelectedProfile, AzureSubscriptions);

        if (match is null)
        {
            SelectedAzureSubscription = null;
            ReplaceAzureZones([]);
            return;
        }

        if (!AzureSubscriptions.Any(subscription =>
                string.Equals(subscription.Id, match.Id, StringComparison.OrdinalIgnoreCase)))
        {
            AzureSubscriptions.Insert(0, match);
        }

        SelectedAzureSubscription = match;
    }

    private async Task RefreshAzureZonesForCurrentSelectionAsync()
    {
        if (SelectedProfile is null ||
            SelectedProfile.SourceType != SourceType.Remote ||
            SelectedProfile.RemoteTransport != RemoteTransport.AzurePrivateDns ||
            string.IsNullOrWhiteSpace(SelectedProfile.AzureSubscriptionId))
        {
            ReplaceAzureZones([]);
            return;
        }

        try
        {
            await RefreshAzureZonesForProfileAsync(SelectedProfile, updateSelectedUi: true);
        }
        catch
        {
            ReplaceAzureZones([]);
        }
    }

    private IEnumerable<HostProfile> GetPersistedSources()
    {
        return Profiles.Where(source => !source.IsReadOnly);
    }

    private Task SaveConfigurationAsync()
    {
        return profileStore.SaveAsync(new AppConfig
        {
            MinimizeToTrayOnClose = MinimizeToTrayOnClose,
            RunAtStartup = RunAtStartup,
            Profiles = GetPersistedSources().ToList()
        });
    }

    private async Task EnsureRunAtStartupMatchesPreferenceAsync()
    {
        if (!startupRegistrationService.IsSupported)
        {
            return;
        }

        try
        {
            await startupRegistrationService.SetEnabledAsync(RunAtStartup);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Startup option failed: {ex.Message}";
        }
    }

    private async Task ApplyRunAtStartupPreferenceAsync(bool value)
    {
        if (!startupRegistrationService.IsSupported)
        {
            return;
        }

        try
        {
            await startupRegistrationService.SetEnabledAsync(value);
            await PersistSourcesIfChangedAsync();
            StatusMessage = value
                ? "Startup enabled. Hosts Manager will launch at Windows sign-in."
                : "Startup disabled.";
        }
        catch (Exception ex)
        {
            isUpdatingRunAtStartup = true;
            RunAtStartup = !value;
            isUpdatingRunAtStartup = false;
            StatusMessage = $"Startup option failed: {ex.Message}";
        }
    }

    private static ProcessStartInfo BuildOpenFolderStartInfo(string folder)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folder}\"",
                UseShellExecute = true
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new ProcessStartInfo
            {
                FileName = "open",
                Arguments = $"\"{folder}\"",
                UseShellExecute = false
            };
        }

        return new ProcessStartInfo
        {
            FileName = "xdg-open",
            Arguments = $"\"{folder}\"",
            UseShellExecute = false
        };
    }

    private static string GetRemoteTransportDisplay(RemoteTransport remoteTransport)
    {
        return remoteTransport switch
        {
            RemoteTransport.Http or RemoteTransport.Https => "HTTP/HTTPS",
            RemoteTransport.AzurePrivateDns => "Azure Private DNS",
            _ => remoteTransport.ToString()
        };
    }

    private async Task<bool> RefreshSystemHostsSourceSnapshotAsync(bool announceWhenChanged, bool skipSelectedProfile = false)
    {
        var systemSource = GetSystemSource();
        var refreshResult = await localSourceRefreshService.RefreshSystemSourceAsync(
            systemSource,
            SelectedProfile,
            announceWhenChanged,
            skipSelectedProfile);

        if (refreshResult.ExternalChangeDetected && systemSource is not null)
        {
            SetSelectedSourceExternalChangeNotification(systemSource);
        }

        if (refreshResult.SelectedProfileChanged)
        {
            var current = SelectedProfile;
            SelectedProfile = null;
            SelectedProfile = current;
        }

        if (refreshResult.RefreshPulseRequested)
        {
            systemHostsRefreshPulseRequested = true;
        }

        return refreshResult.Changed;
    }

    private HostProfile? GetSystemSource()
    {
        return Profiles.FirstOrDefault(source => source.SourceType == SourceType.System && source.IsReadOnly);
    }

    private void SetSelectedSourceExternalChangeNotification(HostProfile source)
    {
        SelectedSourceExternalChangeName = source.Name;
        SelectedSourceChangedExternally = true;
    }


    private void SetPendingElevatedHostsUpdate(bool forBackgroundApply)
    {
        HasPendingElevatedHostsUpdate = true;
        StatusMessage = systemHostsWorkflowService.GetPermissionDeniedMessage(forBackgroundApply);
    }

    private void InitializeManagedStateFromSystemHosts()
    {
        hostsStateTracker.InitializeManagedState(Profiles, managedHostsMatch: !NeedsElevatedApply());
        HasPendingElevatedHostsUpdate = false;
    }

    private bool NeedsElevatedApply()
    {
        return systemHostsWorkflowService.NeedsManagedApply(Profiles, GetSystemSource());
    }

    private async Task<bool> TryEscalateWindowsActionAsync(StartupAction action, string? rawHostsContent = null)
    {
        if (!systemHostsWorkflowService.CanRequestElevation())
        {
            return false;
        }

        string? payloadPath = null;
        if (action == StartupAction.SaveRawHosts)
        {
            payloadPath = await systemHostsWorkflowService.WritePendingRawHostsPayloadAsync(rawHostsContent ?? string.Empty);
        }

        var startInBackground = ShouldRelaunchElevatedInBackground();
        var relaunched = systemHostsWorkflowService.TryRelaunchElevated(action, startInBackground, payloadPath);
        if (!relaunched)
        {
            StatusMessage = "Administrator approval was canceled or failed.";
            return false;
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }

        return true;
    }

    private static bool ShouldRelaunchElevatedInBackground()
    {
        if (Program.StartInBackground)
        {
            return true;
        }

        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return false;
        }

        return desktop.MainWindow?.IsVisible != true;
    }

    private async Task ExecutePendingStartupActionAsync()
    {
        if (Program.PendingStartupAction == StartupAction.None)
        {
            return;
        }

        try
        {
            var action = Program.PendingStartupAction;
            var result = await systemHostsWorkflowService.ExecuteStartupActionAsync(
                action,
                Profiles,
                Program.StartupActionPayloadPath);

            switch (action)
            {
                case StartupAction.ApplyManagedHosts:
                    if (result.Performed)
                    {
                        await RefreshSystemHostsSourceSnapshotAsync(announceWhenChanged: false);
                        hostsStateTracker.MarkManagedApplySucceeded(Profiles);
                        HasPendingElevatedHostsUpdate = false;
                    }

                    StatusMessage = result.StatusMessage;
                    break;
                case StartupAction.RestoreBackup:
                    if (result.Performed)
                    {
                        await RefreshSystemHostsSourceSnapshotAsync(announceWhenChanged: false);
                        HasPendingElevatedHostsUpdate = false;
                    }

                    StatusMessage = result.StatusMessage;
                    break;
                case StartupAction.SaveRawHosts:
                    if (result.Performed)
                    {
                        await RefreshSystemHostsSourceSnapshotAsync(announceWhenChanged: false);
                        localSourceWatcherService.MarkDirty();
                        IsSystemHostsEditingEnabled = false;
                        DismissSelectedSourceExternalChangeNotification();
                        HasPendingElevatedHostsUpdate = false;
                    }

                    StatusMessage = result.StatusMessage;
                    break;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Startup action failed: {ex.Message}";
        }
        finally
        {
            Program.ConsumeStartupAction();
        }
    }
}


