using System;
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
            OnPropertyChanged(nameof(IsSelectedEntriesReadOnly));
        }
    }
}
