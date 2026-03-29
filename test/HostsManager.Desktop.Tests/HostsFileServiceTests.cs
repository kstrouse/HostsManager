namespace HostsManager.Desktop.Tests;

public sealed class HostsFileServiceTests
{
    [Fact]
    public async Task ApplySourcesAsync_WritesManagedBlock_AndPreservesBackup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "hosts");
        var appDataPath = Path.Combine(tempDir.Path, "appdata");
        var service = new HostsFileService(appDataPath, hostsPath);

        await File.WriteAllTextAsync(hostsPath, "127.0.0.1 localhost\n# existing\n", cancellationToken);

        await service.ApplySourcesAsync(
        [
            new HostProfile
            {
                Name = "Enabled Local",
                IsEnabled = true,
                IsReadOnly = false,
                Entries = "10.0.0.1 api.local\n# keep comment\n10.0.0.2 db.local"
            },
            new HostProfile
            {
                Name = "Disabled",
                IsEnabled = false,
                Entries = "10.0.0.3 disabled.local"
            },
            new HostProfile
            {
                Name = "Comments Only",
                IsEnabled = true,
                Entries = "# comment only"
            }
        ], cancellationToken: cancellationToken);

        var hosts = await File.ReadAllTextAsync(hostsPath, cancellationToken);
        var backup = await File.ReadAllTextAsync(service.GetBackupFilePath(), cancellationToken);

        Assert.Contains("127.0.0.1 localhost", hosts);
        Assert.Contains("# source: Enabled Local", hosts);
        Assert.Contains("10.0.0.1 api.local", hosts);
        Assert.Contains("10.0.0.2 db.local", hosts);
        Assert.DoesNotContain("disabled.local", hosts);
        Assert.DoesNotContain("# source: Comments Only", hosts);
        Assert.Equal("127.0.0.1 localhost\n# existing\n", backup.Replace("\r\n", "\n"));
    }

    [Fact]
    public Task ManagedHostsMatch_IgnoresLineEndingDifferences()
    {
        var service = new HostsFileService(Path.GetTempPath(), Path.Combine(Path.GetTempPath(), $"hosts-{Guid.NewGuid():N}"));
        var sources = new[]
        {
            new HostProfile
            {
                Name = "Remote",
                IsEnabled = true,
                Entries = "10.1.0.1 api.remote\n10.1.0.2 db.remote"
            }
        };

        var actual = "127.0.0.1 localhost\r\n\r\n# >>> Hosts Manager managed entries >>>\r\n# source: Remote\r\n10.1.0.1 api.remote\r\n10.1.0.2 db.remote\r\n\r\n# <<< Hosts Manager managed entries <<<\r\n";

        Assert.True(service.ManagedHostsMatch(sources, actual));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task RestoreBackupAsync_ReplacesHostsWithBackup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var tempDir = new TempDirectory();
        var hostsPath = Path.Combine(tempDir.Path, "hosts");
        var appDataPath = Path.Combine(tempDir.Path, "appdata");
        var service = new HostsFileService(appDataPath, hostsPath);

        await File.WriteAllTextAsync(hostsPath, "before", cancellationToken);
        await service.ApplySourcesAsync(
        [
            new HostProfile { Name = "One", IsEnabled = true, Entries = "10.0.0.1 one.test" }
        ], cancellationToken: cancellationToken);

        await File.WriteAllTextAsync(hostsPath, "mutated", cancellationToken);
        await service.RestoreBackupAsync(cancellationToken: cancellationToken);

        var restored = await File.ReadAllTextAsync(hostsPath, cancellationToken);
        Assert.Equal("before", restored);
    }
}
