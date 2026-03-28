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

        var requestedName = (requestedFileName ?? string.Empty).Trim();

        try
        {
            await localSourceService.RenameAsync(SelectedProfile, requestedName);
            localSourcesDirty = true;
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
            await localSourceService.RecreateMissingFileAsync(profile);
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
        {
            return;
        }

        try
        {
            var source = await localSourceService.CreateNewSourceAsync(path);
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
            var source = await localSourceService.LoadExistingSourceAsync(path);
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

    private bool HandleMissingLocalSourceState(HostProfile source)
    {
        var changed = localSourceService.UpdateMissingFileState(source);
        if (!changed)
        {
            return false;
        }

        if (source.IsMissingLocalFile)
        {
            StatusMessage = $"Local source file not found. Source disabled: {source.Name}";
        }

        if (ReferenceEquals(source, SelectedProfile))
        {
            ReloadLocalSourceCommand.NotifyCanExecuteChanged();
            SaveEntriesToLocalCommand.NotifyCanExecuteChanged();
            SaveSelectedSourceCommand.NotifyCanExecuteChanged();
            RecreateMissingLocalFileCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(IsSelectedEntriesReadOnly));
            OnPropertyChanged(nameof(IsSelectedLocalFileMissing));
        }

        return true;
    }
}
