using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
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
    private readonly IBackgroundManagementService backgroundManagementService;
    private readonly IBackgroundManagementCoordinator backgroundManagementCoordinator;
    private readonly IHostsStateTracker hostsStateTracker;
    private readonly IProfilePersistenceService profilePersistenceService;
    private readonly ISystemHostsCommandService systemHostsCommandService;
    private readonly IRemoteSourceSyncService remoteSourceSyncService;
    private readonly IRemoteSyncWorkflowService remoteSyncWorkflowService;
    private readonly IProfileSelectionService profileSelectionService;
    private readonly IAzureProfileCommandService azureProfileCommandService;
    private readonly IStartupActionOrchestrationService startupActionOrchestrationService;
    private readonly IStartupRegistrationService startupRegistrationService;
    private readonly ISystemHostsWorkflowService systemHostsWorkflowService;
    private readonly IUiTimer refreshTimer;
    private bool isRefreshRunning;
    private bool isInitializing;
    private bool isInitialized;
    private bool isUpdatingRunAtStartup;
    private string? quickSyncProfileId;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReloadLocalSourceCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveEntriesToLocalCommand))]
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
    [NotifyPropertyChangedFor(nameof(IsSelectedEntriesReadOnly))]
    private bool isSystemHostsEditingEnabled;

    [ObservableProperty]
    private bool selectedSourceChangedExternally;

    [ObservableProperty]
    private string selectedSourceExternalChangeName = string.Empty;

    public ObservableCollection<HostProfile> Profiles { get; } = [];
    public ObservableCollection<RemoteTransport> RemoteTransports { get; } =
    [
        RemoteTransport.Https,
        RemoteTransport.Http,
        RemoteTransport.Sftp,
        RemoteTransport.AzurePrivateDns
    ];
    public SourceListViewModel SourceList { get; }
    public SelectedSourceDetailsViewModel SelectedSourceDetails { get; }
    public LocalSourceEditorViewModel LocalEditor { get; }
    public RemoteSourceEditorViewModel RemoteEditor { get; }
    public SourceEditorPaneViewModel SourceEditor { get; }
    public StatusActionBarViewModel StatusBar { get; }

    public string HostsPath { get; }
    public bool IsSelectedEntriesReadOnly => SelectedProfile switch
    {
        null => true,
        { SourceType: SourceType.Remote } => true,
        { SourceType: SourceType.Local, IsMissingLocalFile: true } => true,
        { SourceType: SourceType.System } => !IsSystemHostsEditingEnabled,
        { IsReadOnly: true } => true,
        _ => false
    };
    public bool IsQuickSyncRunning => !string.IsNullOrWhiteSpace(quickSyncProfileId);

    public MainWindowViewModel()
        : this(
            new ProfileStore(),
            new HostsFileService(),
            new LocalSourceService(),
            null,
            new LocalSourceWatcherService(),
            null,
            new StartupRegistrationService(),
            new WindowsElevationService(),
            CreateDefaultHttpClient(),
            null,
            CreateTimer(TimeSpan.FromMinutes(1)),
            CreateTimer(TimeSpan.FromSeconds(2)),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
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
        IBackgroundManagementService? backgroundManagementService,
        IStartupRegistrationService startupRegistrationService,
        IWindowsElevationService windowsElevationService,
        HttpClient httpClient,
        IAzurePrivateDnsService? azurePrivateDnsService = null,
        IUiTimer? refreshTimer = null,
        IUiTimer? manageTimer = null,
        IBackgroundManagementCoordinator? backgroundManagementCoordinator = null,
        IHostsStateTracker? hostsStateTracker = null,
        IProfilePersistenceService? profilePersistenceService = null,
        ISystemHostsCommandService? systemHostsCommandService = null,
        ISystemHostsWorkflowService? systemHostsWorkflowService = null,
        IRemoteSourceSyncService? remoteSourceSyncService = null,
        IRemoteSyncWorkflowService? remoteSyncWorkflowService = null,
        IProfileSelectionService? profileSelectionService = null,
        IAzureProfileCommandService? azureProfileCommandService = null,
        IStartupActionOrchestrationService? startupActionOrchestrationService = null)
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
        this.profilePersistenceService = profilePersistenceService ?? new ProfilePersistenceService(profileStore, this.hostsStateTracker);
        this.systemHostsCommandService = systemHostsCommandService ?? new SystemHostsCommandService(resolvedSystemHostsWorkflowService);
        this.backgroundManagementService = backgroundManagementService ?? new BackgroundManagementService(
            profileStore,
            this.hostsStateTracker,
            localSourceWatcherService,
            this.localSourceRefreshService,
            resolvedSystemHostsWorkflowService);
        this.startupRegistrationService = startupRegistrationService;
        this.systemHostsWorkflowService = resolvedSystemHostsWorkflowService;
        var resolvedAzurePrivateDnsService = azurePrivateDnsService ?? new AzurePrivateDnsService(httpClient);
        this.remoteSourceSyncService = remoteSourceSyncService ?? new RemoteSourceSyncService(httpClient, resolvedAzurePrivateDnsService);
        this.remoteSyncWorkflowService = remoteSyncWorkflowService ?? new RemoteSyncWorkflowService(this.remoteSourceSyncService);
        this.profileSelectionService = profileSelectionService ?? new ProfileSelectionService(this.remoteSourceSyncService);
        this.azureProfileCommandService = azureProfileCommandService ?? new AzureProfileCommandService(this.remoteSourceSyncService, this.profileSelectionService);
        this.startupActionOrchestrationService = startupActionOrchestrationService ?? new StartupActionOrchestrationService(
            resolvedSystemHostsWorkflowService,
            () => Program.PendingStartupAction,
            () => Program.StartupActionPayloadPath,
            Program.ConsumeStartupAction,
            () =>
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
            },
            () =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown();
                }
            });
        this.refreshTimer = refreshTimer ?? CreateTimer(TimeSpan.FromMinutes(1));
        var resolvedManageTimer = manageTimer ?? CreateTimer(TimeSpan.FromSeconds(2));
        this.backgroundManagementCoordinator = backgroundManagementCoordinator ?? new BackgroundManagementCoordinator(
            this.backgroundManagementService,
            resolvedManageTimer,
            action => _ = Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await Task.Yield();
                await action();
            }),
            BuildBackgroundManagementRequest,
            ApplyBackgroundManagementResult);
        SourceList = new SourceListViewModel(
            this,
            localSourceService,
            localSourceWatcherService,
            source => HandleRemoteSourceToggledAsync(source),
            () => this.backgroundManagementCoordinator.RequestImmediateReconcileAsync());
        SelectedSourceDetails = new SelectedSourceDetailsViewModel(this);
        LocalEditor = new LocalSourceEditorViewModel(
            this,
            localSourceService,
            localSourceWatcherService,
            () => SaveProfilesAsync(),
            () => this.backgroundManagementCoordinator.RunNowAsync(),
            () => OnPropertyChanged(nameof(SelectedProfile)),
            () => OnPropertyChanged(nameof(IsSelectedEntriesReadOnly)),
            static startInfo => Process.Start(startInfo));
        RemoteEditor = new RemoteSourceEditorViewModel(
            this,
            this.profileSelectionService,
            this.remoteSourceSyncService,
            this.remoteSyncWorkflowService,
            this.azureProfileCommandService,
            () => this.profilePersistenceService.SaveConfigurationAsync(MinimizeToTrayOnClose, RunAtStartup, Profiles),
            () => this.backgroundManagementCoordinator.RunNowAsync(),
            () => OnPropertyChanged(nameof(SelectedProfile)));
        SourceEditor = new SourceEditorPaneViewModel(
            this,
            localSourceService,
            this.systemHostsWorkflowService,
            localSourceWatcherService,
            () => SaveProfilesAsync(),
            profile => SaveSystemHostsDirectAsync(profile),
            () => OnPropertyChanged(nameof(SelectedProfile)));
        StatusBar = new StatusActionBarViewModel(this, startupRegistrationService.IsSupported);

        this.refreshTimer.Tick += async (_, _) => await RefreshRemoteProfilesAsync(forceAll: false, userInitiated: false);

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
            return;

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
            backgroundManagementCoordinator.Start();
            localSourceWatcherService.MarkDirty();
            await backgroundManagementCoordinator.RunNowAsync();
            var hadPendingStartupAction = startupActionOrchestrationService.HasPendingStartupAction();
            await ApplyPendingStartupActionAsync();

            if (!hadPendingStartupAction)
                StatusMessage = $"Loaded {Profiles.Count} source(s).";

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
}


