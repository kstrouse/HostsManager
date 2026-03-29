using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using HostsManager.Desktop.Models;
using HostsManager.Desktop.Services;

namespace HostsManager.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    [RelayCommand(CanExecute = nameof(CanReadSelectedRemoteHosts))]
    private async Task SyncFromUrlAsync()
    {
        IsSelectedRemoteSyncRunning = true;
        try
        {
            var result = await remoteSyncWorkflowService.SyncSelectedSourceAsync(
                SelectedProfile,
                PrepareProfileForRemoteSyncAsync);
            await ApplyRemoteSyncCommandResultAsync(result);
        }
        finally
        {
            IsSelectedRemoteSyncRunning = false;
        }
    }

    [RelayCommand]
    private async Task ReadSelectedRemoteHostsAsync()
    {
        await SyncFromUrlAsync();
    }

    private bool CanReadSelectedRemoteHosts() =>
        SelectedProfile is { SourceType: SourceType.Remote } && !IsSelectedRemoteSyncRunning;

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
                PrepareProfileForRemoteSyncAsync);
            await ApplyRemoteProfilesSyncResultAsync(result);
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
            PrepareProfileForRemoteSyncAsync);
        await ApplyRemoteSyncCommandResultAsync(result);
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
                PrepareProfileForRemoteSyncAsync);
            await ApplyRemoteSyncCommandResultAsync(result);
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

    private async Task PrepareProfileForRemoteSyncAsync(HostProfile profile, CancellationToken cancellationToken)
    {
        if (profile.RemoteTransport == RemoteTransport.AzurePrivateDns &&
            ReferenceEquals(profile, SelectedProfile))
        {
            await LoadAndApplyAzureZonesAsync(
                () => azureProfileCommandService.RefreshZonesForSelectionAsync(profile, cancellationToken));
        }
    }

    private async Task ApplyRemoteProfilesSyncResultAsync(RemoteProfilesSyncResult result)
    {
        if (result.ShouldPersistConfiguration)
            await profilePersistenceService.SaveConfigurationAsync(
                MinimizeToTrayOnClose,
                RunAtStartup,
                Profiles);

        if (result.ShouldNotifySelectedProfileChanged)
            OnPropertyChanged(nameof(SelectedProfile));

        if (result.ShouldRunBackgroundManagement)
            await backgroundManagementCoordinator.RunNowAsync();

        if (!string.IsNullOrWhiteSpace(result.StatusMessage))
            StatusMessage = result.StatusMessage;
    }

    private async Task ApplyRemoteSyncCommandResultAsync(RemoteSourceSyncCommandResult result)
    {
        if (result.ShouldPersistConfiguration)
            await profilePersistenceService.SaveConfigurationAsync(
                MinimizeToTrayOnClose,
                RunAtStartup,
                Profiles);

        if (result.ShouldNotifySelectedProfileChanged)
            OnPropertyChanged(nameof(SelectedProfile));

        if (result.ShouldRunBackgroundManagement)
            await backgroundManagementCoordinator.RunNowAsync();

        if (!string.IsNullOrWhiteSpace(result.StatusMessage))
            StatusMessage = result.StatusMessage;
    }
}
