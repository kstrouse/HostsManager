using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HostsManager.Desktop.Models;

namespace HostsManager.Desktop.Services;

public sealed class RemoteProfilesSyncRequest
{
    public IReadOnlyList<HostProfile> Profiles { get; init; } = [];
    public bool ForceAll { get; init; }
    public bool UserInitiated { get; init; }
    public DateTimeOffset Now { get; init; }
}

public sealed class RemoteProfilesSyncResult
{
    public int SyncedCount { get; init; }
    public int ErrorCount { get; init; }
    public bool ShouldPersistConfiguration { get; init; }
    public bool ShouldNotifySelectedProfileChanged { get; init; }
    public bool ShouldRunBackgroundManagement { get; init; }
    public string? StatusMessage { get; init; }
}

public sealed class RemoteSourceSyncCommandResult
{
    public bool ShouldPersistConfiguration { get; init; }
    public bool ShouldNotifySelectedProfileChanged { get; init; }
    public bool ShouldRunBackgroundManagement { get; init; }
    public string? StatusMessage { get; init; }
}

public sealed class RemoteSyncWorkflowService : IRemoteSyncWorkflowService
{
    private readonly IRemoteSourceSyncService remoteSourceSyncService;

    public RemoteSyncWorkflowService(IRemoteSourceSyncService remoteSourceSyncService)
    {
        this.remoteSourceSyncService = remoteSourceSyncService ?? throw new ArgumentNullException(nameof(remoteSourceSyncService));
    }

    public async Task<RemoteProfilesSyncResult> RefreshProfilesAsync(
        RemoteProfilesSyncRequest request,
        Func<HostProfile, CancellationToken, Task>? beforeSyncAsync = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var syncedCount = 0;
        var errorCount = 0;

        foreach (var profile in request.Profiles)
        {
            if (profile.SourceType != SourceType.Remote)
            {
                continue;
            }

            if (!remoteSourceSyncService.CanSyncRemoteSource(profile))
            {
                continue;
            }

            if (!request.ForceAll && !remoteSourceSyncService.ShouldAutoRefresh(profile, request.Now))
            {
                continue;
            }

            try
            {
                if (beforeSyncAsync is not null)
                {
                    await beforeSyncAsync(profile, cancellationToken);
                }

                if (await remoteSourceSyncService.SyncProfileAsync(profile, cancellationToken))
                {
                    syncedCount++;
                }
            }
            catch
            {
                errorCount++;
            }
        }

        return new RemoteProfilesSyncResult
        {
            SyncedCount = syncedCount,
            ErrorCount = errorCount,
            ShouldPersistConfiguration = syncedCount > 0,
            ShouldNotifySelectedProfileChanged = syncedCount > 0,
            ShouldRunBackgroundManagement = syncedCount > 0,
            StatusMessage = BuildRefreshStatusMessage(request.UserInitiated, syncedCount, errorCount)
        };
    }

    public async Task<RemoteSourceSyncCommandResult> SyncSelectedSourceAsync(
        HostProfile? selectedProfile,
        Func<HostProfile, CancellationToken, Task>? beforeSyncAsync = null,
        CancellationToken cancellationToken = default)
    {
        if (selectedProfile is null)
        {
            return new RemoteSourceSyncCommandResult();
        }

        if (beforeSyncAsync is not null)
        {
            await beforeSyncAsync(selectedProfile, cancellationToken);
        }

        var synced = await remoteSourceSyncService.SyncProfileAsync(selectedProfile, cancellationToken);
        return new RemoteSourceSyncCommandResult
        {
            ShouldPersistConfiguration = synced,
            ShouldNotifySelectedProfileChanged = synced,
            ShouldRunBackgroundManagement = synced,
            StatusMessage = synced
                ? "Synced selected remote source."
                : "Sync skipped. Configure a valid remote source first."
        };
    }

    public async Task<RemoteSourceSyncCommandResult> HandleSourceEnabledAsync(
        HostProfile? source,
        HostProfile? selectedProfile,
        Func<HostProfile, CancellationToken, Task>? beforeSyncAsync = null,
        CancellationToken cancellationToken = default)
    {
        if (source is null || source.SourceType != SourceType.Remote || !source.IsEnabled)
        {
            return new RemoteSourceSyncCommandResult();
        }

        if (beforeSyncAsync is not null)
        {
            await beforeSyncAsync(source, cancellationToken);
        }

        var synced = await remoteSourceSyncService.SyncProfileAsync(source, cancellationToken);
        return new RemoteSourceSyncCommandResult
        {
            ShouldPersistConfiguration = true,
            ShouldNotifySelectedProfileChanged = ReferenceEquals(source, selectedProfile),
            StatusMessage = synced
                ? $"Remote source synced on enable: {source.Name}"
                : $"Remote source enabled: {source.Name}"
        };
    }

    public async Task<RemoteSourceSyncCommandResult> SyncSourceNowAsync(
        HostProfile? source,
        HostProfile? selectedProfile,
        bool isQuickSyncRunning,
        Func<HostProfile, CancellationToken, Task>? beforeSyncAsync = null,
        CancellationToken cancellationToken = default)
    {
        if (source is null || source.SourceType != SourceType.Remote)
        {
            return new RemoteSourceSyncCommandResult();
        }

        if (isQuickSyncRunning)
        {
            return new RemoteSourceSyncCommandResult
            {
                StatusMessage = "A remote sync is already running."
            };
        }

        if (beforeSyncAsync is not null)
        {
            await beforeSyncAsync(source, cancellationToken);
        }

        var synced = await remoteSourceSyncService.SyncProfileAsync(source, cancellationToken);
        if (!remoteSourceSyncService.CanSyncRemoteSource(source))
        {
            return new RemoteSourceSyncCommandResult
            {
                StatusMessage = "Sync skipped. Configure a valid remote source first."
            };
        }

        return new RemoteSourceSyncCommandResult
        {
            ShouldPersistConfiguration = true,
            ShouldNotifySelectedProfileChanged = ReferenceEquals(source, selectedProfile),
            ShouldRunBackgroundManagement = true,
            StatusMessage = synced
                ? $"Synced remote source: {source.Name}"
                : $"Remote source already up to date: {source.Name}"
        };
    }

    private static string? BuildRefreshStatusMessage(bool userInitiated, int syncedCount, int errorCount)
    {
        if (userInitiated)
        {
            return errorCount > 0
                ? $"URL sync completed with {syncedCount} update(s), {errorCount} error(s)."
                : $"Remote sync completed with {syncedCount} update(s).";
        }

        return syncedCount > 0
            ? $"Auto-refresh synced {syncedCount} remote source(s)."
            : null;
    }
}
