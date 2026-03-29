namespace HostsManager.Desktop.Tests;

public sealed class RemoteSourceEditorViewModelTests
{
    [Fact]
    public async Task LoadAzureSubscriptionsCommand_LoadsSubscriptionsIntoChildViewModel()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        await File.WriteAllTextAsync(hostsPath, "127.0.0.1 localhost\n", cancellationToken);
        var azure = new FakeAzurePrivateDnsService
        {
            Subscriptions =
            [
                new AzureSubscriptionOption { Id = "sub-1", Name = "Primary" },
                new AzureSubscriptionOption { Id = "sub-2", Name = "Secondary" }
            ]
        };
        var vm = CreateViewModel(tempDir.Path, hostsPath, azureService: azure);

        await vm.RemoteEditor.LoadAzureSubscriptionsCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.RemoteEditor.AzureSubscriptions.Count);
        Assert.Equal("Loaded 2 Azure subscription(s).", vm.StatusMessage);
    }

    [Fact]
    public void SelectedAzureSubscription_UpdatesSelectedProfile()
    {
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        File.WriteAllText(hostsPath, "127.0.0.1 localhost\n");
        var vm = CreateViewModel(tempDir.Path, hostsPath);
        var profile = new HostProfile
        {
            Name = "Azure Remote",
            SourceType = SourceType.Remote,
            RemoteTransport = RemoteTransport.AzurePrivateDns
        };
        var subscription = new AzureSubscriptionOption { Id = "sub-1", Name = "Primary" };
        vm.Profiles.Add(profile);
        vm.SelectedProfile = profile;

        vm.RemoteEditor.SelectedAzureSubscription = subscription;

        Assert.Same(subscription, vm.RemoteEditor.SelectedAzureSubscription);
        Assert.Equal("sub-1", profile.AzureSubscriptionId);
        Assert.Equal("Primary", profile.AzureSubscriptionName);
    }

    [Fact]
    public void OwnerSelectionChange_RaisesRemoteEditorProperties()
    {
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        File.WriteAllText(hostsPath, "127.0.0.1 localhost\n");
        var vm = CreateViewModel(tempDir.Path, hostsPath);
        var notifications = new List<string>();
        vm.RemoteEditor.PropertyChanged += (_, e) => notifications.Add(e.PropertyName ?? string.Empty);

        vm.SelectedProfile = new HostProfile
        {
            Name = "Remote",
            SourceType = SourceType.Remote,
            RemoteTransport = RemoteTransport.Https
        };

        Assert.Same(vm.SelectedProfile, vm.RemoteEditor.SelectedProfile);
        Assert.True(vm.RemoteEditor.IsVisible);
        Assert.True(vm.RemoteEditor.IsHttpRemoteSelected);
        Assert.Contains(nameof(RemoteSourceEditorViewModel.SelectedProfile), notifications);
        Assert.Contains(nameof(RemoteSourceEditorViewModel.IsVisible), notifications);
        Assert.Contains(nameof(RemoteSourceEditorViewModel.IsHttpRemoteSelected), notifications);
    }

    [Fact]
    public async Task ReadSelectedRemoteHostsCommand_SyncsRemoteSourceAndUpdatesStatus()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        await File.WriteAllTextAsync(hostsPath, "127.0.0.1 localhost\n", cancellationToken);
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("10.0.0.5 synced.remote\n", Encoding.UTF8)
        });
        var vm = CreateViewModel(tempDir.Path, hostsPath, httpClient: new HttpClient(handler));
        var profile = new HostProfile
        {
            Id = "remote-1",
            Name = "Remote",
            SourceType = SourceType.Remote,
            RemoteTransport = RemoteTransport.Https,
            RemoteLocation = "https://example.test/hosts"
        };
        vm.Profiles.Add(profile);
        vm.SelectedProfile = profile;

        await vm.RemoteEditor.ReadSelectedRemoteHostsCommand.ExecuteAsync(null);

        Assert.Equal("10.0.0.5 synced.remote\n", profile.Entries.Replace("\r\n", "\n"));
        Assert.Equal("Synced selected remote source.", vm.StatusMessage);
        Assert.False(vm.RemoteEditor.IsSelectedRemoteSyncRunning);
        Assert.True(vm.RemoteEditor.IsSelectedRemoteSyncIdle);
    }

    [Fact]
    public async Task SelectingAzureProfile_LoadsPlaceholderSubscriptionAndZonesIntoChildViewModel()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        await File.WriteAllTextAsync(hostsPath, "127.0.0.1 localhost\n", cancellationToken);
        var azure = new FakeAzurePrivateDnsService
        {
            Zones =
            [
                new AzurePrivateDnsZoneInfo { ZoneName = "a.internal", ResourceGroup = "rg-a" }
            ]
        };
        var vm = CreateViewModel(tempDir.Path, hostsPath, azureService: azure);
        var profile = new HostProfile
        {
            Name = "Azure Remote",
            SourceType = SourceType.Remote,
            RemoteTransport = RemoteTransport.AzurePrivateDns,
            AzureSubscriptionId = "sub-imported",
            AzureSubscriptionName = "Imported"
        };
        vm.Profiles.Add(profile);

        vm.SelectedProfile = profile;
        await Task.Yield();

        Assert.Single(vm.RemoteEditor.AzureSubscriptions);
        Assert.Equal("sub-imported", vm.RemoteEditor.SelectedAzureSubscription?.Id);
        Assert.Single(vm.RemoteEditor.AzureZones);
    }

    private static MainWindowViewModel CreateViewModel(
        string tempRoot,
        string hostsPath,
        IProfileStore? profileStore = null,
        HttpClient? httpClient = null,
        IAzurePrivateDnsService? azureService = null)
    {
        Directory.CreateDirectory(Path.Combine(tempRoot, "config"));
        var hostsService = new HostsFileService(Path.Combine(tempRoot, "appdata"), hostsPath);

        return new MainWindowViewModel(
            profileStore ?? new ProfileStore(Path.Combine(tempRoot, "config")),
            hostsService,
            new LocalSourceService(),
            null,
            new LocalSourceWatcherService(),
            null,
            new FakeStartupRegistrationService(),
            new FakeWindowsElevationService(),
            httpClient ?? new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty)
            })),
            azureService ?? new FakeAzurePrivateDnsService(),
            new ManualTimer(),
            new ManualTimer());
    }
}
