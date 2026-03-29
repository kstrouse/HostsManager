namespace HostsManager.Desktop.Tests;

public sealed class AzureProfileCommandServiceTests
{
    [Fact]
    public async Task LoadSubscriptionsAsync_ReturnsSubscriptionsSelectionChangeAndStatus()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var selectedProfile = new HostProfile
        {
            SourceType = SourceType.Remote,
            RemoteTransport = RemoteTransport.AzurePrivateDns,
            AzureSubscriptionId = "sub-1"
        };
        var subscriptions = new[]
        {
            new AzureSubscriptionOption { Id = "sub-1", Name = "Primary" },
            new AzureSubscriptionOption { Id = "sub-2", Name = "Secondary" }
        };
        var selectionChange = new SelectedProfileChange
        {
            ShouldUpdateSelectedAzureSubscription = true,
            SelectedAzureSubscription = subscriptions[0]
        };
        var remoteSync = new StubRemoteSourceSyncService
        {
            ListAzureSubscriptionsAsyncHandler = _ => Task.FromResult<IReadOnlyList<AzureSubscriptionOption>>(subscriptions)
        };
        var profileSelection = new StubProfileSelectionService
        {
            EvaluateSelectedProfileHandler = (_, _, _) => selectionChange
        };
        var service = new AzureProfileCommandService(remoteSync, profileSelection);

        var result = await service.LoadSubscriptionsAsync(selectedProfile, false, cancellationToken);

