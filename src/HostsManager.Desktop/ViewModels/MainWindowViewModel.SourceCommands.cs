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

    [RelayCommand]
    private void NewRemoteSource() => AddRemoteSource(RemoteTransport.Https);

    public void AddRemoteSource(RemoteTransport remoteTransport)
    {
        var sourceIndex = Profiles.Count(source => !source.IsReadOnly) + 1;
        var profile = new HostProfile
        {
            Name = $"Remote Source {sourceIndex}",
            IsEnabled = true,
            SourceType = SourceType.Remote,
            RemoteTransport = remoteTransport,
            RefreshIntervalMinutes = "15",
            Entries = string.Empty
        };

        Profiles.Add(profile);
        SelectedProfile = profile;
        localSourceWatcherService.MarkDirty();
        StatusMessage = $"New remote source created ({GetRemoteTransportDisplay(remoteTransport)}).";
    }

    [RelayCommand(CanExecute = nameof(CanDeleteProfile))]
    private void DeleteProfile(HostProfile? source)
    {
        var target = source ?? SelectedProfile;
        if (target is null)
            return;

        if (target.IsReadOnly)
        {
            StatusMessage = "Read-only source cannot be deleted.";
            return;
        }

        var current = target;
        var index = Profiles.IndexOf(current);
        if (index < 0)
            return;

        Profiles.Remove(current);

        SelectedProfile = Profiles.Count == 0
            ? null
            : Profiles[Math.Clamp(index, 0, Profiles.Count - 1)];

        localSourceWatcherService.MarkDirty();
        StatusMessage = "Source removed.";
    }

    private bool CanDeleteProfile(HostProfile? source)
    {
        var target = source ?? SelectedProfile;
        return target is not null && !target.IsReadOnly;
    }

    private bool CanSaveSelectedSource() =>
        SelectedProfile switch
        {
            null => false,
            { SourceType: SourceType.Local, IsMissingLocalFile: true } => false,
            { SourceType: SourceType.System } => IsSystemHostsEditingEnabled,
            _ => !SelectedProfile.IsReadOnly
        };

    private static string GetRemoteTransportDisplay(RemoteTransport remoteTransport) =>
        remoteTransport switch
        {
            RemoteTransport.Http or RemoteTransport.Https => "HTTP/HTTPS",
            RemoteTransport.AzurePrivateDns => "Azure Private DNS",
            _ => remoteTransport.ToString()
        };
}
