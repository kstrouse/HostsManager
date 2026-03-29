namespace HostsManager.Desktop.Tests;

public sealed class RemoteSyncWorkflowServiceTests
{
    [Fact]
    public async Task RefreshProfilesAsync_SyncsEligibleProfilesAndBuildsUserInitiatedStatus()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var remoteA = new HostProfile { Name = "A", SourceType = SourceType.Remote };
        var remoteB = new HostProfile { Name = "B", SourceType = SourceType.Remote };
        var local = new HostProfile { Name = "Local", SourceType = SourceType.Local };
        var syncedProfiles = new List<HostProfile>();
        var beforeSyncProfiles = new List<HostProfile>();
        var service = new RemoteSyncWorkflowService(new StubRemoteSourceSyncService
        {
            CanSyncRemoteSourceHandler = profile => !ReferenceEquals(profile, remoteB),
            ShouldAutoRefreshHandler = (_, _) => true,
            SyncProfileAsyncHandler = (profile, _) =>
            {
                syncedProfiles.Add(profile);
                return Task.FromResult(ReferenceEquals(profile, remoteA));
            }
        });

        var result = await service.RefreshProfilesAsync(
            new RemoteProfilesSyncRequest
            {
                Profiles = [remoteA, remoteB, local],
                UserInitiated = true,
                Now = DateTimeOffset.UtcNow
            },
            (profile, _) =>
            {
                beforeSyncProfiles.Add(profile);
                return Task.CompletedTask;
            },
            cancellationToken);

