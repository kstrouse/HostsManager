namespace HostsManager.Desktop.Tests;

public sealed class ProfileSelectionServiceTests
{
    [Fact]
    public void EvaluateSelectedProfile_ClearsAzureSelectionForNonRemoteProfiles()
    {
        var service = new ProfileSelectionService(new StubRemoteSourceSyncService());
        var profile = new HostProfile
        {
            SourceType = SourceType.Local
        };

        var change = service.EvaluateSelectedProfile(
            profile,
            isSystemHostsEditingEnabled: true,
            subscriptions: []);

        Assert.True(change.DisableSystemHostsEditing);
        Assert.True(change.ShouldUpdateSelectedAzureSubscription);
        Assert.Null(change.SelectedAzureSubscription);
        Assert.True(change.ClearAzureZones);
        Assert.False(change.RefreshAzureZones);
    }

    [Fact]
    public void EvaluateSelectedProfile_ClearsZonesButKeepsAzureSelectionUntouchedForNonAzureRemote()
    {
        var service = new ProfileSelectionService(new StubRemoteSourceSyncService());
        var profile = new HostProfile
        {
            SourceType = SourceType.Remote,
            RemoteTransport = RemoteTransport.Https
        };

        var change = service.EvaluateSelectedProfile(
            profile,
            isSystemHostsEditingEnabled: false,
            subscriptions: []);

        Assert.False(change.DisableSystemHostsEditing);
        Assert.False(change.ShouldUpdateSelectedAzureSubscription);
        Assert.True(change.ClearAzureZones);
        Assert.False(change.RefreshAzureZones);
    }

    [Fact]
    public void EvaluateSelectedProfile_UsesMatchingAzureSubscriptionAndRefreshesZones()
    {
        var matchedSubscription = new AzureSubscriptionOption { Id = "sub-1", Name = "Primary" };
        var service = new ProfileSelectionService(new StubRemoteSourceSyncService
        {
            ResolvedSubscription = matchedSubscription
        });
        var profile = new HostProfile
        {
            SourceType = SourceType.Remote,
            RemoteTransport = RemoteTransport.AzurePrivateDns,
            AzureSubscriptionId = "sub-1"
        };

        var change = service.EvaluateSelectedProfile(
            profile,
            isSystemHostsEditingEnabled: false,
            subscriptions: [matchedSubscription]);

        Assert.True(change.ShouldUpdateSelectedAzureSubscription);
        Assert.Same(matchedSubscription, change.SelectedAzureSubscription);
        Assert.Null(change.SubscriptionToInsert);
        Assert.False(change.ClearAzureZones);
        Assert.True(change.RefreshAzureZones);
    }

    [Fact]
    public void EvaluateSelectedProfile_RequestsPlaceholderInsertionForImportedAzureSubscription()
    {
        var placeholder = new AzureSubscriptionOption { Id = "sub-imported", Name = "Imported" };
        var service = new ProfileSelectionService(new StubRemoteSourceSyncService
        {
            ResolvedSubscription = placeholder
        });
        var profile = new HostProfile
        {
            SourceType = SourceType.Remote,
            RemoteTransport = RemoteTransport.AzurePrivateDns,
            AzureSubscriptionId = "sub-imported",
            AzureSubscriptionName = "Imported"
        };

        var change = service.EvaluateSelectedProfile(
            profile,
            isSystemHostsEditingEnabled: false,
            subscriptions: [new AzureSubscriptionOption { Id = "sub-1", Name = "Primary" }]);

        Assert.True(change.ShouldUpdateSelectedAzureSubscription);
        Assert.Same(placeholder, change.SelectedAzureSubscription);
        Assert.Same(placeholder, change.SubscriptionToInsert);
        Assert.True(change.RefreshAzureZones);
    }

    [Fact]
    public void CreateAzureSubscriptionSelectionChange_ReturnsAssignmentOnlyForRemoteProfileAndSelection()
    {
        var service = new ProfileSelectionService(new StubRemoteSourceSyncService());
        var selectedSubscription = new AzureSubscriptionOption { Id = "sub-1", Name = "Primary" };

        Assert.Null(service.CreateAzureSubscriptionSelectionChange(null, selectedSubscription));
        Assert.Null(service.CreateAzureSubscriptionSelectionChange(new HostProfile { SourceType = SourceType.Local }, selectedSubscription));
        Assert.Null(service.CreateAzureSubscriptionSelectionChange(new HostProfile { SourceType = SourceType.Remote }, null));

        var change = service.CreateAzureSubscriptionSelectionChange(
            new HostProfile { SourceType = SourceType.Remote },
            selectedSubscription);

        Assert.NotNull(change);
        Assert.Equal("sub-1", change.SubscriptionId);
        Assert.Equal("Primary", change.SubscriptionName);
    }

    private sealed class StubRemoteSourceSyncService : IRemoteSourceSyncService
    {
        public AzureSubscriptionOption? ResolvedSubscription { get; init; }

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
            return ResolvedSubscription;
        }
    }
}
