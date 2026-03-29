namespace HostsManager.Desktop.Tests;

public sealed class BackgroundManagementServiceTests
{
    [Fact]
    public async Task PersistConfigurationIfChangedAsync_SavesOnlyWhenNeeded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new TrackingProfileStore();
        using var watcher = new LocalSourceWatcherService(new LocalSourceWatcherServiceTestsFactory());
        var tracker = new HostsStateTracker();
        var workflow = new StubSystemHostsWorkflowService();
        var service = new BackgroundManagementService(
            store,
            tracker,
            watcher,
            new StubLocalSourceRefreshService(),
            workflow);
        var profiles = CreateProfiles();

        var first = await service.PersistConfigurationIfChangedAsync(false, false, profiles, cancellationToken);
        var second = await service.PersistConfigurationIfChangedAsync(false, false, profiles, cancellationToken);

        Assert.True(first);
        Assert.False(second);
        Assert.Single(store.SavedConfigs);
    }

    [Fact]
    public async Task RunPassAsync_ReturnsRefreshChangesAndClearsPendingWhenNoApplyNeeded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var systemSource = CreateProfiles()[0];
        var selectedLocal = CreateProfiles()[1];
        var store = new TrackingProfileStore();
        using var watcher = new LocalSourceWatcherService(new LocalSourceWatcherServiceTestsFactory());
        var refresh = new StubLocalSourceRefreshService
        {
            LocalRefreshResult = new LocalSourceRefreshResult
            {
                AnyContentChanged = true,
                SelectedProfileChanged = true,
                SelectedSourceWithExternalChanges = selectedLocal,
                MissingStateChangedSources = [selectedLocal]
            }
        };
        refresh.SystemRefreshResults.Enqueue(new SystemSourceRefreshResult(false, false, true, false));
        var service = new BackgroundManagementService(
            store,
            new HostsStateTracker(),
            watcher,
            refresh,
            new StubSystemHostsWorkflowService { NeedsManagedApplyResult = false });

        var result = await service.RunPassAsync(new BackgroundManagementRequest
        {
            Profiles = [systemSource, selectedLocal],
            SelectedProfile = selectedLocal
        }, cancellationToken);

        Assert.True(result.SelectedProfileChanged);
        Assert.False(result.HasPendingElevatedHostsUpdate);
        Assert.Same(systemSource, result.SourceWithExternalChanges);
        Assert.Single(result.MissingStateChangedSources);
        Assert.Equal(1, refresh.LocalRefreshCalls);
        Assert.Single(store.SavedConfigs);
    }

    [Fact]
    public async Task RunPassAsync_ReturnsPendingStatusWhenApplyNeedsElevation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var profiles = CreateProfiles();
        var workflow = new StubSystemHostsWorkflowService
        {
            NeedsManagedApplyResult = true,
            ApplyException = new UnauthorizedAccessException("denied")
        };
        using var watcher = new LocalSourceWatcherService(new LocalSourceWatcherServiceTestsFactory());
        watcher.ConsumeDirty();
        var tracker = new HostsStateTracker();
        tracker.InitializeManagedState(profiles, managedHostsMatch: true);
        profiles[1].Entries = "10.0.0.5 changed.remote";
        var service = new BackgroundManagementService(
            new TrackingProfileStore(),
            tracker,
            watcher,
            CreateRefreshServiceWithSystemResults(new SystemSourceRefreshResult()),
            workflow);

        var result = await service.RunPassAsync(new BackgroundManagementRequest
        {
            Profiles = profiles,
            SelectedProfile = profiles[0]
        }, cancellationToken);

        Assert.True(result.HasPendingElevatedHostsUpdate);
        Assert.Equal(workflow.GetPermissionDeniedMessage(true), result.StatusMessage);
    }

    [Fact]
    public async Task RunPassAsync_AppliesManagedHostsAndReportsStatus()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var profiles = CreateProfiles();
        using var watcher = new LocalSourceWatcherService(new LocalSourceWatcherServiceTestsFactory());
        watcher.ConsumeDirty();
        var tracker = new HostsStateTracker();
        tracker.InitializeManagedState(profiles, managedHostsMatch: true);
        profiles[1].Entries = "10.0.0.5 changed.remote";
        var refresh = CreateRefreshServiceWithSystemResults(
            new SystemSourceRefreshResult(),
            new SystemSourceRefreshResult(true, true, false, false));
        var workflow = new StubSystemHostsWorkflowService { NeedsManagedApplyResult = true };
        var service = new BackgroundManagementService(
            new TrackingProfileStore(),
            tracker,
            watcher,
            refresh,
            workflow);

        var result = await service.RunPassAsync(new BackgroundManagementRequest
        {
            Profiles = profiles,
            SelectedProfile = profiles[0]
        }, cancellationToken);

        Assert.True(workflow.ApplyCalled);
        Assert.True(result.SelectedProfileChanged);
        Assert.False(result.HasPendingElevatedHostsUpdate);
        Assert.Equal("Background manager applied source changes to hosts file.", result.StatusMessage);
    }

    private static List<HostProfile> CreateProfiles()
    {
        return
        [
            new HostProfile
            {
                Id = "system-hosts-source",
                Name = "System Hosts",
                SourceType = SourceType.System,
                IsReadOnly = true,
                LocalPath = "C:/temp/hosts",
                Entries = "127.0.0.1 localhost"
            },
            new HostProfile
            {
                Id = "local-1",
                Name = "Local One",
                SourceType = SourceType.Local,
                LocalPath = "C:/temp/local.hosts",
                Entries = "10.0.0.1 local.test"
            }
        ];
    }

    private sealed class TrackingProfileStore : IProfileStore
    {
        public string ConfigDirectory => "C:/temp/config";
        public string ProfilesFilePath => "C:/temp/config/profiles.json";
        public List<AppConfig> SavedConfigs { get; } = [];

        public Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AppConfig());
        }

        public Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default)
        {
            SavedConfigs.Add(config);
            return Task.CompletedTask;
        }
    }

    private sealed class StubLocalSourceRefreshService : ILocalSourceRefreshService
    {
        public Queue<SystemSourceRefreshResult> SystemRefreshResults { get; } = new();
        public LocalSourceRefreshResult LocalRefreshResult { get; set; } = new();
        public int LocalRefreshCalls { get; private set; }

        public Task<SystemSourceRefreshResult> RefreshSystemSourceAsync(HostProfile? systemSource, HostProfile? selectedProfile, bool announceWhenChanged, bool skipSelectedProfile, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SystemRefreshResults.Count > 0 ? SystemRefreshResults.Dequeue() : default);
        }

        public Task<LocalSourceRefreshResult> RefreshLocalSourcesAsync(IEnumerable<HostProfile> sources, HostProfile? selectedProfile, CancellationToken cancellationToken = default)
        {
            LocalRefreshCalls++;
            return Task.FromResult(LocalRefreshResult);
        }
    }

    private sealed class StubSystemHostsWorkflowService : ISystemHostsWorkflowService
    {
        public bool NeedsManagedApplyResult { get; set; }
        public Exception? ApplyException { get; set; }
        public bool ApplyCalled { get; private set; }

        public string GetHostsFilePath() => "C:/temp/hosts";
        public Task<HostProfile> BuildSystemSourceAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasSystemSourceChangedAsync(HostProfile systemSource, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ReloadSystemSourceAsync(HostProfile systemSource, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task ApplyManagedHostsAsync(IEnumerable<HostProfile> sources, bool allowPrivilegePrompt = false, CancellationToken cancellationToken = default)
        {
            ApplyCalled = true;
            return ApplyException is null ? Task.CompletedTask : Task.FromException(ApplyException);
        }

        public Task RestoreBackupAsync(bool allowPrivilegePrompt = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveRawHostsAsync(string content, bool allowPrivilegePrompt = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public bool NeedsManagedApply(IEnumerable<HostProfile> sources, HostProfile? systemSource) => NeedsManagedApplyResult;
        public string GetPermissionDeniedMessage(bool forBackgroundApply) => "Pending hosts changes need administrator approval. Click Apply to elevate.";
        public bool CanRequestElevation() => false;
        public bool TryRelaunchElevated(StartupAction action, bool startInBackground, string? payloadPath = null) => false;
        public Task<string> WritePendingRawHostsPayloadAsync(string content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StartupActionExecutionResult> ExecuteStartupActionAsync(StartupAction action, IEnumerable<HostProfile> sources, string? payloadPath = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class LocalSourceWatcherServiceTestsFactory : LocalSourceWatcherService.IFileSystemWatcherFactory
    {
        public LocalSourceWatcherService.IFileSystemWatcherAdapter Create(string directory, string fileName)
        {
            return new NoOpWatcher();
        }
    }

    private sealed class NoOpWatcher : LocalSourceWatcherService.IFileSystemWatcherAdapter
    {
        public event FileSystemEventHandler? Changed
        {
            add { }
            remove { }
        }

        public event FileSystemEventHandler? Created
        {
            add { }
            remove { }
        }

        public event RenamedEventHandler? Renamed
        {
            add { }
            remove { }
        }

        public event FileSystemEventHandler? Deleted
        {
            add { }
            remove { }
        }

        public void Dispose()
        {
        }
    }

    private static StubLocalSourceRefreshService CreateRefreshServiceWithSystemResults(params SystemSourceRefreshResult[] results)
    {
        var service = new StubLocalSourceRefreshService();
        foreach (var result in results)
        {
            service.SystemRefreshResults.Enqueue(result);
        }

        return service;
    }
}
