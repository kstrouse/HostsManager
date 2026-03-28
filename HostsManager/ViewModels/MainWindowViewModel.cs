using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HostsManager.Models;
using HostsManager.Services;

namespace HostsManager.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ProfileStore profileStore;
    private readonly HostsFileService hostsFileService;
    private readonly StartupRegistrationService startupRegistrationService;
    private readonly WindowsElevationService windowsElevationService;
    private readonly AzurePrivateDnsService azurePrivateDnsService;
    private readonly HttpClient httpClient;
    private readonly DispatcherTimer refreshTimer;
    private readonly DispatcherTimer manageTimer;
    private readonly Dictionary<string, FileSystemWatcher> localSourceWatchers;
    private bool isRefreshRunning;
    private bool isManageRunning;
    private bool isReconcileScheduled;
    private bool reconcileRequested;
    private bool localSourcesDirty;
    private bool systemHostsRefreshPulseRequested;
    private bool isInitializing;
    private bool isInitialized;
    private bool isUpdatingRunAtStartup;
    private string? quickSyncProfileId;
    private string lastAppliedSignature;
    private string lastAttemptedSignature;
    private string lastSavedSignature;

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

    public MainWindowViewModel()
    {
        profileStore = new ProfileStore();
        hostsFileService = new HostsFileService();
        startupRegistrationService = new StartupRegistrationService();
        windowsElevationService = new WindowsElevationService();
        httpClient = new HttpClient();
        azurePrivateDnsService = new AzurePrivateDnsService(httpClient);
        localSourceWatchers = new Dictionary<string, FileSystemWatcher>(StringComparer.OrdinalIgnoreCase);
        lastAppliedSignature = string.Empty;
        lastAttemptedSignature = string.Empty;
        lastSavedSignature = string.Empty;
        localSourcesDirty = true;

        refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        refreshTimer.Tick += async (_, _) => await RefreshRemoteProfilesAsync(forceAll: false, userInitiated: false);

        manageTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        manageTimer.Tick += async (_, _) => await RunBackgroundManagementTickAsync();

        HostsPath = hostsFileService.GetHostsFilePath();
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

            var systemSource = await BuildSystemHostsSourceAsync();
            Profiles.Add(systemSource);

            foreach (var profile in loaded.Profiles)
            {
                Profiles.Add(profile);
            }

            SelectedProfile = Profiles.FirstOrDefault();
            InitializeManagedStateFromSystemHosts();
            await EnsureRunAtStartupMatchesPreferenceAsync();
            lastSavedSignature = BuildPersistenceSignature();

            refreshTimer.Start();
            manageTimer.Start();
            localSourcesDirty = true;
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
            lastSavedSignature = BuildPersistenceSignature();
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
                localSourcesDirty = true;
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
        localSourcesDirty = true;
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

        localSourcesDirty = true;
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
            await hostsFileService.ApplySourcesAsync(Profiles, allowPrivilegePrompt: true);
            await RefreshSystemHostsSourceSnapshotAsync(announceWhenChanged: false);
            var signature = BuildManagedSignature();
            lastAppliedSignature = signature;
            lastAttemptedSignature = signature;
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
            await hostsFileService.RestoreBackupAsync(allowPrivilegePrompt: true);
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

        try
        {
            SelectedProfile.Entries = await File.ReadAllTextAsync(SelectedProfile.LocalPath);
            OnPropertyChanged(nameof(SelectedProfile));
            localSourcesDirty = true;
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

        try
        {
            await File.WriteAllTextAsync(SelectedProfile.LocalPath, SelectedProfile.Entries ?? string.Empty);
            SelectedProfile.LastLoadedFromDiskEntries = SelectedProfile.Entries ?? string.Empty;
            localSourcesDirty = true;
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
            var subscriptions = await azurePrivateDnsService.ListSubscriptionsAsync();
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

    private bool CanReloadLocalSource() =>
        SelectedProfile is not null &&
        SelectedProfile.SourceType == SourceType.Local &&
        !string.IsNullOrWhiteSpace(SelectedProfile.LocalPath);

    private bool CanSaveEntriesToLocal() =>
        SelectedProfile is not null &&
        SelectedProfile.SourceType == SourceType.Local &&
        !string.IsNullOrWhiteSpace(SelectedProfile.LocalPath);

    private bool CanSaveSelectedSource() =>
        SelectedProfile switch
        {
            null => false,
            { SourceType: SourceType.System } => IsSystemHostsEditingEnabled,
            _ => !SelectedProfile.IsReadOnly
        };

    public async Task RenameSelectedLocalFileAsync(string? requestedFileName)
    {
        if (SelectedProfile is null || SelectedProfile.SourceType != SourceType.Local)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedProfile.LocalPath))
        {
            StatusMessage = "Local source path is empty.";
            return;
        }

        var requestedName = (requestedFileName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(requestedName))
        {
            StatusMessage = "Enter a file name first.";
            return;
        }

        if (requestedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            StatusMessage = "File name contains invalid characters.";
            return;
        }

        var currentPath = SelectedProfile.LocalPath;
        var directory = Path.GetDirectoryName(currentPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            StatusMessage = "Local source folder not found.";
            return;
        }

        var currentExtension = Path.GetExtension(currentPath);
        var targetFileName = requestedName;
        if (string.IsNullOrWhiteSpace(Path.GetExtension(targetFileName)) && !string.IsNullOrWhiteSpace(currentExtension))
        {
            targetFileName += currentExtension;
        }

        var targetPath = Path.Combine(directory, targetFileName);
        if (string.Equals(currentPath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "File name unchanged.";
            return;
        }

        if (File.Exists(targetPath))
        {
            StatusMessage = "A file with that name already exists.";
            return;
        }

        try
        {
            File.Move(currentPath, targetPath);
            SelectedProfile.LocalPath = targetPath;
            localSourcesDirty = true;
            OnPropertyChanged(nameof(SelectedProfile));
            OnPropertyChanged(nameof(SelectedLocalFilePath));
            OnPropertyChanged(nameof(SelectedLocalFolderPath));
            await SaveProfilesAsync();
            StatusMessage = $"Renamed local file to {Path.GetFileName(targetPath)}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Rename failed: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenSelectedLocalFolder))]
    private void OpenSelectedLocalFolder()
    {
        if (SelectedProfile is null || SelectedProfile.SourceType != SourceType.Local)
        {
            return;
        }

        try
        {
            var folder = Path.GetDirectoryName(SelectedProfile.LocalPath);
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                StatusMessage = "Local source folder not found.";
                return;
            }

            Process.Start(BuildOpenFolderStartInfo(folder));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Open folder failed: {ex.Message}";
        }
    }

    private bool CanOpenSelectedLocalFolder() =>
        SelectedProfile is { SourceType: SourceType.Local } profile &&
        !string.IsNullOrWhiteSpace(profile.LocalPath);

    private async Task SaveSystemHostsDirectAsync(HostProfile profile)
    {
        if (await TryEscalateWindowsActionAsync(StartupAction.SaveRawHosts, profile.Entries ?? string.Empty))
        {
            return;
        }

        try
        {
            await hostsFileService.SaveRawHostsFileAsync(profile.Entries ?? string.Empty, allowPrivilegePrompt: true);
            await RefreshSystemHostsSourceSnapshotAsync(announceWhenChanged: false);
            localSourcesDirty = true;
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

    public async Task AddNewLocalSourceAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var initial = "# New local hosts source" + Environment.NewLine;
            await File.WriteAllTextAsync(path, initial);

            var source = new HostProfile
            {
                Name = Path.GetFileNameWithoutExtension(path),
                IsEnabled = true,
                SourceType = SourceType.Local,
                LocalPath = path,
                Entries = initial,
                LastLoadedFromDiskEntries = initial
            };

            Profiles.Add(source);
            SelectedProfile = source;
            localSourcesDirty = true;
            StatusMessage = "Local source created and added.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Create local source failed: {ex.Message}";
        }
    }

    public async Task AddExistingLocalSourceAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var text = await File.ReadAllTextAsync(path);
            var source = new HostProfile
            {
                Name = Path.GetFileNameWithoutExtension(path),
                IsEnabled = true,
                SourceType = SourceType.Local,
                LocalPath = path,
                Entries = text,
                LastLoadedFromDiskEntries = text
            };

            Profiles.Add(source);
            SelectedProfile = source;
            localSourcesDirty = true;
            StatusMessage = "Existing local source added.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Add local source failed: {ex.Message}";
        }
    }

    public async Task<bool> ReloadSelectedSourceFromDiskAsync()
    {
        if (SelectedProfile is null)
        {
            return false;
        }

        var isFileBacked = SelectedProfile.SourceType is SourceType.Local or SourceType.System;
        if (!isFileBacked)
        {
            return false;
        }

        var changed = await TryReloadSourceFromDiskAsync(SelectedProfile);
        DismissSelectedSourceExternalChangeNotification();

        if (changed)
        {
            OnPropertyChanged(nameof(SelectedProfile));
            StatusMessage = $"Reloaded external changes for {SelectedProfile.Name}.";
        }

        return changed;
    }

    public void DismissSelectedSourceExternalChangeNotification()
    {
        SelectedSourceChangedExternally = false;
        SelectedSourceExternalChangeName = string.Empty;
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

                if (!forceAll && !ShouldAutoRefresh(profile, now))
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
        if (profile.SourceType != SourceType.Remote)
        {
            return false;
        }

        if (profile.RemoteTransport == RemoteTransport.AzurePrivateDns)
        {
            if (string.IsNullOrWhiteSpace(profile.AzureSubscriptionId))
            {
                return false;
            }

            var zoneSelections = await RefreshAzureZonesForProfileAsync(
                profile,
                updateSelectedUi: ReferenceEquals(profile, SelectedProfile));

            var enabledZones = zoneSelections
                .Where(zone => zone.IsEnabled)
                .Select(zone => new AzurePrivateDnsZoneInfo
                {
                    ZoneName = zone.ZoneName,
                    ResourceGroup = zone.ResourceGroup
                })
                .ToList();

            var azureEntries = await azurePrivateDnsService.BuildHostsEntriesAsync(
                profile.AzureSubscriptionId,
                enabledZones);

            var azureChanged = !string.Equals(profile.Entries, azureEntries, StringComparison.Ordinal);
            profile.Entries = azureEntries;
            profile.LastSyncedAtUtc = DateTimeOffset.UtcNow;
            return azureChanged;
        }

        if (!TryGetHttpUri(profile, out var uri))
        {
            return false;
        }

        var remote = await httpClient.GetStringAsync(uri);
        var changed = !string.Equals(profile.Entries, remote, StringComparison.Ordinal);

        profile.Entries = remote;
        profile.LastSyncedAtUtc = DateTimeOffset.UtcNow;

        return changed;
    }

    private static bool TryGetHttpUri(HostProfile source, out Uri? uri)
    {
        if (source.RemoteTransport is not (RemoteTransport.Http or RemoteTransport.Https))
        {
            uri = null;
            return false;
        }

        if (!Uri.TryCreate(source.RemoteLocation, UriKind.Absolute, out uri))
        {
            return false;
        }

        return uri.Scheme is "http" or "https";
    }

    private static bool CanSyncRemoteSource(HostProfile source)
    {
        if (source.SourceType != SourceType.Remote)
        {
            return false;
        }

        if (!source.IsEnabled)
        {
            return false;
        }

        if (source.RemoteTransport == RemoteTransport.AzurePrivateDns)
        {
            return !string.IsNullOrWhiteSpace(source.AzureSubscriptionId);
        }

        return TryGetHttpUri(source, out _);
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

    private static IReadOnlyCollection<string> ParseExcludedZones(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return [];
        }

        return input
            .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
            var zones = await azurePrivateDnsService.ListZonesAsync(profile.AzureSubscriptionId);
            var disabledKeys = ParseExcludedZones(profile.AzureExcludedZones)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var selections = zones.Select(zone =>
            {
                var compositeKey = BuildZoneCompositeKey(zone.ZoneName, zone.ResourceGroup);
                var isDisabled = disabledKeys.Contains(compositeKey) || disabledKeys.Contains(zone.ZoneName);
                return new AzureZoneSelectionItem
                {
                    ZoneName = zone.ZoneName,
                    ResourceGroup = zone.ResourceGroup,
                    IsEnabled = !isDisabled
                };
            }).ToList();

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

        var excluded = AzureZones
            .Where(zone => !zone.IsEnabled)
            .Select(zone => BuildZoneCompositeKey(zone.ZoneName, zone.ResourceGroup))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        SelectedProfile.AzureExcludedZones = string.Join(Environment.NewLine, excluded);
    }

    private static string BuildZoneCompositeKey(string zoneName, string resourceGroup)
    {
        return $"{zoneName}|{resourceGroup}";
    }

    private static bool ShouldAutoRefresh(HostProfile profile, DateTimeOffset now)
    {
        if (!profile.AutoRefreshFromRemote)
        {
            return false;
        }

        var interval = ParseIntervalMinutes(profile.RefreshIntervalMinutes);
        var last = profile.LastSyncedAtUtc;
        return !last.HasValue || (now - last.Value) >= TimeSpan.FromMinutes(interval);
    }

    private static int ParseIntervalMinutes(string? input)
    {
        if (!int.TryParse(input, out var value) || value < 1)
        {
            return 15;
        }

        return Math.Min(value, 1440);
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

                EnsureLocalSourceWatchers();

                await PersistSourcesIfChangedAsync();

                var localContentChanged = await ReloadLocalSourcesFromDiskIfNeededAsync();
                if (localContentChanged)
                {
                    OnPropertyChanged(nameof(SelectedProfile));
                    await PersistSourcesIfChangedAsync();
                }

                var signature = BuildManagedSignature();
                if (!NeedsElevatedApply())
                {
                    lastAppliedSignature = signature;
                    lastAttemptedSignature = signature;
                    HasPendingElevatedHostsUpdate = false;
                    continue;
                }

                if (string.Equals(signature, lastAttemptedSignature, StringComparison.Ordinal))
                {
                    continue;
                }

                var hadAppliedChangeBefore = !string.IsNullOrEmpty(lastAppliedSignature) &&
                    !string.Equals(signature, lastAppliedSignature, StringComparison.Ordinal);

                try
                {
                    await hostsFileService.ApplySourcesAsync(Profiles);
                    var systemChangedAfterApply = await RefreshSystemHostsSourceSnapshotAsync(announceWhenChanged: true);
                    lastAppliedSignature = signature;
                    lastAttemptedSignature = signature;
                    HasPendingElevatedHostsUpdate = false;

                    if (hadAppliedChangeBefore || localContentChanged || systemChangedAfterApply)
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
                    lastAttemptedSignature = signature;
                    if (NeedsElevatedApply())
                    {
                        SetPendingElevatedHostsUpdate(forBackgroundApply: true);
                    }
                }
                catch (Exception ex)
                {
                    lastAttemptedSignature = signature;
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
        var persistenceSignature = BuildPersistenceSignature();
        if (string.Equals(persistenceSignature, lastSavedSignature, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            await SaveConfigurationAsync();
            lastSavedSignature = persistenceSignature;
        }
        catch
        {
        }
    }

    private async Task<bool> ReloadLocalSourcesFromDiskIfNeededAsync()
    {
        var changed = await RefreshSystemHostsSourceSnapshotAsync(announceWhenChanged: true, skipSelectedProfile: true);

        if (!localSourcesDirty)
        {
            return changed;
        }

        localSourcesDirty = false;

        foreach (var source in Profiles)
        {
            var isFileBacked = source.SourceType == SourceType.Local;
            if (!isFileBacked || string.IsNullOrWhiteSpace(source.LocalPath))
            {
                continue;
            }

            if (ReferenceEquals(source, SelectedProfile))
            {
                if (await TryHasDiskContentChangedAsync(source))
                {
                    SetSelectedSourceExternalChangeNotification(source);
                }

                continue;
            }

            changed |= await TryReloadSourceFromDiskAsync(source);
        }

        return changed;
    }

    private void EnsureLocalSourceWatchers()
    {
        var wantedPaths = Profiles
            .Where(source => source.SourceType is SourceType.Local or SourceType.System)
            .Where(source => !string.IsNullOrWhiteSpace(source.LocalPath))
            .Select(source => NormalizePath(source.LocalPath))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toRemove = localSourceWatchers.Keys.Where(path => !wantedPaths.Contains(path)).ToList();
        foreach (var path in toRemove)
        {
            localSourceWatchers[path].Dispose();
            localSourceWatchers.Remove(path);
        }

        foreach (var path in wantedPaths)
        {
            if (localSourceWatchers.ContainsKey(path))
            {
                continue;
            }

            var directory = Path.GetDirectoryName(path);
            var fileName = Path.GetFileName(path);

            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName) || !Directory.Exists(directory))
            {
                continue;
            }

            var watcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };

            watcher.Changed += OnLocalSourceFileChanged;
            watcher.Created += OnLocalSourceFileChanged;
            watcher.Renamed += OnLocalSourceFileChanged;
            watcher.Deleted += OnLocalSourceFileChanged;

            localSourceWatchers[path] = watcher;
        }
    }

    private void OnLocalSourceFileChanged(object sender, FileSystemEventArgs e)
    {
        localSourcesDirty = true;
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    private string BuildManagedSignature()
    {
        var builder = new StringBuilder();

        foreach (var source in GetManagedSources())
        {
            builder.Append(source.Id).Append('|')
                .Append(source.IsEnabled).Append('|')
                .Append(source.SourceType).Append('|')
                .Append(source.LocalPath).Append('|')
                .Append(source.RemoteTransport).Append('|')
                .Append(source.RemoteLocation).Append('|')
                .Append(source.Entries).Append('\n');
        }

        return builder.ToString();
    }

    private string BuildPersistenceSignature()
    {
        var builder = new StringBuilder();
        builder.Append("MinimizeToTrayOnClose=").Append(MinimizeToTrayOnClose).Append('\n');
        builder.Append("RunAtStartup=").Append(RunAtStartup).Append('\n');

        foreach (var source in GetPersistedSources())
        {
            builder.Append(source.Id).Append('|')
                .Append(source.Name).Append('|')
                .Append(source.IsEnabled).Append('|')
                .Append(source.SourceType).Append('|')
                .Append(source.LocalPath).Append('|')
                .Append(source.RemoteTransport).Append('|')
                .Append(source.RemoteLocation).Append('|')
                .Append(source.AzureSubscriptionId).Append('|')
                .Append(source.AzureSubscriptionName).Append('|')
                .Append(source.AzureExcludedZones).Append('|')
                .Append(source.AutoRefreshFromRemote).Append('|')
                .Append(source.RefreshIntervalMinutes).Append('|')
                .Append(source.LastSyncedAtUtc?.ToString("O") ?? string.Empty).Append('|')
                .Append(source.Entries).Append('\n');
        }

        return builder.ToString();
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
        OnPropertyChanged(nameof(CanRefreshAzureZones));
        ReloadLocalSourceCommand.NotifyCanExecuteChanged();
        SaveEntriesToLocalCommand.NotifyCanExecuteChanged();
        OpenSelectedLocalFolderCommand.NotifyCanExecuteChanged();
        ReadSelectedRemoteHostsCommand.NotifyCanExecuteChanged();
        DeleteProfileCommand.NotifyCanExecuteChanged();
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

        var match = AzureSubscriptions.FirstOrDefault(subscription =>
            string.Equals(subscription.Id, SelectedProfile.AzureSubscriptionId, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            match = new AzureSubscriptionOption
            {
                Id = SelectedProfile.AzureSubscriptionId,
                Name = SelectedProfile.AzureSubscriptionName
            };

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

    private IEnumerable<HostProfile> GetManagedSources()
    {
        return Profiles.Where(source => !source.IsReadOnly);
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

    private static string GetHostsPermissionDeniedMessage(bool forBackgroundApply)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return forBackgroundApply
                ? "Background manager skipped hosts-file updates. Use Apply Hosts Now and approve the macOS admin prompt."
                : "Administrative access is required to modify /etc/hosts on macOS. Approve the macOS admin prompt and try again.";
        }

        return forBackgroundApply
            ? "Pending hosts changes need administrator approval. Click Apply to elevate."
            : "Administrator approval is required to modify the hosts file.";
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

    private async Task<HostProfile> BuildSystemHostsSourceAsync()
    {
        string text;
        try
        {
            text = await File.ReadAllTextAsync(HostsPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            text = "# Unable to read system hosts file (permission denied)." + Environment.NewLine +
                   "# " + ex.Message;
        }
        catch (Exception ex)
        {
            text = "# Unable to read system hosts file." + Environment.NewLine +
                   "# " + ex.Message;
        }

        return new HostProfile
        {
            Id = "system-hosts-source",
            Name = "System Hosts",
            IsEnabled = true,
            SourceType = SourceType.System,
            LocalPath = HostsPath,
            Entries = text,
            IsReadOnly = true,
            LastLoadedFromDiskEntries = text
        };
    }

    private async Task<bool> RefreshSystemHostsSourceSnapshotAsync(bool announceWhenChanged, bool skipSelectedProfile = false)
    {
        var systemSource = GetSystemSource();
        if (systemSource is null)
        {
            return false;
        }

        if (skipSelectedProfile && ReferenceEquals(SelectedProfile, systemSource))
        {
            if (await TryHasDiskContentChangedAsync(systemSource))
            {
                SetSelectedSourceExternalChangeNotification(systemSource);
            }

            return false;
        }

        var changed = await TryReloadSourceFromDiskAsync(systemSource);
        if (changed && ReferenceEquals(SelectedProfile, systemSource))
        {
            var current = SelectedProfile;
            SelectedProfile = null;
            SelectedProfile = current;

            if (announceWhenChanged)
            {
                systemHostsRefreshPulseRequested = true;
            }
        }

        return changed;
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

    private static async Task<bool> TryHasDiskContentChangedAsync(HostProfile source)
    {
        if (string.IsNullOrWhiteSpace(source.LocalPath) || !File.Exists(source.LocalPath))
        {
            return false;
        }

        try
        {
            var text = await File.ReadAllTextAsync(source.LocalPath);
            var baseline = source.LastLoadedFromDiskEntries;
            if (baseline is null)
            {
                baseline = source.Entries;
            }

            return !string.Equals(text, baseline, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> TryReloadSourceFromDiskAsync(HostProfile source)
    {
        if (string.IsNullOrWhiteSpace(source.LocalPath) || !File.Exists(source.LocalPath))
        {
            return false;
        }

        try
        {
            var text = await File.ReadAllTextAsync(source.LocalPath);
            source.LastLoadedFromDiskEntries = text;
            if (string.Equals(text, source.Entries, StringComparison.Ordinal))
            {
                return false;
            }

            source.Entries = text;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void SetPendingElevatedHostsUpdate(bool forBackgroundApply)
    {
        HasPendingElevatedHostsUpdate = true;
        StatusMessage = GetHostsPermissionDeniedMessage(forBackgroundApply);
    }

    private void InitializeManagedStateFromSystemHosts()
    {
        if (NeedsElevatedApply())
        {
            HasPendingElevatedHostsUpdate = false;
            return;
        }

        var signature = BuildManagedSignature();
        lastAppliedSignature = signature;
        lastAttemptedSignature = signature;
        HasPendingElevatedHostsUpdate = false;
    }

    private bool NeedsElevatedApply()
    {
        var systemSource = GetSystemSource();
        if (systemSource is null)
        {
            return true;
        }

        return !hostsFileService.ManagedHostsMatch(Profiles, systemSource.Entries);
    }

    private async Task<bool> TryEscalateWindowsActionAsync(StartupAction action, string? rawHostsContent = null)
    {
        if (!windowsElevationService.IsSupported || windowsElevationService.IsProcessElevated())
        {
            return false;
        }

        string? payloadPath = null;
        if (action == StartupAction.SaveRawHosts)
        {
            payloadPath = await WritePendingRawHostsPayloadAsync(rawHostsContent ?? string.Empty);
        }

        var startInBackground = ShouldRelaunchElevatedInBackground();
        var relaunched = windowsElevationService.TryRelaunchElevated(action, payloadPath, startInBackground);
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

    private async Task<string> WritePendingRawHostsPayloadAsync(string content)
    {
        Directory.CreateDirectory(profileStore.ConfigDirectory);
        var payloadPath = Path.Combine(profileStore.ConfigDirectory, "pending-system-hosts.txt");
        await File.WriteAllTextAsync(payloadPath, content);
        return payloadPath;
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
            switch (Program.PendingStartupAction)
            {
                case StartupAction.ApplyManagedHosts:
                    await hostsFileService.ApplySourcesAsync(Profiles);
                    await RefreshSystemHostsSourceSnapshotAsync(announceWhenChanged: false);
                    lastAppliedSignature = BuildManagedSignature();
                    lastAttemptedSignature = lastAppliedSignature;
                    HasPendingElevatedHostsUpdate = false;
                    StatusMessage = "Applied enabled sources to system hosts file.";
                    break;
                case StartupAction.RestoreBackup:
                    await hostsFileService.RestoreBackupAsync();
                    await RefreshSystemHostsSourceSnapshotAsync(announceWhenChanged: false);
                    HasPendingElevatedHostsUpdate = false;
                    StatusMessage = "Hosts file restored from backup.";
                    break;
                case StartupAction.SaveRawHosts:
                    if (string.IsNullOrWhiteSpace(Program.StartupActionPayloadPath) ||
                        !File.Exists(Program.StartupActionPayloadPath))
                    {
                        StatusMessage = "Pending system hosts content was not found.";
                        break;
                    }

                    var content = await File.ReadAllTextAsync(Program.StartupActionPayloadPath);
                    await hostsFileService.SaveRawHostsFileAsync(content);
                    await RefreshSystemHostsSourceSnapshotAsync(announceWhenChanged: false);
                    localSourcesDirty = true;
                    IsSystemHostsEditingEnabled = false;
                    DismissSelectedSourceExternalChangeNotification();
                    HasPendingElevatedHostsUpdate = false;
                    StatusMessage = "System hosts file saved.";

                    try
                    {
                        File.Delete(Program.StartupActionPayloadPath);
                    }
                    catch
                    {
                    }

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
