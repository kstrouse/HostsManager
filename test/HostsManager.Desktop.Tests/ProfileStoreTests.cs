using System.Text.Json;

namespace HostsManager.Desktop.Tests;

public sealed class ProfileStoreTests
{
    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsConfig()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var tempDir = new TempDirectory();
        var store = new ProfileStore(tempDir.Path);
        var config = new AppConfig
        {
            MinimizeToTrayOnClose = true,
            RunAtStartup = true,
            Profiles =
            [
                new HostProfile
                {
                    Id = "local-1",
                    Name = "Local One",
                    SourceType = SourceType.Local,
                    LocalPath = System.IO.Path.Combine(tempDir.Path, "one.hosts"),
                    Entries = "127.0.0.1 local.test"
                }
            ]
        };

        await store.SaveAsync(config, cancellationToken);
        var loaded = await store.LoadAsync(cancellationToken);

        Assert.True(loaded.MinimizeToTrayOnClose);
        Assert.True(loaded.RunAtStartup);
        var profile = Assert.Single(loaded.Profiles);
        Assert.Equal("local-1", profile.Id);
        Assert.Equal("Local One", profile.Name);
        Assert.Equal(SourceType.Local, profile.SourceType);
        Assert.Equal("127.0.0.1 local.test", profile.Entries);
    }

    [Fact]
    public async Task LoadAsync_LegacyArrayPayload_LoadsProfiles()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var tempDir = new TempDirectory();
        var store = new ProfileStore(tempDir.Path);
        var profiles = new[]
        {
            new HostProfile
            {
                Id = "legacy-1",
                Name = "Legacy",
                SourceType = SourceType.Remote,
                RemoteTransport = RemoteTransport.Https,
                RemoteLocation = "https://example.test/hosts"
            }
        };

        var json = JsonSerializer.Serialize(profiles);
        await File.WriteAllTextAsync(store.ProfilesFilePath, json, cancellationToken);

        var loaded = await store.LoadAsync(cancellationToken);

        var profile = Assert.Single(loaded.Profiles);
        Assert.Equal("legacy-1", profile.Id);
        Assert.Equal("Legacy", profile.Name);
        Assert.Equal(RemoteTransport.Https, profile.RemoteTransport);
    }
}
