using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HostsManager.Desktop.Models;

namespace HostsManager.Desktop.Services;

public sealed class LocalSourceService : ILocalSourceService
{
    public async Task<HostProfile> CreateNewSourceAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var initial = "# New local hosts source" + Environment.NewLine;
        await File.WriteAllTextAsync(path, initial, cancellationToken);

        return new HostProfile
        {
            Name = Path.GetFileNameWithoutExtension(path),
            IsEnabled = true,
            SourceType = SourceType.Local,
            LocalPath = path,
            Entries = initial,
            LastLoadedFromDiskEntries = initial
        };
    }

    public async Task<HostProfile> LoadExistingSourceAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var text = await File.ReadAllTextAsync(path, cancellationToken);
        return new HostProfile
        {
            Name = Path.GetFileNameWithoutExtension(path),
            IsEnabled = true,
            SourceType = SourceType.Local,
            LocalPath = path,
            Entries = text,
            LastLoadedFromDiskEntries = text
        };
    }

    public Task RenameAsync(HostProfile source, string requestedFileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (string.IsNullOrWhiteSpace(source.LocalPath))
        {
            throw new InvalidOperationException("Local source path is empty.");
        }

        var requestedName = requestedFileName.Trim();
        if (string.IsNullOrWhiteSpace(requestedName))
        {
            throw new InvalidOperationException("Enter a file name first.");
        }

        if (requestedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException("File name contains invalid characters.");
        }

        var currentPath = source.LocalPath;
        var directory = Path.GetDirectoryName(currentPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            throw new InvalidOperationException("Local source folder not found.");
        }

        var currentExtension = Path.GetExtension(currentPath);
        var targetFileName = requestedName;
        if (string.IsNullOrWhiteSpace(Path.GetExtension(targetFileName)) && !string.IsNullOrWhiteSpace(currentExtension))
        {
            targetFileName += currentExtension;
        }

        var targetPath = Path.Combine(directory, targetFileName);
        if (string.Equals(currentPath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("File name unchanged.");
        }

        if (File.Exists(targetPath))
        {
            throw new InvalidOperationException("A file with that name already exists.");
        }

        File.Move(currentPath, targetPath);
        source.LocalPath = targetPath;
        return Task.CompletedTask;
    }

    public async Task RecreateMissingFileAsync(HostProfile source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (string.IsNullOrWhiteSpace(source.LocalPath))
        {
            throw new InvalidOperationException("Local source folder not found.");
        }

        var directory = Path.GetDirectoryName(source.LocalPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Local source folder not found.");
        }

        Directory.CreateDirectory(directory);

        var content = source.LastLoadedFromDiskEntries ?? source.Entries ?? string.Empty;
        await File.WriteAllTextAsync(source.LocalPath, content, cancellationToken);

        source.Entries = content;
        source.LastLoadedFromDiskEntries = content;
        source.IsMissingLocalFile = false;
        source.IsEnabled = true;
    }

    public async Task<bool> HasDiskContentChangedAsync(HostProfile source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (string.IsNullOrWhiteSpace(source.LocalPath) || !File.Exists(source.LocalPath))
        {
            return false;
        }

        try
        {
            var text = await File.ReadAllTextAsync(source.LocalPath, cancellationToken);
            var baseline = source.LastLoadedFromDiskEntries ?? source.Entries;
            return !string.Equals(text, baseline, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ReloadFromDiskAsync(HostProfile source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (string.IsNullOrWhiteSpace(source.LocalPath) || !File.Exists(source.LocalPath))
        {
            return false;
        }

        try
        {
            var text = await File.ReadAllTextAsync(source.LocalPath, cancellationToken);
            source.LastLoadedFromDiskEntries = text;
            if (string.Equals(text, source.Entries, StringComparison.Ordinal))
            {
                return false;
            }

            source.Entries = text;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool UpdateMissingFileState(HostProfile source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.SourceType != SourceType.Local || string.IsNullOrWhiteSpace(source.LocalPath))
        {
            return false;
        }

        var exists = File.Exists(source.LocalPath);
        if (!exists)
        {
            var changed = !source.IsMissingLocalFile || source.IsEnabled;
            source.IsMissingLocalFile = true;
            source.IsEnabled = false;
            return changed;
        }

        var wasMissing = source.IsMissingLocalFile;
        source.IsMissingLocalFile = false;
        return wasMissing;
    }
}
