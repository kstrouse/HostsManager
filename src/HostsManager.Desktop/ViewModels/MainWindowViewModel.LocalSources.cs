using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using HostsManager.Desktop.Models;

namespace HostsManager.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private bool CanReloadLocalSource() =>
        SelectedProfile is not null &&
        SelectedProfile.SourceType == SourceType.Local &&
        !SelectedProfile.IsMissingLocalFile &&
        !string.IsNullOrWhiteSpace(SelectedProfile.LocalPath);

    private bool CanSaveEntriesToLocal() =>
        SelectedProfile is not null &&
        SelectedProfile.SourceType == SourceType.Local &&
        !SelectedProfile.IsMissingLocalFile &&
        !string.IsNullOrWhiteSpace(SelectedProfile.LocalPath);

    private bool CanRecreateMissingLocalFile() =>
        SelectedProfile is { SourceType: SourceType.Local, IsMissingLocalFile: true } profile &&
        !string.IsNullOrWhiteSpace(profile.LocalPath);

    public async Task RenameSelectedLocalFileAsync(string? requestedFileName)
    {
        if (SelectedProfile is null || SelectedProfile.SourceType != SourceType.Local)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedProfile.LocalPath))
        {
            StatusMessage = "Local source path is empty.";
            return;
        }

        var requestedName = (requestedFileName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(requestedName))
        {
            StatusMessage = "Enter a file name first.";
            return;
        }

        if (requestedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            StatusMessage = "File name contains invalid characters.";
            return;
        }

        var currentPath = SelectedProfile.LocalPath;
        var directory = Path.GetDirectoryName(currentPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            StatusMessage = "Local source folder not found.";
            return;
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
            StatusMessage = "File name unchanged.";
            return;
        }

        if (File.Exists(targetPath))
        {
            StatusMessage = "A file with that name already exists.";
            return;
        }

        try
        {
            File.Move(currentPath, targetPath);
            SelectedProfile.LocalPath = targetPath;
            localSourcesDirty = true;
            OnPropertyChanged(nameof(SelectedProfile));
            OnPropertyChanged(nameof(SelectedLocalFilePath));
            OnPropertyChanged(nameof(SelectedLocalFolderPath));
            await SaveProfilesAsync();
            StatusMessage = $"Renamed local file to {Path.GetFileName(targetPath)}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Rename failed: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenSelectedLocalFolder))]
    private void OpenSelectedLocalFolder()
    {
        if (SelectedProfile is null || SelectedProfile.SourceType != SourceType.Local)
        {
            return;
        }

        try
        {
            var folder = Path.GetDirectoryName(SelectedProfile.LocalPath);
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                StatusMessage = "Local source folder not found.";
                return;
            }

            Process.Start(BuildOpenFolderStartInfo(folder));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Open folder failed: {ex.Message}";
        }
    }

    private bool CanOpenSelectedLocalFolder() =>
        SelectedProfile is { SourceType: SourceType.Local } profile &&
        !string.IsNullOrWhiteSpace(profile.LocalPath);

    [RelayCommand(CanExecute = nameof(CanRecreateMissingLocalFile))]
    private async Task RecreateMissingLocalFileAsync()
    {
        if (SelectedProfile is not { SourceType: SourceType.Local, IsMissingLocalFile: true } profile ||
            string.IsNullOrWhiteSpace(profile.LocalPath))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(profile.LocalPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                StatusMessage = "Local source folder not found.";
                return;
            }

            Directory.CreateDirectory(directory);

            var content = profile.LastLoadedFromDiskEntries ?? profile.Entries ?? string.Empty;
            await File.WriteAllTextAsync(profile.LocalPath, content);

            profile.Entries = content;
            profile.LastLoadedFromDiskEntries = content;
            profile.IsMissingLocalFile = false;
            profile.IsEnabled = true;
            localSourcesDirty = true;

            OnPropertyChanged(nameof(SelectedProfile));
            OnPropertyChanged(nameof(IsSelectedEntriesReadOnly));
            OnPropertyChanged(nameof(IsSelectedLocalFileMissing));
            ReloadLocalSourceCommand.NotifyCanExecuteChanged();
            SaveEntriesToLocalCommand.NotifyCanExecuteChanged();
            SaveSelectedSourceCommand.NotifyCanExecuteChanged();
            RecreateMissingLocalFileCommand.NotifyCanExecuteChanged();

            await SaveProfilesAsync();
            await RunBackgroundManagementTickAsync();
            StatusMessage = $"Re-created local source file: {Path.GetFileName(profile.LocalPath)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Re-create local file failed: {ex.Message}";
        }
    }

    public async Task AddNewLocalSourceAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var initial = "# New local hosts source" + Environment.NewLine;
            await File.WriteAllTextAsync(path, initial);

            var source = new HostProfile
            {
                Name = Path.GetFileNameWithoutExtension(path),
                IsEnabled = true,
                SourceType = SourceType.Local,
                LocalPath = path,
                Entries = initial,
                LastLoadedFromDiskEntries = initial
            };

            Profiles.Add(source);
            SelectedProfile = source;
            localSourcesDirty = true;
            StatusMessage = "Local source created and added.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Create local source failed: {ex.Message}";
        }
    }

    public async Task AddExistingLocalSourceAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var text = await File.ReadAllTextAsync(path);
            var source = new HostProfile
            {
                Name = Path.GetFileNameWithoutExtension(path),
                IsEnabled = true,
                SourceType = SourceType.Local,
                LocalPath = path,
                Entries = text,
                LastLoadedFromDiskEntries = text
            };

            Profiles.Add(source);
            SelectedProfile = source;
            localSourcesDirty = true;
            StatusMessage = "Existing local source added.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Add local source failed: {ex.Message}";
        }
    }

    public async Task<bool> ReloadSelectedSourceFromDiskAsync()
    {
        if (SelectedProfile is null)
        {
            return false;
        }

        var isFileBacked = SelectedProfile.SourceType is SourceType.Local or SourceType.System;
        if (!isFileBacked)
        {
            return false;
        }

        var changed = await TryReloadSourceFromDiskAsync(SelectedProfile);
        DismissSelectedSourceExternalChangeNotification();

        if (changed)
        {
            OnPropertyChanged(nameof(SelectedProfile));
            StatusMessage = $"Reloaded external changes for {SelectedProfile.Name}.";
        }

        return changed;
    }

    public void DismissSelectedSourceExternalChangeNotification()
    {
        SelectedSourceChangedExternally = false;
        SelectedSourceExternalChangeName = string.Empty;
    }

    private static async Task<bool> TryHasDiskContentChangedAsync(HostProfile source)
    {
        if (string.IsNullOrWhiteSpace(source.LocalPath) || !File.Exists(source.LocalPath))
        {
            return false;
        }

        try
        {
            var text = await File.ReadAllTextAsync(source.LocalPath);
            var baseline = source.LastLoadedFromDiskEntries;
            if (baseline is null)
            {
                baseline = source.Entries;
            }

            return !string.Equals(text, baseline, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> TryReloadSourceFromDiskAsync(HostProfile source)
    {
        if (string.IsNullOrWhiteSpace(source.LocalPath) || !File.Exists(source.LocalPath))
        {
            return false;
        }

        try
        {
            var text = await File.ReadAllTextAsync(source.LocalPath);
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

    private bool HandleMissingLocalSourceState(HostProfile source)
    {
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

            if (changed)
            {
                StatusMessage = $"Local source file not found. Source disabled: {source.Name}";
                if (ReferenceEquals(source, SelectedProfile))
                {
                    ReloadLocalSourceCommand.NotifyCanExecuteChanged();
                    SaveEntriesToLocalCommand.NotifyCanExecuteChanged();
                    SaveSelectedSourceCommand.NotifyCanExecuteChanged();
                    RecreateMissingLocalFileCommand.NotifyCanExecuteChanged();
                    OnPropertyChanged(nameof(IsSelectedEntriesReadOnly));
                    OnPropertyChanged(nameof(IsSelectedLocalFileMissing));
                }
            }

            return changed;
        }

        var wasMissing = source.IsMissingLocalFile;
        source.IsMissingLocalFile = false;
        if (wasMissing && ReferenceEquals(source, SelectedProfile))
        {
            ReloadLocalSourceCommand.NotifyCanExecuteChanged();
            SaveEntriesToLocalCommand.NotifyCanExecuteChanged();
            SaveSelectedSourceCommand.NotifyCanExecuteChanged();
            RecreateMissingLocalFileCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(IsSelectedEntriesReadOnly));
            OnPropertyChanged(nameof(IsSelectedLocalFileMissing));
        }

        return false;
    }
}