        Assert.Equal(2, result.Subscriptions.Count);
        Assert.Same(selectionChange, result.SelectedProfileChange);
        Assert.Equal("Loaded 2 Azure subscription(s).", result.StatusMessage);
    }

    [Fact]
    public async Task LoadSubscriptionsAsync_ReturnsFailureStatusOnException()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new AzureProfileCommandService(
            new StubRemoteSourceSyncService
            {
                ListAzureSubscriptionsAsyncHandler = _ => Task.FromException<IReadOnlyList<AzureSubscriptionOption>>(
                    new InvalidOperationException("boom"))
            },
            new StubProfileSelectionService());

        var result = await service.LoadSubscriptionsAsync(null, false, cancellationToken);

        Assert.Empty(result.Subscriptions);
        Assert.Null(result.SelectedProfileChange);
        Assert.Equal("Azure connect failed: boom", result.StatusMessage);
    }

    [Fact]
    public async Task RefreshZonesAsync_ValidatesSelectedProfileAndSubscription()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new AzureProfileCommandService(
            new StubRemoteSourceSyncService(),
            new StubProfileSelectionService());

        var notAzure = await service.RefreshZonesAsync(
            new HostProfile { SourceType = SourceType.Remote, RemoteTransport = RemoteTransport.Https },
            cancellationToken);
        var noSubscription = await service.RefreshZonesAsync(
            new HostProfile { SourceType = SourceType.Remote, RemoteTransport = RemoteTransport.AzurePrivateDns },
            cancellationToken);

        Assert.Equal("Select an Azure Private DNS remote source first.", notAzure.StatusMessage);
        Assert.False(notAzure.ShouldReplaceZones);
        Assert.Equal("Select an Azure subscription first.", noSubscription.StatusMessage);
        Assert.False(noSubscription.ShouldReplaceZones);
    }

    [Fact]
    public async Task RefreshZonesAsync_ReturnsZonesAndStatus()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var profile = new HostProfile
        {
            SourceType = SourceType.Remote,
            RemoteTransport = RemoteTransport.AzurePrivateDns,
            AzureSubscriptionId = "sub-1"
        };
        var zones = new[]
        {
            new AzureZoneSelectionItem { ZoneName = "a.internal", ResourceGroup = "rg-a" },
            new AzureZoneSelectionItem { ZoneName = "b.internal", ResourceGroup = "rg-b" }
        };
        var service = new AzureProfileCommandService(
            new StubRemoteSourceSyncService
            {
                GetAzureZoneSelectionsAsyncHandler = (_, _) => Task.FromResult<IReadOnlyList<AzureZoneSelectionItem>>(zones)
            },
            new StubProfileSelectionService());

        var result = await service.RefreshZonesAsync(profile, cancellationToken);

        Assert.True(result.ShouldReplaceZones);
        Assert.Equal(2, result.Zones.Count);
        Assert.Equal("Loaded 2 Azure zone(s).", result.StatusMessage);
    }

    [Fact]
    public async Task RefreshZonesForSelectionAsync_ClearsZonesOnInvalidProfileOrFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var invalidProfile = new HostProfile
        {
            SourceType = SourceType.Local
        };
        var failingProfile = new HostProfile
        {
            SourceType = SourceType.Remote,
            RemoteTransport = RemoteTransport.AzurePrivateDns,
            AzureSubscriptionId = "sub-1"
        };
        var service = new AzureProfileCommandService(
            new StubRemoteSourceSyncService
            {
                GetAzureZoneSelectionsAsyncHandler = (_, _) => Task.FromException<IReadOnlyList<AzureZoneSelectionItem>>(
                    new InvalidOperationException("boom"))
            },
            new StubProfileSelectionService());

        var invalid = await service.RefreshZonesForSelectionAsync(invalidProfile, cancellationToken);
        var failing = await service.RefreshZonesForSelectionAsync(failingProfile, cancellationToken);

        Assert.True(invalid.ShouldReplaceZones);
        Assert.Empty(invalid.Zones);
        Assert.Null(invalid.StatusMessage);
        Assert.True(failing.ShouldReplaceZones);
        Assert.Empty(failing.Zones);
        Assert.Null(failing.StatusMessage);
    }

    private sealed class StubRemoteSourceSyncService : IRemoteSourceSyncService
    {
        public Func<CancellationToken, Task<IReadOnlyList<AzureSubscriptionOption>>>? ListAzureSubscriptionsAsyncHandler { get; init; }
        public Func<HostProfile, CancellationToken, Task<IReadOnlyList<AzureZoneSelectionItem>>>? GetAzureZoneSelectionsAsyncHandler { get; init; }

        public Task<IReadOnlyList<AzureSubscriptionOption>> ListAzureSubscriptionsAsync(CancellationToken cancellationToken = default)
        {
            return ListAzureSubscriptionsAsyncHandler is null
                ? Task.FromResult<IReadOnlyList<AzureSubscriptionOption>>([])
                : ListAzureSubscriptionsAsyncHandler(cancellationToken);
        }

        public Task<IReadOnlyList<AzureZoneSelectionItem>> GetAzureZoneSelectionsAsync(HostProfile profile, CancellationToken cancellationToken = default)
        {
            return GetAzureZoneSelectionsAsyncHandler is null
                ? Task.FromResult<IReadOnlyList<AzureZoneSelectionItem>>([])
                : GetAzureZoneSelectionsAsyncHandler(profile, cancellationToken);
        }

        public Task<bool> SyncProfileAsync(HostProfile profile, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public bool CanSyncRemoteSource(HostProfile source)
        {
            throw new NotSupportedException();
        }

        public bool ShouldAutoRefresh(HostProfile profile, DateTimeOffset now)
        {
            throw new NotSupportedException();
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

    private sealed class StubProfileSelectionService : IProfileSelectionService
    {
        public Func<HostProfile?, bool, IEnumerable<AzureSubscriptionOption>, SelectedProfileChange>? EvaluateSelectedProfileHandler { get; init; }

        public SelectedProfileChange EvaluateSelectedProfile(HostProfile? selectedProfile, bool isSystemHostsEditingEnabled, IEnumerable<AzureSubscriptionOption> subscriptions)
        {
            return EvaluateSelectedProfileHandler?.Invoke(selectedProfile, isSystemHostsEditingEnabled, subscriptions)
                   ?? new SelectedProfileChange();
        }

        public AzureSubscriptionSelectionChange? CreateAzureSubscriptionSelectionChange(HostProfile? selectedProfile, AzureSubscriptionOption? selectedSubscription)
        {
            throw new NotSupportedException();
        }
    }
}
