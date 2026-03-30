namespace HostsManager.Desktop.Tests;

public sealed class RemoteSourceSyncServiceTests
{
    [Fact]
    public void CanSyncRemoteSource_ValidatesSupportedRemoteProfiles()
    {
        var service = CreateService();

        var validHttp = new HostProfile
        {
            SourceType = SourceType.Remote,
            IsEnabled = true,
            RemoteTransport = RemoteTransport.Https,
            RemoteLocation = "https://example.test/hosts"
        };
        var invalidHttp = new HostProfile
        {
            SourceType = SourceType.Remote,
            IsEnabled = true,
            RemoteTransport = RemoteTransport.Https,
            RemoteLocation = "not-a-uri"
        };
        var disabledHttp = new HostProfile
        {
            SourceType = SourceType.Remote,
            IsEnabled = false,
            RemoteTransport = RemoteTransport.Https,
            RemoteLocation = "https://example.test/hosts"
        };
        var azure = new HostProfile
        {
            SourceType = SourceType.Remote,
            IsEnabled = true,
            RemoteTransport = RemoteTransport.AzurePrivateDns,
            AzureSubscriptionId = "sub-1"
        };

        Assert.True(service.CanSyncRemoteSource(validHttp));
        Assert.False(service.CanSyncRemoteSource(invalidHttp));
        Assert.False(service.CanSyncRemoteSource(disabledHttp));
        Assert.True(service.CanSyncRemoteSource(azure));
    }

    [Fact]
    public void ShouldAutoRefresh_UsesDefaultAndMaximumIntervalBounds()
    {
        var service = CreateService();
        var now = DateTimeOffset.UtcNow;
        var profile = new HostProfile
        {
            SourceType = SourceType.Remote,
            AutoRefreshFromRemote = true,
            RefreshIntervalMinutes = "invalid",
            LastSyncedAtUtc = now.AddMinutes(-14)
        };

        Assert.False(service.ShouldAutoRefresh(profile, now));

        profile.LastSyncedAtUtc = now.AddMinutes(-15);
        Assert.True(service.ShouldAutoRefresh(profile, now));

        profile.RefreshIntervalMinutes = "99999";
        profile.LastSyncedAtUtc = now.AddMinutes(-1439);
        Assert.False(service.ShouldAutoRefresh(profile, now));

        profile.LastSyncedAtUtc = now.AddMinutes(-1440);
        Assert.True(service.ShouldAutoRefresh(profile, now));
    }

    [Fact]
    public async Task GetAzureZoneSelectionsAsync_DisablesZonesFromProfileExclusions()
    {
        var azure = new TrackingAzurePrivateDnsService
        {
            Zones =
            [
                new AzurePrivateDnsZoneInfo { ZoneName = "a.internal", ResourceGroup = "rg-a" },
                new AzurePrivateDnsZoneInfo { ZoneName = "b.internal", ResourceGroup = "rg-b" },
                new AzurePrivateDnsZoneInfo { ZoneName = "c.internal", ResourceGroup = "rg-c" }
            ]
        };
        var service = CreateService(azureService: azure);
        var profile = new HostProfile
        {
            SourceType = SourceType.Remote,
            RemoteTransport = RemoteTransport.AzurePrivateDns,
            AzureSubscriptionId = "sub-1",
            AzureExcludedZones = "a.internal|rg-a\nb.internal"
        };

        var cancellationToken = TestContext.Current.CancellationToken;
        var selections = await service.GetAzureZoneSelectionsAsync(profile, cancellationToken);

        Assert.Equal("sub-1", azure.LastListedZonesSubscriptionId);
        Assert.False(selections[0].IsEnabled);
        Assert.False(selections[1].IsEnabled);
        Assert.True(selections[2].IsEnabled);
    }

    [Fact]
    public void BuildExcludedZones_UsesDisabledCompositeKeysOnce()
    {
        var service = CreateService();
        var zones = new[]
        {
            new AzureZoneSelectionItem { ZoneName = "a.internal", ResourceGroup = "rg-a", IsEnabled = false },
            new AzureZoneSelectionItem { ZoneName = "A.internal", ResourceGroup = "rg-a", IsEnabled = false },
            new AzureZoneSelectionItem { ZoneName = "b.internal", ResourceGroup = "rg-b", IsEnabled = true }
        };

        var excluded = service.BuildExcludedZones(zones);

        Assert.Equal("a.internal|rg-a", excluded);
    }

    [Fact]
    public void ResolveSelectedAzureSubscription_ReturnsMatchOrPlaceholder()
    {
        var service = CreateService();
        var subscriptions = new[]
        {
            new AzureSubscriptionOption { Id = "sub-1", Name = "Primary" }
        };
        var matchedProfile = new HostProfile
        {
            SourceType = SourceType.Remote,
            RemoteTransport = RemoteTransport.AzurePrivateDns,
            AzureSubscriptionId = "sub-1"
        };
        var placeholderProfile = new HostProfile
        {
            SourceType = SourceType.Remote,
            RemoteTransport = RemoteTransport.AzurePrivateDns,
            AzureSubscriptionId = "sub-2",
            AzureSubscriptionName = "Imported"
        };

        var matched = service.ResolveSelectedAzureSubscription(matchedProfile, subscriptions);
        var placeholder = service.ResolveSelectedAzureSubscription(placeholderProfile, subscriptions);

        Assert.Same(subscriptions[0], matched);
        Assert.Equal("sub-2", placeholder?.Id);
        Assert.Equal("Imported", placeholder?.Name);
    }