        Assert.Equal([remoteA], beforeSyncProfiles);
        Assert.Equal([remoteA], syncedProfiles);
        Assert.Equal(1, result.SyncedCount);
        Assert.Equal(0, result.ErrorCount);
        Assert.True(result.ShouldPersistConfiguration);
        Assert.True(result.ShouldNotifySelectedProfileChanged);
        Assert.True(result.ShouldRunBackgroundManagement);
        Assert.Equal("Remote sync completed with 1 update(s).", result.StatusMessage);
    }

    [Fact]
    public async Task RefreshProfilesAsync_CountsErrorsAndSkipsAutoRefreshWhenNotForced()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var eligible = new HostProfile { Name = "Eligible", SourceType = SourceType.Remote };
        var failing = new HostProfile { Name = "Failing", SourceType = SourceType.Remote };
        var skipped = new HostProfile { Name = "Skipped", SourceType = SourceType.Remote };
        var service = new RemoteSyncWorkflowService(new StubRemoteSourceSyncService
        {
            CanSyncRemoteSourceHandler = _ => true,
            ShouldAutoRefreshHandler = (profile, _) => !ReferenceEquals(profile, skipped),
            SyncProfileAsyncHandler = (profile, _) =>
            {
                if (ReferenceEquals(profile, failing))
                {
                    return Task.FromException<bool>(new InvalidOperationException("boom"));
                }

                return Task.FromResult(false);
            }
        });

        var result = await service.RefreshProfilesAsync(
            new RemoteProfilesSyncRequest
            {
                Profiles = [eligible, failing, skipped],
                UserInitiated = true,
                Now = DateTimeOffset.UtcNow
            },
            cancellationToken: cancellationToken);

        Assert.Equal(0, result.SyncedCount);
        Assert.Equal(1, result.ErrorCount);
        Assert.False(result.ShouldPersistConfiguration);
        Assert.Equal("URL sync completed with 0 update(s), 1 error(s).", result.StatusMessage);
    }

    [Fact]
    public async Task SyncSelectedSourceAsync_ReturnsSkipMessageWhenSyncDoesNotChange()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var profile = new HostProfile { Name = "Selected", SourceType = SourceType.Remote };
        var beforeSyncCount = 0;
        var service = new RemoteSyncWorkflowService(new StubRemoteSourceSyncService
        {
            SyncProfileAsyncHandler = (_, _) => Task.FromResult(false)
        });

        var result = await service.SyncSelectedSourceAsync(
            profile,
            (_, _) =>
            {
                beforeSyncCount++;
                return Task.CompletedTask;
            },
            cancellationToken);

        Assert.Equal(1, beforeSyncCount);
        Assert.False(result.ShouldPersistConfiguration);
        Assert.False(result.ShouldNotifySelectedProfileChanged);
        Assert.False(result.ShouldRunBackgroundManagement);
        Assert.Equal("Sync skipped. Configure a valid remote source first.", result.StatusMessage);
    }

    [Fact]
    public async Task HandleSourceEnabledAsync_PersistsAndNotifiesWhenSelectedSourceSyncs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = new HostProfile { Name = "Enabled", SourceType = SourceType.Remote, IsEnabled = true };
        var service = new RemoteSyncWorkflowService(new StubRemoteSourceSyncService
        {
            SyncProfileAsyncHandler = (_, _) => Task.FromResult(true)
        });

        var result = await service.HandleSourceEnabledAsync(source, source, cancellationToken: cancellationToken);

        Assert.True(result.ShouldPersistConfiguration);
        Assert.True(result.ShouldNotifySelectedProfileChanged);
        Assert.False(result.ShouldRunBackgroundManagement);
        Assert.Equal("Remote source synced on enable: Enabled", result.StatusMessage);
    }

    [Fact]
    public async Task SyncSourceNowAsync_ReturnsBusyMessageWhenQuickSyncAlreadyRunning()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = new HostProfile { Name = "Busy", SourceType = SourceType.Remote };
        var stub = new StubRemoteSourceSyncService();
        var service = new RemoteSyncWorkflowService(stub);

        var result = await service.SyncSourceNowAsync(
            source,
            source,
            isQuickSyncRunning: true,
            cancellationToken: cancellationToken);

        Assert.Equal("A remote sync is already running.", result.StatusMessage);
        Assert.Equal(0, stub.SyncProfileCalls);
    }

    [Fact]
    public async Task SyncSourceNowAsync_ReturnsSkipMessageWhenSourceCannotSyncAfterAttempt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = new HostProfile { Name = "Invalid", SourceType = SourceType.Remote };
        var stub = new StubRemoteSourceSyncService
        {
            SyncProfileAsyncHandler = (_, _) => Task.FromResult(false),
            CanSyncRemoteSourceHandler = _ => false
        };
        var service = new RemoteSyncWorkflowService(stub);

        var result = await service.SyncSourceNowAsync(
            source,
            selectedProfile: null,
            isQuickSyncRunning: false,
            cancellationToken: cancellationToken);

        Assert.Equal(1, stub.SyncProfileCalls);
        Assert.False(result.ShouldPersistConfiguration);
        Assert.Equal("Sync skipped. Configure a valid remote source first.", result.StatusMessage);
    }

    private sealed class StubRemoteSourceSyncService : IRemoteSourceSyncService
    {
        public int SyncProfileCalls { get; private set; }

        public Func<HostProfile, bool>? CanSyncRemoteSourceHandler { get; init; }
        public Func<HostProfile, DateTimeOffset, bool>? ShouldAutoRefreshHandler { get; init; }
        public Func<HostProfile, CancellationToken, Task<bool>>? SyncProfileAsyncHandler { get; init; }

        public Task<IReadOnlyList<AzureSubscriptionOption>> ListAzureSubscriptionsAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<AzureZoneSelectionItem>> GetAzureZoneSelectionsAsync(HostProfile profile, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> SyncProfileAsync(HostProfile profile, CancellationToken cancellationToken = default)
        {
            SyncProfileCalls++;
            return SyncProfileAsyncHandler is null
                ? Task.FromResult(false)
                : SyncProfileAsyncHandler(profile, cancellationToken);
        }

        public bool CanSyncRemoteSource(HostProfile source)
        {
            return CanSyncRemoteSourceHandler?.Invoke(source) ?? true;
        }

        public bool ShouldAutoRefresh(HostProfile profile, DateTimeOffset now)
        {
            return ShouldAutoRefreshHandler?.Invoke(profile, now) ?? true;
        }

        public string BuildExcludedZones(IEnumerable<AzureZoneSelectionItem> zones)
        {
            throw new NotSupportedException();
        }

        public AzureSubscriptionOption? ResolveSelectedAzureSubscription(HostProfile profile, IEnumerable<AzureSubscriptionOption> subscriptions)
        {
            throw new NotSupportedException();
        }
    }
}
