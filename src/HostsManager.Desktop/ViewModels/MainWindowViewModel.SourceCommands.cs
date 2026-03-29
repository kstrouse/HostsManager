using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using HostsManager.Desktop.Models;

namespace HostsManager.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task SaveProfilesAsync()
    {
        try
        {
            await profilePersistenceService.SaveConfigurationAndMarkSavedAsync(
                MinimizeToTrayOnClose,
                RunAtStartup,
                Profiles);
            await backgroundManagementCoordinator.RunNowAsync();
            StatusMessage = "Sources saved.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanSaveSelectedSource))]
    private async Task SaveSelectedSourceAsync()
    {
        if (SelectedProfile is null)
            return;

        if (SelectedProfile.SourceType == SourceType.Local)
        {
            if (string.IsNullOrWhiteSpace(SelectedProfile.LocalPath))
            {
                StatusMessage = "Local source path is empty.";
                return;
            }

            try
            {
                await File.WriteAllTextAsync(SelectedProfile.LocalPath, SelectedProfile.Entries ?? string.Empty);
                SelectedProfile.LastLoadedFromDiskEntries = SelectedProfile.Entries ?? string.Empty;
                localSourceWatcherService.MarkDirty();
                await SaveProfilesAsync();
                StatusMessage = $"Saved source: {SelectedProfile.Name}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Save local failed: {ex.Message}";
            }

            return;
        }

        if (SelectedProfile.SourceType == SourceType.System)
        {
            if (!IsSystemHostsEditingEnabled)
            {
                StatusMessage = "Enable system hosts editing before saving direct edits.";
                return;
            }

            await SaveSystemHostsDirectAsync(SelectedProfile);
            return;
        }

        if (SelectedProfile.IsReadOnly)
            return;

        await SaveProfilesAsync();
        StatusMessage = $"Saved source: {SelectedProfile.Name}";
    }

    private bool CanSaveSelectedSource() =>
        SelectedProfile switch
        {
            null => false,
            { SourceType: SourceType.Local, IsMissingLocalFile: true } => false,
            { SourceType: SourceType.System } => IsSystemHostsEditingEnabled,
            _ => !SelectedProfile.IsReadOnly
        };
}
