namespace HostsManager.Desktop.Tests;

public sealed class LocalSourceWatcherServiceTests
{
    [Fact]
    public void ConsumeDirty_TracksInitialAndManualDirtyState()
    {
        var service = new LocalSourceWatcherService(new TrackingWatcherFactory());

        Assert.True(service.ConsumeDirty());
        Assert.False(service.ConsumeDirty());

        service.MarkDirty();

        Assert.True(service.ConsumeDirty());
        Assert.False(service.ConsumeDirty());
    }

    [Fact]
    public void SyncWatchedSources_CreatesDistinctWatchersForFileBackedSourcesOnly()
    {
        using var tempDir = new TempDirectory();
        var localPath = Path.Combine(tempDir.Path, "local.hosts");
        var systemPath = Path.Combine(tempDir.Path, "system-hosts");
        File.WriteAllText(localPath, "127.0.0.1 local");
        File.WriteAllText(systemPath, "127.0.0.1 system");

        var factory = new TrackingWatcherFactory();
        using var service = new LocalSourceWatcherService(factory);
        service.ConsumeDirty();

        service.SyncWatchedSources(
        [
            new HostProfile { SourceType = SourceType.Local, LocalPath = localPath },
            new HostProfile { SourceType = SourceType.System, LocalPath = systemPath },
            new HostProfile { SourceType = SourceType.System, LocalPath = localPath },
            new HostProfile { SourceType = SourceType.Remote, RemoteLocation = "https://example.test/hosts" }
        ]);

        Assert.Equal(2, factory.Watchers.Count);
        Assert.Contains(factory.Watchers, watcher => watcher.Directory == tempDir.Path && watcher.FileName == "local.hosts");
        Assert.Contains(factory.Watchers, watcher => watcher.Directory == tempDir.Path && watcher.FileName == "system-hosts");
    }

    [Fact]
    public void SyncWatchedSources_RemovesObsoleteWatchers_AndFileEventsMarkDirty()
    {
        using var tempDir = new TempDirectory();
        var localPath = Path.Combine(tempDir.Path, "local.hosts");
        File.WriteAllText(localPath, "127.0.0.1 local");

        var factory = new TrackingWatcherFactory();
        using var service = new LocalSourceWatcherService(factory);
        service.ConsumeDirty();
        service.SyncWatchedSources([new HostProfile { SourceType = SourceType.Local, LocalPath = localPath }]);

        var watcher = Assert.Single(factory.Watchers);
        watcher.RaiseChanged(localPath);
        Assert.True(service.ConsumeDirty());
        watcher.RaiseCreated(localPath);
        Assert.True(service.ConsumeDirty());
        watcher.RaiseRenamed(localPath, Path.Combine(tempDir.Path, "renamed.hosts"));
        Assert.True(service.ConsumeDirty());
        watcher.RaiseDeleted(localPath);
        Assert.True(service.ConsumeDirty());

        service.SyncWatchedSources([]);

        Assert.True(watcher.IsDisposed);
    }

    private sealed class TrackingWatcherFactory : LocalSourceWatcherService.IFileSystemWatcherFactory
    {
        public List<TrackingWatcher> Watchers { get; } = [];

        public LocalSourceWatcherService.IFileSystemWatcherAdapter Create(string directory, string fileName)
        {
            var watcher = new TrackingWatcher(directory, fileName);
            Watchers.Add(watcher);
            return watcher;
        }
    }

    private sealed class TrackingWatcher : LocalSourceWatcherService.IFileSystemWatcherAdapter
    {
        public TrackingWatcher(string directory, string fileName)
        {
            Directory = directory;
            FileName = fileName;
        }

        public string Directory { get; }
        public string FileName { get; }
        public bool IsDisposed { get; private set; }

        public event FileSystemEventHandler? Changed;
        public event FileSystemEventHandler? Created;
        public event RenamedEventHandler? Renamed;
        public event FileSystemEventHandler? Deleted;

        public void Dispose()
        {
            IsDisposed = true;
        }

        public void RaiseChanged(string fullPath)
        {
            Changed?.Invoke(this, new FileSystemEventArgs(WatcherChangeTypes.Changed, Path.GetDirectoryName(fullPath)!, Path.GetFileName(fullPath)));
        }

        public void RaiseCreated(string fullPath)
        {
            Created?.Invoke(this, new FileSystemEventArgs(WatcherChangeTypes.Created, Path.GetDirectoryName(fullPath)!, Path.GetFileName(fullPath)));
        }

        public void RaiseRenamed(string oldFullPath, string newFullPath)
        {
            Renamed?.Invoke(
                this,
                new RenamedEventArgs(
                    WatcherChangeTypes.Renamed,
                    Path.GetDirectoryName(newFullPath)!,
                    Path.GetFileName(newFullPath),
                    Path.GetFileName(oldFullPath)));
        }

        public void RaiseDeleted(string fullPath)
        {
            Deleted?.Invoke(this, new FileSystemEventArgs(WatcherChangeTypes.Deleted, Path.GetDirectoryName(fullPath)!, Path.GetFileName(fullPath)));
        }
    }
}
