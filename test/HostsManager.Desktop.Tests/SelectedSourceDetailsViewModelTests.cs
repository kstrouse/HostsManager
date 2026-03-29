namespace HostsManager.Desktop.Tests;

public sealed class SelectedSourceDetailsViewModelTests
{
    [Fact]
    public void Constructor_ExposesApplyCommandAndHostsPath()
    {
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        File.WriteAllText(hostsPath, "127.0.0.1 localhost\n");
        var vm = CreateViewModel(tempDir.Path, hostsPath);

        Assert.NotNull(vm.SelectedSourceDetails.ApplyToSystemHostsCommand);
        Assert.Equal(hostsPath, vm.SelectedSourceDetails.HostsPath);
    }

    [Fact]
    public void OwnerSelectionChange_UpdatesSourceDisplayState()
    {
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        File.WriteAllText(hostsPath, "127.0.0.1 localhost\n");
        var vm = CreateViewModel(tempDir.Path, hostsPath);
        var notifications = new List<string>();
        vm.SelectedSourceDetails.PropertyChanged += (_, e) => notifications.Add(e.PropertyName ?? string.Empty);

        vm.SelectedProfile = new HostProfile
        {
            Name = "Remote",
            SourceType = SourceType.Remote,
            RemoteTransport = RemoteTransport.AzurePrivateDns
        };

        Assert.Same(vm.SelectedProfile, vm.SelectedSourceDetails.SelectedProfile);
        Assert.Equal("Remote (Azure Private DNS)", vm.SelectedSourceDetails.SelectedSourceTypeDisplay);
        Assert.False(vm.SelectedSourceDetails.IsSystemSelected);
        Assert.Contains(nameof(SelectedSourceDetailsViewModel.SelectedProfile), notifications);
        Assert.Contains(nameof(SelectedSourceDetailsViewModel.SelectedSourceTypeDisplay), notifications);
    }

    [Fact]
    public void SystemSelection_ExposesEditStateAndReadOnlyStatus()
    {
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        File.WriteAllText(hostsPath, "127.0.0.1 localhost\n");
        var vm = CreateViewModel(tempDir.Path, hostsPath);
        vm.SelectedProfile = new HostProfile
        {
            Name = "System",
            SourceType = SourceType.System,
            IsReadOnly = true
        };

        Assert.True(vm.SelectedSourceDetails.IsSystemSelected);
        Assert.True(vm.SelectedSourceDetails.IsSelectedSourceReadOnly);

        vm.SelectedSourceDetails.IsSystemHostsEditingEnabled = true;

        Assert.True(vm.IsSystemHostsEditingEnabled);
        Assert.True(vm.SelectedSourceDetails.IsSystemHostsEditingEnabled);
    }

    [Fact]
    public void OwnerEditStateChange_UpdatesChildState()
    {
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        File.WriteAllText(hostsPath, "127.0.0.1 localhost\n");
        var vm = CreateViewModel(tempDir.Path, hostsPath);
        var notifications = new List<string>();
        vm.SelectedSourceDetails.PropertyChanged += (_, e) => notifications.Add(e.PropertyName ?? string.Empty);

        vm.IsSystemHostsEditingEnabled = true;

        Assert.True(vm.SelectedSourceDetails.IsSystemHostsEditingEnabled);
        Assert.Contains(nameof(SelectedSourceDetailsViewModel.IsSystemHostsEditingEnabled), notifications);
    }

    [Fact]
    public void PendingApplyChange_RaisesNotification()
    {
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        File.WriteAllText(hostsPath, "127.0.0.1 localhost\n");
        var vm = CreateViewModel(tempDir.Path, hostsPath);
        var notifications = new List<string>();
        vm.SelectedSourceDetails.PropertyChanged += (_, e) => notifications.Add(e.PropertyName ?? string.Empty);

        vm.HasPendingElevatedHostsUpdate = true;

        Assert.True(vm.SelectedSourceDetails.HasPendingElevatedHostsUpdate);
        Assert.Contains(nameof(SelectedSourceDetailsViewModel.HasPendingElevatedHostsUpdate), notifications);
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
