using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HostsManager.Desktop.Models;

namespace HostsManager.Desktop.Services;

public sealed class LocalSourceWatcherService : ILocalSourceWatcherService
{
    private readonly Dictionary<string, IFileSystemWatcherAdapter> watchers;
    private readonly IFileSystemWatcherFactory watcherFactory;
    private bool dirty;

    public LocalSourceWatcherService()
        : this(new FileSystemWatcherFactory())
    {
    }

    public LocalSourceWatcherService(IFileSystemWatcherFactory watcherFactory)
    {
        this.watcherFactory = watcherFactory ?? throw new ArgumentNullException(nameof(watcherFactory));
        watchers = new Dictionary<string, IFileSystemWatcherAdapter>(StringComparer.OrdinalIgnoreCase);
        dirty = true;
    }

    public void SyncWatchedSources(IEnumerable<HostProfile> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var wantedPaths = sources
            .Where(source => source.SourceType is SourceType.Local or SourceType.System)
            .Where(source => !string.IsNullOrWhiteSpace(source.LocalPath))
            .Select(source => NormalizePath(source.LocalPath))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toRemove = watchers.Keys.Where(path => !wantedPaths.Contains(path)).ToList();
        foreach (var path in toRemove)
        {
            watchers[path].Dispose();
            watchers.Remove(path);
        }

        foreach (var path in wantedPaths)
        {
            if (watchers.ContainsKey(path))
            {
                continue;
            }

            var directory = Path.GetDirectoryName(path);
            var fileName = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(directory) ||
                string.IsNullOrWhiteSpace(fileName) ||
                !Directory.Exists(directory))
            {
                continue;
            }

            var watcher = watcherFactory.Create(directory, fileName);
            watcher.Changed += OnSourceFileChanged;
            watcher.Created += OnSourceFileChanged;
            watcher.Renamed += OnSourceFileRenamed;
            watcher.Deleted += OnSourceFileChanged;
            watchers[path] = watcher;
        }
    }

    public void MarkDirty()
    {
        dirty = true;
    }

    public bool ConsumeDirty()
    {
        var result = dirty;
        dirty = false;
        return result;
    }

    public void Dispose()
    {
        foreach (var watcher in watchers.Values)
        {
            watcher.Dispose();
        }

        watchers.Clear();
    }

    private void OnSourceFileChanged(object? sender, FileSystemEventArgs e)
    {
        dirty = true;
    }

    private void OnSourceFileRenamed(object? sender, RenamedEventArgs e)
    {
        dirty = true;
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    public interface IFileSystemWatcherFactory
    {
        IFileSystemWatcherAdapter Create(string directory, string fileName);
    }

    public interface IFileSystemWatcherAdapter : IDisposable
    {
        event FileSystemEventHandler? Changed;
        event FileSystemEventHandler? Created;
        event RenamedEventHandler? Renamed;
        event FileSystemEventHandler? Deleted;
    }

    private sealed class FileSystemWatcherFactory : IFileSystemWatcherFactory
    {
        public IFileSystemWatcherAdapter Create(string directory, string fileName)
        {
            return new FileSystemWatcherAdapter(directory, fileName);
        }
    }

    private sealed class FileSystemWatcherAdapter : IFileSystemWatcherAdapter
    {
        private readonly FileSystemWatcher watcher;

        public FileSystemWatcherAdapter(string directory, string fileName)
        {
            watcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };
        }

        public event FileSystemEventHandler? Changed
        {
            add => watcher.Changed += value;
            remove => watcher.Changed -= value;
        }

        public event FileSystemEventHandler? Created
        {
            add => watcher.Created += value;
            remove => watcher.Created -= value;
        }

        public event RenamedEventHandler? Renamed
        {
            add => watcher.Renamed += value;
            remove => watcher.Renamed -= value;
        }

        public event FileSystemEventHandler? Deleted
        {
            add => watcher.Deleted += value;
            remove => watcher.Deleted -= value;
        }

        public void Dispose()
        {
            watcher.Dispose();
        }
    }
}
