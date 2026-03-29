namespace HostsManager.Desktop.Tests;

public sealed class LocalSourceRefreshServiceTests
{
    [Fact]
    public async Task RefreshSystemSourceAsync_DetectsExternalChangeWhenSelectedSourceIsSkipped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        await File.WriteAllTextAsync(hostsPath, "127.0.0.1 localhost\n", cancellationToken);
        var workflowService = CreateService(tempDir.Path, hostsPath);
        var service = CreateRefreshService(tempDir.Path, hostsPath);
        var systemSource = await workflowService.BuildSystemSourceAsync(cancellationToken);
        await File.WriteAllTextAsync(hostsPath, "127.0.0.1 changed.localhost\n", cancellationToken);

        var result = await service.RefreshSystemSourceAsync(
            systemSource,
            systemSource,
            announceWhenChanged: true,
            skipSelectedProfile: true,
            cancellationToken);

        Assert.False(result.Changed);
        Assert.True(result.ExternalChangeDetected);
        Assert.False(result.SelectedProfileChanged);
        Assert.False(result.RefreshPulseRequested);
    }

    [Fact]
    public async Task RefreshSystemSourceAsync_ReloadsSystemSourceAndRequestsPulseWhenSelected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        await File.WriteAllTextAsync(hostsPath, "127.0.0.1 localhost\n", cancellationToken);
        var workflowService = CreateService(tempDir.Path, hostsPath);
        var service = CreateRefreshService(tempDir.Path, hostsPath);
        var systemSource = await workflowService.BuildSystemSourceAsync(cancellationToken);
        await File.WriteAllTextAsync(hostsPath, "127.0.0.1 changed.localhost\n", cancellationToken);

        var result = await service.RefreshSystemSourceAsync(
            systemSource,
            systemSource,
            announceWhenChanged: true,
            skipSelectedProfile: false,
            cancellationToken);

        Assert.True(result.Changed);
        Assert.True(result.SelectedProfileChanged);
        Assert.True(result.RefreshPulseRequested);
        Assert.Equal("127.0.0.1 changed.localhost\n", systemSource.Entries.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task RefreshLocalSourcesAsync_ReloadsNonSelectedLocalSources()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        await File.WriteAllTextAsync(hostsPath, "127.0.0.1 localhost\n", cancellationToken);
        var localPath = Path.Combine(tempDir.Path, "local.hosts");
        await File.WriteAllTextAsync(localPath, "127.0.0.1 before.local\n", cancellationToken);
        var service = CreateRefreshService(tempDir.Path, hostsPath);
        var localSource = await new LocalSourceService().LoadExistingSourceAsync(localPath, cancellationToken);
        await File.WriteAllTextAsync(localPath, "127.0.0.1 after.local\n", cancellationToken);

        var result = await service.RefreshLocalSourcesAsync([localSource], selectedProfile: null, cancellationToken);

        Assert.True(result.AnyContentChanged);
        Assert.False(result.SelectedProfileChanged);
        Assert.Null(result.SelectedSourceWithExternalChanges);
        Assert.Equal("127.0.0.1 after.local\n", localSource.Entries.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task RefreshLocalSourcesAsync_DetectsExternalChangeForSelectedLocalSource()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        await File.WriteAllTextAsync(hostsPath, "127.0.0.1 localhost\n", cancellationToken);
        var localPath = Path.Combine(tempDir.Path, "selected.hosts");
        await File.WriteAllTextAsync(localPath, "127.0.0.1 before.local\n", cancellationToken);
        var service = CreateRefreshService(tempDir.Path, hostsPath);
        var localSource = await new LocalSourceService().LoadExistingSourceAsync(localPath, cancellationToken);
        await File.WriteAllTextAsync(localPath, "127.0.0.1 after.local\n", cancellationToken);

        var result = await service.RefreshLocalSourcesAsync([localSource], localSource, cancellationToken);

        Assert.False(result.AnyContentChanged);
        Assert.Same(localSource, result.SelectedSourceWithExternalChanges);
    }

    [Fact]
    public async Task RefreshLocalSourcesAsync_TracksMissingStateChanges()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        await File.WriteAllTextAsync(hostsPath, "127.0.0.1 localhost\n", cancellationToken);
        var missingPath = Path.Combine(tempDir.Path, "missing.hosts");
        var service = CreateRefreshService(tempDir.Path, hostsPath);
        var source = new HostProfile
        {
            Name = "Missing",
            SourceType = SourceType.Local,
            LocalPath = missingPath,
            IsEnabled = true
        };

        var result = await service.RefreshLocalSourcesAsync([source], source, cancellationToken);

        Assert.True(result.AnyContentChanged);
        Assert.True(result.SelectedProfileChanged);
        Assert.Single(result.MissingStateChangedSources);
        Assert.True(source.IsMissingLocalFile);
        Assert.False(source.IsEnabled);
    }

    private static SystemHostsWorkflowService CreateService(string tempRoot, string hostsPath)
    {
        Directory.CreateDirectory(Path.Combine(tempRoot, "config"));
        return new SystemHostsWorkflowService(
            new HostsFileService(Path.Combine(tempRoot, "appdata"), hostsPath),
            new LocalSourceService(),
            new ProfileStore(Path.Combine(tempRoot, "config")),
            new PassiveWindowsElevationService());
    }

    private static LocalSourceRefreshService CreateRefreshService(string tempRoot, string hostsPath)
    {
        return new LocalSourceRefreshService(new LocalSourceService(), CreateService(tempRoot, hostsPath));
    }

    private sealed class PassiveWindowsElevationService : IWindowsElevationService
    {
        public bool IsSupported => false;

        public bool IsProcessElevated()
        {
            return false;
        }

        public bool TryRelaunchElevated(StartupAction action, string? payloadPath = null, bool startInBackground = false)
        {
            return false;
        }
    }
}
