using System;
using System.Threading.Tasks;

namespace HostsManager.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    partial void OnMinimizeToTrayOnCloseChanged(bool value)
    {
        if (isInitializing)
            return;

        _ = backgroundManagementService.PersistConfigurationIfChangedAsync(MinimizeToTrayOnClose, RunAtStartup, Profiles);
    }

    partial void OnRunAtStartupChanged(bool value)
    {
        if (isInitializing || isUpdatingRunAtStartup)
            return;

        _ = ApplyRunAtStartupPreferenceAsync(value);
    }

    private async Task EnsureRunAtStartupMatchesPreferenceAsync()
    {
        if (!startupRegistrationService.IsSupported)
            return;

        try
        {
            await startupRegistrationService.SetEnabledAsync(RunAtStartup);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Startup option failed: {ex.Message}";
        }
    }

    private async Task ApplyRunAtStartupPreferenceAsync(bool value)
    {
        if (!startupRegistrationService.IsSupported)
            return;

        try
        {
            await startupRegistrationService.SetEnabledAsync(value);
            await backgroundManagementService.PersistConfigurationIfChangedAsync(MinimizeToTrayOnClose, RunAtStartup, Profiles);
            StatusMessage = value
                ? "Startup enabled. Hosts Manager will launch at Windows sign-in."
                : "Startup disabled.";
        }
        catch (Exception ex)
        {
            isUpdatingRunAtStartup = true;
            RunAtStartup = !value;
            isUpdatingRunAtStartup = false;
            StatusMessage = $"Startup option failed: {ex.Message}";
        }
    }

    private async Task<bool> TryRelaunchElevatedAsync(StartupAction action, string? rawHostsContent = null)
    {
        var result = await startupActionOrchestrationService.TryRelaunchElevatedAsync(action, rawHostsContent);
        if (!string.IsNullOrWhiteSpace(result.StatusMessage))
            StatusMessage = result.StatusMessage;

        return result.Relaunched;
    }

    private async Task ApplyPendingStartupActionAsync()
    {
        var result = await startupActionOrchestrationService.ExecutePendingStartupActionAsync(Profiles);
        if (result is null)
            return;

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

        if (result.ClearPendingElevatedHostsUpdate)
            HasPendingElevatedHostsUpdate = false;

        StatusMessage = result.StatusMessage;
    }
}
