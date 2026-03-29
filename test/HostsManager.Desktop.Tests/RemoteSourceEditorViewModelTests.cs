using System.ComponentModel;

namespace HostsManager.Desktop.Tests;

public sealed class RemoteSourceEditorViewModelTests
{
    [Fact]
    public void Constructor_ExposesParentCommandsAndCollections()
    {
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        File.WriteAllText(hostsPath, "127.0.0.1 localhost\n");
        var vm = CreateViewModel(tempDir.Path, hostsPath);

        Assert.Same(vm.LoadAzureSubscriptionsCommand, vm.RemoteEditor.LoadAzureSubscriptionsCommand);
        Assert.Same(vm.RefreshAzureZonesCommand, vm.RemoteEditor.RefreshAzureZonesCommand);
        Assert.Same(vm.ReadSelectedRemoteHostsCommand, vm.RemoteEditor.ReadSelectedRemoteHostsCommand);
        Assert.Same(vm.AzureSubscriptions, vm.RemoteEditor.AzureSubscriptions);
        Assert.Same(vm.AzureZones, vm.RemoteEditor.AzureZones);
    }

    [Fact]
    public void SelectedAzureSubscription_ForwardsToOwner()
    {
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        File.WriteAllText(hostsPath, "127.0.0.1 localhost\n");
        var vm = CreateViewModel(tempDir.Path, hostsPath);
        var subscription = new AzureSubscriptionOption { Id = "sub-1", Name = "Primary" };

        vm.RemoteEditor.SelectedAzureSubscription = subscription;

        Assert.Same(subscription, vm.SelectedAzureSubscription);
        Assert.Same(subscription, vm.RemoteEditor.SelectedAzureSubscription);
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
    public void OwnerSyncStateChange_RaisesRunningAndIdleProperties()
    {
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        File.WriteAllText(hostsPath, "127.0.0.1 localhost\n");
        var vm = CreateViewModel(tempDir.Path, hostsPath);
        var notifications = new List<string>();
        vm.RemoteEditor.PropertyChanged += (_, e) => notifications.Add(e.PropertyName ?? string.Empty);

        vm.IsSelectedRemoteSyncRunning = true;

        Assert.True(vm.RemoteEditor.IsSelectedRemoteSyncRunning);
        Assert.False(vm.RemoteEditor.IsSelectedRemoteSyncIdle);
        Assert.Contains(nameof(RemoteSourceEditorViewModel.IsSelectedRemoteSyncRunning), notifications);
        Assert.Contains(nameof(RemoteSourceEditorViewModel.IsSelectedRemoteSyncIdle), notifications);
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
