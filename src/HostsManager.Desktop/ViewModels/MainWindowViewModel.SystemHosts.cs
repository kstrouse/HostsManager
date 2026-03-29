using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using HostsManager.Desktop.Models;
using HostsManager.Desktop.Services;

namespace HostsManager.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task ApplyToSystemHostsAsync()
    {
        if (await TryRelaunchElevatedAsync(StartupAction.ApplyManagedHosts))
            return;

        var result = await systemHostsCommandService.ApplyManagedHostsAsync(Profiles);
        await ApplySystemHostsCommandResultAsync(result);
    }

    [RelayCommand]
    private async Task RestoreBackupAsync()
    {
        if (await TryRelaunchElevatedAsync(StartupAction.RestoreBackup))
            return;

        var result = await systemHostsCommandService.RestoreBackupAsync();
        await ApplySystemHostsCommandResultAsync(result);
    }

    private async Task SaveSystemHostsDirectAsync(HostProfile profile)
    {
        if (await TryRelaunchElevatedAsync(StartupAction.SaveRawHosts, profile.Entries ?? string.Empty))
            return;

        var result = await systemHostsCommandService.SaveRawHostsAsync(profile.Entries ?? string.Empty);
        await ApplySystemHostsCommandResultAsync(result);
    }

    private async Task ApplySystemHostsCommandResultAsync(SystemHostsCommandResult result)
    {
        if (result.RefreshSystemSourceSnapshot)
            await RefreshSystemHostsSourceSnapshotAsync(announceWhenChanged: false);

        if (result.MarkManagedApplySucceeded)
            hostsStateTracker.MarkManagedApplySucceeded(Profiles);

        if (result.MarkLocalSourcesDirty)
            localSourceWatcherService.MarkDirty();

        if (result.DisableSystemHostsEditing)
            IsSystemHostsEditingEnabled = false;

        if (result.DismissSelectedSourceExternalChangeNotification)
            DismissSelectedSourceExternalChangeNotification();

        if (result.PendingElevatedHostsUpdate.HasValue)
            HasPendingElevatedHostsUpdate = result.PendingElevatedHostsUpdate.Value;

        if (!string.IsNullOrWhiteSpace(result.StatusMessage))
            StatusMessage = result.StatusMessage;
    }

    private async Task<bool> RefreshSystemHostsSourceSnapshotAsync(bool announceWhenChanged, bool skipSelectedProfile = false)
    {
        var systemSource = GetSystemSource();
        var refreshResult = await localSourceRefreshService.RefreshSystemSourceAsync(
            systemSource,
            SelectedProfile,
            announceWhenChanged,
            skipSelectedProfile);

        if (refreshResult.ExternalChangeDetected && systemSource is not null)
            SetSelectedSourceExternalChangeNotification(systemSource);

        if (refreshResult.SelectedProfileChanged)
        {
            var current = SelectedProfile;
            SelectedProfile = null;
            SelectedProfile = current;
        }

        return refreshResult.Changed;
    }

    private HostProfile? GetSystemSource() =>
        Profiles.FirstOrDefault(source => source.SourceType == SourceType.System && source.IsReadOnly);

    private void SetSelectedSourceExternalChangeNotification(HostProfile source)
    {
        SelectedSourceExternalChangeName = source.Name;
        SelectedSourceChangedExternally = true;
    }

    private void InitializeManagedStateFromSystemHosts()
    {
        hostsStateTracker.InitializeManagedState(Profiles, managedHostsMatch: !NeedsElevatedApply());
        HasPendingElevatedHostsUpdate = false;
    }

    private bool NeedsElevatedApply() => systemHostsWorkflowService.NeedsManagedApply(Profiles, GetSystemSource());
}
