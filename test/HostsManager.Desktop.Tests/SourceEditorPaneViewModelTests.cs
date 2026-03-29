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
        Assert.NotNull(vm.SourceEditor.SaveSelectedSourceCommand);
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
    public void SaveSelectedSourceCommand_CanExecuteFollowsSelectionState()
    {
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        File.WriteAllText(hostsPath, "127.0.0.1 localhost\n");
        var vm = CreateViewModel(tempDir.Path, hostsPath);

        Assert.False(vm.SourceEditor.SaveSelectedSourceCommand.CanExecute(null));

        vm.SelectedProfile = new HostProfile
        {
            Name = "Local",
            SourceType = SourceType.Local,
            LocalPath = Path.Combine(tempDir.Path, "local.hosts")
        };

        Assert.True(vm.SourceEditor.SaveSelectedSourceCommand.CanExecute(null));

        vm.SelectedProfile.IsMissingLocalFile = true;

        Assert.False(vm.SourceEditor.SaveSelectedSourceCommand.CanExecute(null));

        vm.SelectedProfile = new HostProfile
        {
            Name = "System Hosts",
            SourceType = SourceType.System,
            LocalPath = hostsPath,
            IsReadOnly = true
        };

        Assert.False(vm.SourceEditor.SaveSelectedSourceCommand.CanExecute(null));

        vm.IsSystemHostsEditingEnabled = true;

        Assert.True(vm.SourceEditor.SaveSelectedSourceCommand.CanExecute(null));

        vm.SelectedProfile = new HostProfile
        {
            Name = "Remote",
            SourceType = SourceType.Remote,
            IsReadOnly = true
        };

        Assert.False(vm.SourceEditor.SaveSelectedSourceCommand.CanExecute(null));
    }

    [Fact]
    public async Task SaveSelectedSourceCommand_LocalSelectionWritesFileAndPersistsConfig()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        await File.WriteAllTextAsync(hostsPath, "127.0.0.1 localhost\n", cancellationToken);
        var localPath = Path.Combine(tempDir.Path, "save.hosts");
        await File.WriteAllTextAsync(localPath, "127.0.0.1 before.local\n", cancellationToken);
        var store = new ProfileStore(Path.Combine(tempDir.Path, "config"));
        var vm = CreateViewModel(tempDir.Path, hostsPath, profileStore: store);
        await vm.SourceList.AddExistingLocalSourceAsync(localPath);
        vm.SelectedProfile!.Entries = "127.0.0.1 after.local\n";

        await vm.SourceEditor.SaveSelectedSourceCommand.ExecuteAsync(null);

        Assert.Equal("127.0.0.1 after.local\n", (await File.ReadAllTextAsync(localPath, cancellationToken)).Replace("\r\n", "\n"));
        Assert.Equal("127.0.0.1 after.local\n", vm.SelectedProfile.LastLoadedFromDiskEntries?.Replace("\r\n", "\n"));
        Assert.Equal("Saved source: save", vm.StatusMessage);

        var config = await store.LoadAsync(cancellationToken);
        var savedProfile = Assert.Single(config.Profiles);
        Assert.Equal(localPath, savedProfile.LocalPath);
        Assert.Equal("127.0.0.1 after.local\n", savedProfile.Entries.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task SaveSelectedSourceCommand_SystemSelectionSavesHostsFile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        await File.WriteAllTextAsync(hostsPath, "127.0.0.1 localhost\n", cancellationToken);
        var vm = CreateViewModel(tempDir.Path, hostsPath);
        var systemSource = new HostProfile
        {
            Id = "system-hosts-source",
            Name = "System Hosts",
            SourceType = SourceType.System,
            LocalPath = hostsPath,
            Entries = "127.0.0.1 edited.local\n",
            IsReadOnly = true,
            LastLoadedFromDiskEntries = "127.0.0.1 localhost\n"
        };
        vm.Profiles.Add(systemSource);
        vm.SelectedProfile = systemSource;
        vm.IsSystemHostsEditingEnabled = true;

        await vm.SourceEditor.SaveSelectedSourceCommand.ExecuteAsync(null);

        Assert.Equal("127.0.0.1 edited.local\n", (await File.ReadAllTextAsync(hostsPath, cancellationToken)).Replace("\r\n", "\n"));
        Assert.Equal("System hosts file saved.", vm.StatusMessage);
        Assert.False(vm.IsSystemHostsEditingEnabled);
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
    public async Task ReloadAndDismiss_OperateWithinChildViewModel()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        await File.WriteAllTextAsync(hostsPath, "127.0.0.1 localhost\n", cancellationToken);
        var localPath = Path.Combine(tempDir.Path, "reload.hosts");
        await File.WriteAllTextAsync(localPath, "127.0.0.1 before.local\n", cancellationToken);
        var vm = CreateViewModel(tempDir.Path, hostsPath);
        await vm.SourceList.AddExistingLocalSourceAsync(localPath);
        vm.SelectedSourceChangedExternally = true;
        vm.SelectedSourceExternalChangeName = "reload";
        await File.WriteAllTextAsync(localPath, "127.0.0.1 after.local\n", cancellationToken);

        var changed = await vm.SourceEditor.ReloadSelectedSourceFromDiskAsync();
        vm.SourceEditor.DismissSelectedSourceExternalChangeNotification();

        Assert.True(changed);
        Assert.Equal("127.0.0.1 after.local\n", vm.SelectedProfile?.Entries.Replace("\r\n", "\n"));
        Assert.False(vm.SourceEditor.SelectedSourceChangedExternally);
        Assert.Equal($"Reloaded external changes for {vm.SelectedProfile?.Name}.", vm.StatusMessage);
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
