using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using HostsManager.Desktop.Models;
using HostsManager.Desktop.Services;

namespace HostsManager.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task SyncAllFromUrlAsync()
    {
        await RefreshRemoteProfilesAsync(forceAll: true, userInitiated: true);
        await backgroundManagementCoordinator.RunNowAsync();
    }

    private async Task RefreshRemoteProfilesAsync(bool forceAll, bool userInitiated)
    {
        if (isRefreshRunning)
            return;

        isRefreshRunning = true;
        try
        {
            var result = await remoteSyncWorkflowService.RefreshProfilesAsync(
                new RemoteProfilesSyncRequest
                {
                    Profiles = Profiles.ToList(),
                    ForceAll = forceAll,
                    UserInitiated = userInitiated,
                    Now = DateTimeOffset.UtcNow
                },
                RemoteEditor.PrepareProfileForRemoteSyncAsync);
            await RemoteEditor.ApplyRemoteProfilesSyncResultAsync(result);
        }
        finally
        {
            isRefreshRunning = false;
        }
    }

    public async Task HandleRemoteSourceToggledAsync(HostProfile? source)
    {
        var result = await remoteSyncWorkflowService.HandleSourceEnabledAsync(
            source,
            SelectedProfile,
            RemoteEditor.PrepareProfileForRemoteSyncAsync);
        await RemoteEditor.ApplyRemoteSyncCommandResultAsync(result);
    }

    public async Task SyncRemoteSourceNowAsync(HostProfile? source)
    {
        if (source is null || source.SourceType != SourceType.Remote)
            return;

        if (IsQuickSyncRunning)
        {
            StatusMessage = "A remote sync is already running.";
            return;
        }

        quickSyncProfileId = source.Id;
        try
        {
            var result = await remoteSyncWorkflowService.SyncSourceNowAsync(
                source,
                SelectedProfile,
                isQuickSyncRunning: false,
                RemoteEditor.PrepareProfileForRemoteSyncAsync);
            await RemoteEditor.ApplyRemoteSyncCommandResultAsync(result);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Remote sync failed: {ex.Message}";
        }
        finally
        {
            quickSyncProfileId = null;
            OnPropertyChanged(nameof(IsQuickSyncRunning));
        }
    }
}
