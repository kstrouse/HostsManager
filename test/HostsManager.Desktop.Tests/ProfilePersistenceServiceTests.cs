namespace HostsManager.Desktop.Tests;

public sealed class ProfilePersistenceServiceTests
{
    [Fact]
    public async Task SaveConfigurationAsync_PersistsOnlyWritableProfiles()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new TrackingProfileStore();
        var tracker = new TrackingHostsStateTracker();
        var service = new ProfilePersistenceService(store, tracker);
        var profiles = CreateProfiles();

        await service.SaveConfigurationAsync(
            minimizeToTrayOnClose: true,
            runAtStartup: false,
            profiles,
            cancellationToken);

        var saved = Assert.Single(store.SavedConfigs);
        Assert.True(saved.MinimizeToTrayOnClose);
        Assert.False(saved.RunAtStartup);
        Assert.Single(saved.Profiles);
        Assert.Equal("local-1", saved.Profiles[0].Id);
        Assert.Equal(0, tracker.MarkConfigurationSavedCalls);
    }

    [Fact]
    public async Task SaveConfigurationAndMarkSavedAsync_PersistsAndMarksTracker()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new TrackingProfileStore();
        var tracker = new TrackingHostsStateTracker();
        var service = new ProfilePersistenceService(store, tracker);
        var profiles = CreateProfiles();

        await service.SaveConfigurationAndMarkSavedAsync(
            minimizeToTrayOnClose: false,
            runAtStartup: true,
            profiles,
            cancellationToken);

        Assert.Single(store.SavedConfigs);
        Assert.Equal(1, tracker.MarkConfigurationSavedCalls);
        Assert.True(tracker.LastRunAtStartup);
        Assert.Equal(2, tracker.LastMarkedProfiles.Count);
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
                IsReadOnly = true
            },
            new HostProfile
            {
                Id = "local-1",
                Name = "Local One",
                SourceType = SourceType.Local,
                LocalPath = "C:/temp/local.hosts"
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
            throw new NotSupportedException();
        }

        public Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default)
        {
            SavedConfigs.Add(config);
            return Task.CompletedTask;
        }
    }

    private sealed class TrackingHostsStateTracker : IHostsStateTracker
    {
        public int MarkConfigurationSavedCalls { get; private set; }
        public bool LastMinimizeToTrayOnClose { get; private set; }
        public bool LastRunAtStartup { get; private set; }
        public IReadOnlyList<HostProfile> LastMarkedProfiles { get; private set; } = [];

        public void MarkConfigurationSaved(bool minimizeToTrayOnClose, bool runAtStartup, IEnumerable<HostProfile> sources)
        {
            MarkConfigurationSavedCalls++;
            LastMinimizeToTrayOnClose = minimizeToTrayOnClose;
            LastRunAtStartup = runAtStartup;
            LastMarkedProfiles = sources.ToList();
        }

        public bool NeedsConfigurationSave(bool minimizeToTrayOnClose, bool runAtStartup, IEnumerable<HostProfile> sources)
        {
            throw new NotSupportedException();
        }

        public void InitializeManagedState(IEnumerable<HostProfile> sources, bool managedHostsMatch)
        {
            throw new NotSupportedException();
        }

        public ManagedApplyEvaluation EvaluateManagedApply(IEnumerable<HostProfile> sources, bool managedHostsMatch)
        {
            throw new NotSupportedException();
        }

        public void MarkManagedApplySucceeded(IEnumerable<HostProfile> sources)
        {
            throw new NotSupportedException();
        }

        public void MarkManagedApplyAttempted(IEnumerable<HostProfile> sources)
        {
            throw new NotSupportedException();
        }
    }
}