    [Fact]
    public async Task SyncProfileAsync_HttpUpdatesEntriesAndTimestamp()
    {
        Uri? requestedUri = null;
        var service = CreateService(
            httpClient: new HttpClient(new StubHttpMessageHandler(request =>
            {
                requestedUri = request.RequestUri;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("10.0.0.5 service.test\n", Encoding.UTF8)
                };
            })));
        var profile = new HostProfile
        {
            SourceType = SourceType.Remote,
            RemoteTransport = RemoteTransport.Https,
            RemoteLocation = "https://example.test/hosts",
            Entries = "10.0.0.1 before.test\n"
        };

        var cancellationToken = TestContext.Current.CancellationToken;
        var changed = await service.SyncProfileAsync(profile, cancellationToken);

        Assert.True(changed);
        Assert.Equal(new Uri("https://example.test/hosts"), requestedUri);
        Assert.Equal("10.0.0.5 service.test\n", profile.Entries.Replace("\r\n", "\n"));
        Assert.NotNull(profile.LastSyncedAtUtc);
    }

    [Fact]
    public async Task SyncProfileAsync_AzureBuildsEntriesFromEnabledZonesOnly()
    {
        var azure = new TrackingAzurePrivateDnsService
        {
            Zones =
            [
                new AzurePrivateDnsZoneInfo { ZoneName = "include.internal", ResourceGroup = "rg-a" },
                new AzurePrivateDnsZoneInfo { ZoneName = "skip.internal", ResourceGroup = "rg-b" }
            ],
            HostsEntries = "10.10.0.5 include.internal\n"
        };
        var service = CreateService(azureService: azure);
        var profile = new HostProfile
        {
            SourceType = SourceType.Remote,
            RemoteTransport = RemoteTransport.AzurePrivateDns,
            AzureSubscriptionId = "sub-azure",
            AzureExcludedZones = "skip.internal|rg-b",
            AzureStripPrivatelinkSubdomain = true,
            Entries = "old"
        };

        var cancellationToken = TestContext.Current.CancellationToken;
        var changed = await service.SyncProfileAsync(profile, cancellationToken);

        Assert.True(changed);
        Assert.Equal("sub-azure", azure.LastBuiltHostsSubscriptionId);
        Assert.Single(azure.LastBuiltHostsZones);
        Assert.Equal("include.internal", azure.LastBuiltHostsZones[0].ZoneName);
        Assert.True(azure.LastStripPrivatelinkSubdomain);
        Assert.Equal("10.10.0.5 include.internal\n", profile.Entries.Replace("\r\n", "\n"));
        Assert.NotNull(profile.LastSyncedAtUtc);
    }

    private static RemoteSourceSyncService CreateService(
        TrackingAzurePrivateDnsService? azureService = null,
        HttpClient? httpClient = null)
    {
        return new RemoteSourceSyncService(
            httpClient ?? new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("127.0.0.1 default.test\n", Encoding.UTF8)
            })),
            azureService ?? new TrackingAzurePrivateDnsService());
    }

    private sealed class TrackingAzurePrivateDnsService : IAzurePrivateDnsService
    {
        public IReadOnlyList<AzureSubscriptionOption> Subscriptions { get; set; } = [];
        public IReadOnlyList<AzurePrivateDnsZoneInfo> Zones { get; set; } = [];
        public string HostsEntries { get; set; } = string.Empty;
        public string? LastListedZonesSubscriptionId { get; private set; }
        public string? LastBuiltHostsSubscriptionId { get; private set; }
        public IReadOnlyList<AzurePrivateDnsZoneInfo> LastBuiltHostsZones { get; private set; } = [];
        public bool LastStripPrivatelinkSubdomain { get; private set; }

        public Task<IReadOnlyList<AzureSubscriptionOption>> ListSubscriptionsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Subscriptions);
        }

        public Task<IReadOnlyList<AzurePrivateDnsZoneInfo>> ListZonesAsync(string subscriptionId, CancellationToken cancellationToken = default)
        {
            LastListedZonesSubscriptionId = subscriptionId;
            return Task.FromResult(Zones);
        }

        public Task<string> BuildHostsEntriesAsync(
            string subscriptionId,
            IEnumerable<AzurePrivateDnsZoneInfo> includedZones,
            bool stripPrivatelinkSubdomain = false,
            CancellationToken cancellationToken = default)
        {
            LastBuiltHostsSubscriptionId = subscriptionId;
            LastBuiltHostsZones = includedZones.ToArray();
            LastStripPrivatelinkSubdomain = stripPrivatelinkSubdomain;
            return Task.FromResult(HostsEntries);
        }
    }
}

