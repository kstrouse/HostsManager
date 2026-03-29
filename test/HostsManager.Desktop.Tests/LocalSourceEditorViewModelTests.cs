namespace HostsManager.Desktop.Tests;

public sealed class LocalSourceEditorViewModelTests
{
    [Fact]
    public void Commands_CanExecuteFollowsLocalSelectionState()
    {
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        File.WriteAllText(hostsPath, "127.0.0.1 localhost\n");
        var vm = CreateViewModel(tempDir.Path, hostsPath);
        var missingPath = Path.Combine(tempDir.Path, "missing.hosts");

        Assert.False(vm.LocalEditor.OpenSelectedLocalFolderCommand.CanExecute(null));
        Assert.False(vm.LocalEditor.RecreateMissingLocalFileCommand.CanExecute(null));

        vm.SelectedProfile = new HostProfile
        {
            Name = "Local",
            SourceType = SourceType.Local,
            LocalPath = missingPath
        };

        Assert.True(vm.LocalEditor.OpenSelectedLocalFolderCommand.CanExecute(null));
        Assert.False(vm.LocalEditor.RecreateMissingLocalFileCommand.CanExecute(null));

        vm.SelectedProfile.IsMissingLocalFile = true;

        Assert.True(vm.LocalEditor.RecreateMissingLocalFileCommand.CanExecute(null));
    }

    [Fact]
    public void OwnerSelectionChange_UpdatesVisibilityAndPath()
    {
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        File.WriteAllText(hostsPath, "127.0.0.1 localhost\n");
        var vm = CreateViewModel(tempDir.Path, hostsPath);
        var notifications = new List<string>();
        vm.LocalEditor.PropertyChanged += (_, e) => notifications.Add(e.PropertyName ?? string.Empty);

        vm.SelectedProfile = new HostProfile
        {
            Name = "Local",
            SourceType = SourceType.Local,
            LocalPath = Path.Combine(tempDir.Path, "dev.hosts")
        };

        Assert.True(vm.LocalEditor.IsVisible);
        Assert.Equal(Path.Combine(tempDir.Path, "dev.hosts"), vm.LocalEditor.SelectedLocalFilePath);
        Assert.Contains(nameof(LocalSourceEditorViewModel.IsVisible), notifications);
        Assert.Contains(nameof(LocalSourceEditorViewModel.SelectedLocalFilePath), notifications);
    }

    [Fact]
    public void SelectedProfilePropertyChange_UpdatesMissingState()
    {
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        File.WriteAllText(hostsPath, "127.0.0.1 localhost\n");
        var vm = CreateViewModel(tempDir.Path, hostsPath);
        var profile = new HostProfile
        {
            Name = "Local",
            SourceType = SourceType.Local,
            LocalPath = Path.Combine(tempDir.Path, "dev.hosts")
        };
        vm.SelectedProfile = profile;
        var notifications = new List<string>();
        vm.LocalEditor.PropertyChanged += (_, e) => notifications.Add(e.PropertyName ?? string.Empty);

        profile.IsMissingLocalFile = true;

        Assert.True(vm.LocalEditor.IsSelectedLocalFileMissing);
        Assert.Contains(nameof(LocalSourceEditorViewModel.IsSelectedLocalFileMissing), notifications);
    }

    [Fact]
    public async Task RenameSelectedLocalFileAsync_RenamesFileAndUpdatesStatus()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        await File.WriteAllTextAsync(hostsPath, "127.0.0.1 localhost\n", cancellationToken);
        var currentPath = Path.Combine(tempDir.Path, "old.hosts");
        await File.WriteAllTextAsync(currentPath, "127.0.0.1 old.local\n", cancellationToken);
        var vm = CreateViewModel(tempDir.Path, hostsPath);
        await vm.SourceList.AddExistingLocalSourceAsync(currentPath);

        await vm.LocalEditor.RenameSelectedLocalFileAsync("renamed");

        var expectedPath = Path.Combine(tempDir.Path, "renamed.hosts");
        Assert.Equal(expectedPath, vm.LocalEditor.SelectedLocalFilePath);
        Assert.Equal("Renamed local file to renamed.hosts.", vm.StatusMessage);
    }

    [Fact]
    public async Task RecreateMissingLocalFileCommand_RecreatesFileAndUpdatesState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        await File.WriteAllTextAsync(hostsPath, "127.0.0.1 localhost\n", cancellationToken);
        var vm = CreateViewModel(tempDir.Path, hostsPath);
        var missingPath = Path.Combine(tempDir.Path, "nested", "missing.hosts");
        var profile = new HostProfile
        {
            Name = "Missing",
            SourceType = SourceType.Local,
            LocalPath = missingPath,
            IsEnabled = false,
            IsMissingLocalFile = true,
            Entries = "127.0.0.1 restored.local\n",
            LastLoadedFromDiskEntries = "127.0.0.1 restored.local\n"
        };
        vm.Profiles.Add(profile);
        vm.SelectedProfile = profile;

        await vm.LocalEditor.RecreateMissingLocalFileCommand.ExecuteAsync(null);

        Assert.True(File.Exists(missingPath));
        Assert.False(profile.IsMissingLocalFile);
        Assert.True(profile.IsEnabled);
        Assert.Equal("127.0.0.1 restored.local\n", profile.Entries.Replace("\r\n", "\n"));
        Assert.Equal($"Re-created local source file: {Path.GetFileName(missingPath)}", vm.StatusMessage);
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
