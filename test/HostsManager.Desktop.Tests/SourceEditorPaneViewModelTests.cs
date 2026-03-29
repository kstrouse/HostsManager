namespace HostsManager.Desktop.Tests;

public sealed class SourceEditorPaneViewModelTests
{
    [Fact]
    public void Constructor_ExposesChildViewModelsAndSaveCommand()
    {
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        File.WriteAllText(hostsPath, "127.0.0.1 localhost\n");
        var vm = CreateViewModel(tempDir.Path, hostsPath);

        Assert.Same(vm.SelectedSourceDetails, vm.SourceEditor.SelectedSourceDetails);
        Assert.Same(vm.LocalEditor, vm.SourceEditor.LocalEditor);
        Assert.Same(vm.RemoteEditor, vm.SourceEditor.RemoteEditor);
        Assert.Same(vm.SaveSelectedSourceCommand, vm.SourceEditor.SaveSelectedSourceCommand);
    }

    [Fact]
    public void OwnerSelectionChange_RaisesSelectedProfile()
    {
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        File.WriteAllText(hostsPath, "127.0.0.1 localhost\n");
        var vm = CreateViewModel(tempDir.Path, hostsPath);
        var notifications = new List<string>();
        vm.SourceEditor.PropertyChanged += (_, e) => notifications.Add(e.PropertyName ?? string.Empty);

        vm.SelectedProfile = new HostProfile { Name = "Remote", SourceType = SourceType.Remote };

        Assert.Same(vm.SelectedProfile, vm.SourceEditor.SelectedProfile);
        Assert.Contains(nameof(SourceEditorPaneViewModel.SelectedProfile), notifications);
    }

    [Fact]
    public void ExternalChangeProperties_ForwardFromOwner()
    {
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        File.WriteAllText(hostsPath, "127.0.0.1 localhost\n");
        var vm = CreateViewModel(tempDir.Path, hostsPath);
        var notifications = new List<string>();
        vm.SourceEditor.PropertyChanged += (_, e) => notifications.Add(e.PropertyName ?? string.Empty);

        vm.SelectedSourceChangedExternally = true;
        vm.SelectedSourceExternalChangeName = "local.hosts";

        Assert.True(vm.SourceEditor.SelectedSourceChangedExternally);
        Assert.Equal("local.hosts", vm.SourceEditor.SelectedSourceExternalChangeName);
        Assert.Contains(nameof(SourceEditorPaneViewModel.SelectedSourceChangedExternally), notifications);
        Assert.Contains(nameof(SourceEditorPaneViewModel.SelectedSourceExternalChangeName), notifications);
    }

    [Fact]
    public async Task ReloadAndDismiss_DelegateToOwner()
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

        var changed = await vm.SourceEditor.ReloadSelectedSourceFromDiskAsync();
        vm.SourceEditor.DismissSelectedSourceExternalChangeNotification();

        Assert.True(changed);
        Assert.False(vm.SourceEditor.SelectedSourceChangedExternally);
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
