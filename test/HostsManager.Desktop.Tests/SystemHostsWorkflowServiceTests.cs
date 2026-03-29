namespace HostsManager.Desktop.Tests;

public sealed class SystemHostsWorkflowServiceTests
{
    [Fact]
    public async Task BuildSystemSourceAsync_ReadsHostsFileIntoReadOnlyProfile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        await File.WriteAllTextAsync(hostsPath, "127.0.0.1 localhost\n", cancellationToken);
        var service = CreateService(tempDir.Path, hostsPath);

        var source = await service.BuildSystemSourceAsync(cancellationToken);

        Assert.Equal(SourceType.System, source.SourceType);
        Assert.True(source.IsReadOnly);
        Assert.Equal(hostsPath, source.LocalPath);
        Assert.Equal("127.0.0.1 localhost\n", source.Entries.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task NeedsManagedApply_ReflectsCurrentSystemHostsContent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        await File.WriteAllTextAsync(hostsPath, "127.0.0.1 localhost\n", cancellationToken);
        var service = CreateService(tempDir.Path, hostsPath);
        var sources = new[]
        {
            new HostProfile
            {
                Name = "Remote One",
                SourceType = SourceType.Remote,
                RemoteTransport = RemoteTransport.Https,
                Entries = "10.0.0.5 example.test\n"
            }
        };

        var initialSystemSource = await service.BuildSystemSourceAsync(cancellationToken);
        Assert.True(service.NeedsManagedApply(sources, initialSystemSource));

        await service.ApplyManagedHostsAsync(sources, cancellationToken: cancellationToken);
        var updatedSystemSource = await service.BuildSystemSourceAsync(cancellationToken);

        Assert.False(service.NeedsManagedApply(sources, updatedSystemSource));
    }

    [Fact]
    public async Task WritePendingRawHostsPayloadAsync_WritesPayloadToConfigDirectory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        await File.WriteAllTextAsync(hostsPath, "127.0.0.1 localhost\n", cancellationToken);
        var service = CreateService(tempDir.Path, hostsPath);

        var payloadPath = await service.WritePendingRawHostsPayloadAsync("127.0.0.1 payload.test\n", cancellationToken);

        Assert.True(File.Exists(payloadPath));
        Assert.Equal("127.0.0.1 payload.test\n", (await File.ReadAllTextAsync(payloadPath, cancellationToken)).Replace("\r\n", "\n"));
    }

    [Fact]
    public void CanRequestElevation_AndTryRelaunchElevated_DelegateToElevationService()
    {
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        File.WriteAllText(hostsPath, "127.0.0.1 localhost\n");
        var elevation = new TrackingWindowsElevationService
        {
            IsSupported = true,
            TryResult = true
        };
        var service = CreateService(tempDir.Path, hostsPath, elevation);

        var canRequest = service.CanRequestElevation();
        var relaunched = service.TryRelaunchElevated(StartupAction.SaveRawHosts, startInBackground: true, payloadPath: "payload.txt");

        Assert.True(canRequest);
        Assert.True(relaunched);
        Assert.Equal(StartupAction.SaveRawHosts, elevation.LastAction);
        Assert.Equal("payload.txt", elevation.LastPayloadPath);
        Assert.True(elevation.LastStartInBackground);
    }

    [Fact]
    public async Task ExecuteStartupActionAsync_SaveRawHostsHandlesMissingAndExistingPayload()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "system-hosts");
        await File.WriteAllTextAsync(hostsPath, "127.0.0.1 localhost\n", cancellationToken);
        var service = CreateService(tempDir.Path, hostsPath);

        var missing = await service.ExecuteStartupActionAsync(
            StartupAction.SaveRawHosts,
            [],
            Path.Combine(tempDir.Path, "missing.txt"),
            cancellationToken);

        var payloadPath = Path.Combine(tempDir.Path, "pending.txt");
        await File.WriteAllTextAsync(payloadPath, "10.0.0.7 saved.test\n", cancellationToken);

        var saved = await service.ExecuteStartupActionAsync(
            StartupAction.SaveRawHosts,
            [],
            payloadPath,
            cancellationToken);

        Assert.False(missing.Performed);
        Assert.Equal("Pending system hosts content was not found.", missing.StatusMessage);
        Assert.True(saved.Performed);
        Assert.Equal("System hosts file saved.", saved.StatusMessage);
        Assert.False(File.Exists(payloadPath));
        Assert.Equal("10.0.0.7 saved.test\n", (await File.ReadAllTextAsync(hostsPath, cancellationToken)).Replace("\r\n", "\n"));
    }

    private static SystemHostsWorkflowService CreateService(
        string tempRoot,
        string hostsPath,
        TrackingWindowsElevationService? elevationService = null)
    {
        Directory.CreateDirectory(Path.Combine(tempRoot, "config"));
        return new SystemHostsWorkflowService(
            new HostsFileService(Path.Combine(tempRoot, "appdata"), hostsPath),
            new LocalSourceService(),
            new ProfileStore(Path.Combine(tempRoot, "config")),
            elevationService ?? new TrackingWindowsElevationService());
    }

    private sealed class TrackingWindowsElevationService : IWindowsElevationService
    {
        public bool IsSupported { get; set; }
        public bool IsElevated { get; set; }
        public bool TryResult { get; set; }
        public StartupAction? LastAction { get; private set; }
        public string? LastPayloadPath { get; private set; }
        public bool LastStartInBackground { get; private set; }

        public bool IsProcessElevated()
        {
            return IsElevated;
        }

        public bool TryRelaunchElevated(StartupAction action, string? payloadPath = null, bool startInBackground = false)
        {
            LastAction = action;
            LastPayloadPath = payloadPath;
            LastStartInBackground = startInBackground;
            return TryResult;
        }
    }
}
