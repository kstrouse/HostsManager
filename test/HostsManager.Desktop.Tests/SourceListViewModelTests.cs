namespace HostsManager.Desktop.Tests;

public sealed class SourceListViewModelTests
{
    [Fact]
    public void Constructor_ExposesProfilesAndDeleteCommand()
    {
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        File.WriteAllText(hostsPath, "127.0.0.1 localhost\n");
        var vm = CreateViewModel(tempDir.Path, hostsPath);

        Assert.Same(vm.Profiles, vm.SourceList.Profiles);
        Assert.Same(vm.DeleteProfileCommand, vm.SourceList.DeleteProfileCommand);
    }

    [Fact]
    public void SelectedProfile_ForwardsToOwner()
    {
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        File.WriteAllText(hostsPath, "127.0.0.1 localhost\n");
        var vm = CreateViewModel(tempDir.Path, hostsPath);
        var profile = new HostProfile { Name = "Remote", SourceType = SourceType.Remote };

        vm.SourceList.SelectedProfile = profile;

        Assert.Same(profile, vm.SelectedProfile);
        Assert.Same(profile, vm.SourceList.SelectedProfile);
    }

    [Fact]
    public void OwnerSelectionChange_RaisesSelectedProfile()
    {
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        File.WriteAllText(hostsPath, "127.0.0.1 localhost\n");
        var vm = CreateViewModel(tempDir.Path, hostsPath);
        var notifications = new List<string>();
        vm.SourceList.PropertyChanged += (_, e) => notifications.Add(e.PropertyName ?? string.Empty);

        vm.SelectedProfile = new HostProfile { Name = "Local", SourceType = SourceType.Local };

        Assert.Contains(nameof(SourceListViewModel.SelectedProfile), notifications);
    }

    [Fact]
    public async Task HandleSourceToggledAsync_RunsOwnerWorkflow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        await File.WriteAllTextAsync(hostsPath, "127.0.0.1 localhost\n", cancellationToken);
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("10.0.0.5 remote.toggle\n")
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

        await vm.SourceList.HandleSourceToggledAsync(source);

        Assert.Equal("10.0.0.5 remote.toggle\n", source.Entries.Replace("\r\n", "\n"));
        Assert.Equal("Remote source synced on enable: Remote Toggle", vm.StatusMessage);
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
