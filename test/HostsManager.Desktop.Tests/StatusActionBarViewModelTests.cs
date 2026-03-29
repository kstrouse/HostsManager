namespace HostsManager.Desktop.Tests;

public sealed class StatusActionBarViewModelTests
{
    [Fact]
    public void Constructor_ExposesOwnerCommandsAndStartupSupport()
    {
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        File.WriteAllText(hostsPath, "127.0.0.1 localhost\n");
        var vm = CreateViewModel(tempDir.Path, hostsPath);

        Assert.Same(vm.SyncAllFromUrlCommand, vm.StatusBar.SyncAllFromUrlCommand);
        Assert.Same(vm.ApplyToSystemHostsCommand, vm.StatusBar.ApplyToSystemHostsCommand);
        Assert.False(vm.StatusBar.CanConfigureRunAtStartup);
    }

    [Fact]
    public void StatusMessageChange_RaisesNotification()
    {
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        File.WriteAllText(hostsPath, "127.0.0.1 localhost\n");
        var vm = CreateViewModel(tempDir.Path, hostsPath);
        var notifications = new List<string>();
        vm.StatusBar.PropertyChanged += (_, e) => notifications.Add(e.PropertyName ?? string.Empty);

        vm.StatusMessage = "Updated";

        Assert.Equal("Updated", vm.StatusBar.StatusMessage);
        Assert.Contains(nameof(StatusActionBarViewModel.StatusMessage), notifications);
    }

    [Fact]
    public void MinimizePreference_ForwardsToOwner()
    {
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        File.WriteAllText(hostsPath, "127.0.0.1 localhost\n");
        var vm = CreateViewModel(tempDir.Path, hostsPath);

        vm.StatusBar.MinimizeToTrayOnClose = true;

        Assert.True(vm.MinimizeToTrayOnClose);
        Assert.True(vm.StatusBar.MinimizeToTrayOnClose);
    }

    [Fact]
    public void RunAtStartup_ForwardsToOwner()
    {
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        File.WriteAllText(hostsPath, "127.0.0.1 localhost\n");
        var vm = CreateViewModel(tempDir.Path, hostsPath);

        vm.StatusBar.RunAtStartup = true;

        Assert.True(vm.RunAtStartup);
        Assert.True(vm.StatusBar.RunAtStartup);
    }

    [Fact]
    public void Constructor_CanExposeStartupSupport()
    {
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        File.WriteAllText(hostsPath, "127.0.0.1 localhost\n");
        var vm = CreateViewModel(tempDir.Path, hostsPath, startupRegistrationService: new FakeStartupRegistrationService { IsSupported = true });

        Assert.True(vm.StatusBar.CanConfigureRunAtStartup);
    }

    private static MainWindowViewModel CreateViewModel(
        string tempRoot,
        string hostsPath,
        IProfileStore? profileStore = null,
        HttpClient? httpClient = null,
        IAzurePrivateDnsService? azureService = null,
        IStartupRegistrationService? startupRegistrationService = null)
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
            startupRegistrationService ?? new FakeStartupRegistrationService(),
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
