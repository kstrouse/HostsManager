using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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
            return;

        var requestedName = (requestedFileName ?? string.Empty).Trim();

        try
        {
            await localSourceService.RenameAsync(SelectedProfile, requestedName);
            localSourceWatcherService.MarkDirty();
            OnPropertyChanged(nameof(SelectedProfile));
            OnPropertyChanged(nameof(SelectedLocalFilePath));
            OnPropertyChanged(nameof(SelectedLocalFolderPath));
            await SaveProfilesAsync();
            StatusMessage = $"Renamed local file to {Path.GetFileName(SelectedProfile.LocalPath)}.";
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = ex.Message;
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
            return;

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

    [RelayCommand(CanExecute = nameof(CanReloadLocalSource))]
    private async Task ReloadLocalSourceAsync()
    {
        if (SelectedProfile is null)
            return;

        if (SelectedProfile.SourceType != SourceType.Local || string.IsNullOrWhiteSpace(SelectedProfile.LocalPath))
        {
            StatusMessage = "Select a local source with a valid file path first.";
            return;
        }

        if (SelectedProfile.IsMissingLocalFile)
        {
            StatusMessage = $"Local source file not found: {SelectedProfile.LocalPath}";
            return;
        }

        try
        {
            SelectedProfile.Entries = await File.ReadAllTextAsync(SelectedProfile.LocalPath);
            OnPropertyChanged(nameof(SelectedProfile));
            localSourceWatcherService.MarkDirty();
            StatusMessage = "Reloaded entries from local file.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import failed: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanSaveEntriesToLocal))]
    private async Task SaveEntriesToLocalAsync()
    {
        if (SelectedProfile is null)
            return;

        if (SelectedProfile.SourceType != SourceType.Local || string.IsNullOrWhiteSpace(SelectedProfile.LocalPath))
        {
            StatusMessage = "Select a local source with a valid file path first.";
            return;
        }

        if (SelectedProfile.IsMissingLocalFile)
        {
            StatusMessage = $"Local source file not found: {SelectedProfile.LocalPath}";
            return;
        }

        try
        {
            await File.WriteAllTextAsync(SelectedProfile.LocalPath, SelectedProfile.Entries ?? string.Empty);
            SelectedProfile.LastLoadedFromDiskEntries = SelectedProfile.Entries ?? string.Empty;
            localSourceWatcherService.MarkDirty();
            await backgroundManagementCoordinator.RunNowAsync();
            StatusMessage = "Saved entries to local source file.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save local failed: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanRecreateMissingLocalFile))]
    private async Task RecreateMissingLocalFileAsync()
    {
        if (SelectedProfile is not { SourceType: SourceType.Local, IsMissingLocalFile: true } profile ||
            string.IsNullOrWhiteSpace(profile.LocalPath))
            return;

        try
        {
            await localSourceService.RecreateMissingFileAsync(profile);
            localSourceWatcherService.MarkDirty();

            OnPropertyChanged(nameof(SelectedProfile));
            OnPropertyChanged(nameof(IsSelectedEntriesReadOnly));
            OnPropertyChanged(nameof(IsSelectedLocalFileMissing));
            ReloadLocalSourceCommand.NotifyCanExecuteChanged();
            SaveEntriesToLocalCommand.NotifyCanExecuteChanged();
            SaveSelectedSourceCommand.NotifyCanExecuteChanged();
            RecreateMissingLocalFileCommand.NotifyCanExecuteChanged();

            await SaveProfilesAsync();
            await backgroundManagementCoordinator.RunNowAsync();
            StatusMessage = $"Re-created local source file: {Path.GetFileName(profile.LocalPath)}";
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Re-create local file failed: {ex.Message}";
        }
    }

    public async Task AddNewLocalSourceAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var source = await localSourceService.CreateNewSourceAsync(path);
            Profiles.Add(source);
            SelectedProfile = source;
            localSourceWatcherService.MarkDirty();
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
            return;

        try
        {
            var source = await localSourceService.LoadExistingSourceAsync(path);
            Profiles.Add(source);
            SelectedProfile = source;
            localSourceWatcherService.MarkDirty();
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
            return false;

        var isFileBacked = SelectedProfile.SourceType is SourceType.Local or SourceType.System;
        if (!isFileBacked)
            return false;

        var changed = await localSourceService.ReloadFromDiskAsync(SelectedProfile);
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

    private void ApplyMissingLocalSourceStateChanged(HostProfile source)
    {
        if (source.IsMissingLocalFile)
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

    private static ProcessStartInfo BuildOpenFolderStartInfo(string folder)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folder}\"",
                UseShellExecute = true
            };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return new ProcessStartInfo
            {
                FileName = "open",
                Arguments = $"\"{folder}\"",
                UseShellExecute = false
            };

        return new ProcessStartInfo
        {
            FileName = "xdg-open",
            Arguments = $"\"{folder}\"",
            UseShellExecute = false
        };
    }
}
