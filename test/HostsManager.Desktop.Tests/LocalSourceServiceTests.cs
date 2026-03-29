namespace HostsManager.Desktop.Tests;

public sealed class LocalSourceServiceTests
{
    [Fact]
    public async Task CreateNewSourceAsync_CreatesFileAndProfile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var tempDir = new TempDirectory();
        var service = new LocalSourceService();
        var path = Path.Combine(tempDir.Path, "new.hosts");

        var profile = await service.CreateNewSourceAsync(path, cancellationToken);

        Assert.True(File.Exists(path));
        Assert.Equal(SourceType.Local, profile.SourceType);
        Assert.Equal(path, profile.LocalPath);
        Assert.Equal(profile.Entries, profile.LastLoadedFromDiskEntries);
    }

    [Fact]
    public async Task RenameAsync_PreservesExtensionAndUpdatesPath()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var tempDir = new TempDirectory();
        var path = Path.Combine(tempDir.Path, "old.hosts");
        await File.WriteAllTextAsync(path, "127.0.0.1 old.local", cancellationToken);
        var service = new LocalSourceService();
        var profile = await service.LoadExistingSourceAsync(path, cancellationToken);

        await service.RenameAsync(profile, "renamed", cancellationToken);

        Assert.Equal(Path.Combine(tempDir.Path, "renamed.hosts"), profile.LocalPath);
        Assert.True(File.Exists(profile.LocalPath));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task ReloadFromDiskAsync_UpdatesEntriesAndBaseline()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var tempDir = new TempDirectory();
        var path = Path.Combine(tempDir.Path, "reload.hosts");
        await File.WriteAllTextAsync(path, "127.0.0.1 before.local", cancellationToken);
        var service = new LocalSourceService();
        var profile = await service.LoadExistingSourceAsync(path, cancellationToken);
        await File.WriteAllTextAsync(path, "127.0.0.1 after.local", cancellationToken);

        var changed = await service.ReloadFromDiskAsync(profile, cancellationToken);

        Assert.True(changed);
        Assert.Equal("127.0.0.1 after.local", profile.Entries);
        Assert.Equal(profile.Entries, profile.LastLoadedFromDiskEntries);
    }

    [Fact]
    public void UpdateMissingFileState_MarksMissingLocalSourceDisabled()
    {
        using var tempDir = new TempDirectory();
        var path = Path.Combine(tempDir.Path, "missing.hosts");
        var service = new LocalSourceService();
        var profile = new HostProfile
        {
            Name = "Missing",
            SourceType = SourceType.Local,
            LocalPath = path,
            IsEnabled = true
        };

        var changed = service.UpdateMissingFileState(profile);

        Assert.True(changed);
        Assert.True(profile.IsMissingLocalFile);
        Assert.False(profile.IsEnabled);
    }
}
