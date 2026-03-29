namespace HostsManager.Desktop.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task InitializeAsync_LoadsSystemSourceFirst_AndPersistedProfiles()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        await File.WriteAllTextAsync(hostsPath, "127.0.0.1 localhost\n", cancellationToken);

        var store = new ProfileStore(Path.Combine(tempDir.Path, "config"));
        await store.SaveAsync(new AppConfig
        {
            Profiles =
            [
                new HostProfile
                {
                    Id = "remote-1",
                    Name = "Remote One",
                    SourceType = SourceType.Remote,
                    RemoteTransport = RemoteTransport.Https,
                    RemoteLocation = "https://example.test/hosts"
                }
            ]
        }, cancellationToken);

        var vm = CreateViewModel(tempDir.Path, hostsPath, profileStore: store);

        await vm.InitializeAsync();

        Assert.Equal(2, vm.Profiles.Count);
        Assert.Equal(SourceType.System, vm.Profiles[0].SourceType);
        Assert.True(vm.Profiles[0].IsReadOnly);
        Assert.Equal("Remote One", vm.Profiles[1].Name);
        Assert.Same(vm.Profiles[0], vm.SelectedProfile);
        Assert.Equal("Loaded 2 source(s).", vm.StatusMessage);
    }

    [Fact]
    public void AddRemoteSource_SetsExpectedDefaults()
    {
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        File.WriteAllText(hostsPath, "127.0.0.1 localhost\n");
        var vm = CreateViewModel(tempDir.Path, hostsPath);

        vm.AddRemoteSource(RemoteTransport.Http);

        var profile = Assert.IsType<HostProfile>(vm.SelectedProfile);
        Assert.Equal(SourceType.Remote, profile.SourceType);
        Assert.Equal(RemoteTransport.Http, profile.RemoteTransport);
        Assert.Equal("15", profile.RefreshIntervalMinutes);
        Assert.Equal("New remote source created (HTTP/HTTPS).", vm.StatusMessage);
    }

    [Fact]
    public async Task AddNewLocalSourceAsync_CreatesFileAndSelectsProfile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        await File.WriteAllTextAsync(hostsPath, "127.0.0.1 localhost\n", cancellationToken);
        var vm = CreateViewModel(tempDir.Path, hostsPath);
        var localPath = Path.Combine(tempDir.Path, "dev.hosts");

        await vm.AddNewLocalSourceAsync(localPath);

        Assert.True(File.Exists(localPath));
        Assert.Equal("Local source created and added.", vm.StatusMessage);
        var profile = Assert.IsType<HostProfile>(vm.SelectedProfile);
        Assert.Equal(SourceType.Local, profile.SourceType);
        Assert.Equal(localPath, profile.LocalPath);
        Assert.Contains("# New local hosts source", profile.Entries);
    }

    [Fact]
    public async Task RenameSelectedLocalFileAsync_PreservesExtensionAndUpdatesPath()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        await File.WriteAllTextAsync(hostsPath, "127.0.0.1 localhost\n", cancellationToken);
        var currentPath = Path.Combine(tempDir.Path, "old.hosts");
        await File.WriteAllTextAsync(currentPath, "127.0.0.1 old.local\n", cancellationToken);

        var vm = CreateViewModel(tempDir.Path, hostsPath);
        await vm.AddExistingLocalSourceAsync(currentPath);

        await vm.RenameSelectedLocalFileAsync("renamed");

        var expectedPath = Path.Combine(tempDir.Path, "renamed.hosts");
        Assert.False(File.Exists(currentPath));
        Assert.True(File.Exists(expectedPath));
        Assert.Equal(expectedPath, vm.SelectedProfile?.LocalPath);
        Assert.Equal("Renamed local file to renamed.hosts.", vm.StatusMessage);
    }

    [Fact]
    public async Task ReloadSelectedSourceFromDiskAsync_UpdatesEntriesAndClearsExternalChangeNotification()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        await File.WriteAllTextAsync(hostsPath, "127.0.0.1 localhost\n", cancellationToken);
        var localPath = Path.Combine(tempDir.Path, "reload.hosts");
        await File.WriteAllTextAsync(localPath, "127.0.0.1 before.local\n", cancellationToken);

        var vm = CreateViewModel(tempDir.Path, hostsPath);
        await vm.AddExistingLocalSourceAsync(localPath);
        vm.SelectedSourceChangedExternally = true;
        vm.SelectedSourceExternalChangeName = "reload";
        await File.WriteAllTextAsync(localPath, "127.0.0.1 after.local\n", cancellationToken);

        var changed = await vm.ReloadSelectedSourceFromDiskAsync();

        Assert.True(changed);
        Assert.Equal("127.0.0.1 after.local\n", vm.SelectedProfile?.Entries.Replace("\r\n", "\n"));
        Assert.False(vm.SelectedSourceChangedExternally);
        Assert.Equal(string.Empty, vm.SelectedSourceExternalChangeName);
        Assert.Equal($"Reloaded external changes for {vm.SelectedProfile?.Name}.", vm.StatusMessage);
    }

    [Fact]
    public async Task HandleRemoteSourceToggledAsync_SyncsEnabledHttpSource()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        await File.WriteAllTextAsync(hostsPath, "127.0.0.1 localhost\n", cancellationToken);
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("10.0.0.5 remote.toggle\n", Encoding.UTF8)
        });
        var vm = CreateViewModel(tempDir.Path, hostsPath, httpClient: new HttpClient(handler));
        var source = new HostProfile
        {
            Id = "remote-toggle",
            Name = "Remote Toggle",
            IsEnabled = true,
            SourceType = SourceType.Remote,
            RemoteTransport = RemoteTransport.Https,
            RemoteLocation = "https://example.test/toggle"
        };
        vm.Profiles.Add(source);
        vm.SelectedProfile = source;

        await vm.HandleRemoteSourceToggledAsync(source);

        Assert.Equal("10.0.0.5 remote.toggle\n", source.Entries.Replace("\r\n", "\n"));
        Assert.NotNull(source.LastSyncedAtUtc);
        Assert.Equal("Remote source synced on enable: Remote Toggle", vm.StatusMessage);
    }

    [Fact]
    public async Task LoadAzureSubscriptionsCommand_PopulatesSubscriptions()
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

        await vm.LoadAzureSubscriptionsCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.AzureSubscriptions.Count);
        Assert.Equal("Loaded 2 Azure subscription(s).", vm.StatusMessage);
    }

    [Fact]
    public async Task RefreshAzureZonesCommand_UpdatesExcludedZonesWhenSelectionChanges()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        await File.WriteAllTextAsync(hostsPath, "127.0.0.1 localhost\n", cancellationToken);
        var azure = new FakeAzurePrivateDnsService
        {
            Zones =
            [
                new AzurePrivateDnsZoneInfo { ZoneName = "a.internal", ResourceGroup = "rg-a" },
                new AzurePrivateDnsZoneInfo { ZoneName = "b.internal", ResourceGroup = "rg-b" }
            ]
        };
        var vm = CreateViewModel(tempDir.Path, hostsPath, azureService: azure);
        var source = new HostProfile
        {
            Name = "Azure Remote",
            IsEnabled = true,
            SourceType = SourceType.Remote,
            RemoteTransport = RemoteTransport.AzurePrivateDns,
            AzureSubscriptionId = "sub-1",
            AzureSubscriptionName = "Primary"
        };
        vm.Profiles.Add(source);
        vm.SelectedProfile = source;

        await vm.RefreshAzureZonesCommand.ExecuteAsync(null);
        vm.AzureZones[1].IsEnabled = false;

        Assert.Equal(2, vm.AzureZones.Count);
        Assert.Equal("Loaded 2 Azure zone(s).", vm.StatusMessage);
        Assert.Equal("b.internal|rg-b", source.AzureExcludedZones.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task SelectingAzureProfile_LoadsZonesOnceAndAddsPlaceholderSubscription()
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
        var source = new HostProfile
        {
            Name = "Azure Remote",
            IsEnabled = true,
            SourceType = SourceType.Remote,
            RemoteTransport = RemoteTransport.AzurePrivateDns,
            AzureSubscriptionId = "sub-imported",
            AzureSubscriptionName = "Imported"
        };
        vm.Profiles.Add(source);

        vm.SelectedProfile = source;
        await Task.Yield();

        Assert.Equal(1, azure.ListZonesCallCount);
        Assert.Single(vm.AzureSubscriptions);
        Assert.Equal("sub-imported", vm.SelectedAzureSubscription?.Id);
        Assert.Single(vm.AzureZones);
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
                Content = new StringContent("127.0.0.1 default.test\n", Encoding.UTF8)
            })),
            azureService ?? new FakeAzurePrivateDnsService(),
            new ManualTimer(),
            new ManualTimer(),
            null,
            null,
            null,
            null,
            null,
            null);
    }
}
