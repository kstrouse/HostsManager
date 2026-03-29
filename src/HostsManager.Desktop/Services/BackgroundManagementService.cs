using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HostsManager.Desktop.Models;

namespace HostsManager.Desktop.Services;

public sealed class BackgroundManagementRequest
{
    public bool MinimizeToTrayOnClose { get; init; }
    public bool RunAtStartup { get; init; }
    public IReadOnlyList<HostProfile> Profiles { get; init; } = [];
    public HostProfile? SelectedProfile { get; init; }
}

public sealed class BackgroundManagementResult
{
    public bool SelectedProfileChanged { get; init; }
    public bool? PendingElevatedHostsUpdate { get; init; }
    public string? StatusMessage { get; init; }
    public HostProfile? SourceWithExternalChanges { get; init; }
    public IReadOnlyList<HostProfile> MissingStateChangedSources { get; init; } = [];
}

public sealed class BackgroundManagementService : IBackgroundManagementService
{
    private readonly IProfileStore profileStore;
    private readonly IHostsStateTracker hostsStateTracker;
    private readonly ILocalSourceWatcherService localSourceWatcherService;
    private readonly ILocalSourceRefreshService localSourceRefreshService;
    private readonly ISystemHostsWorkflowService systemHostsWorkflowService;

    public BackgroundManagementService(
        IProfileStore profileStore,
        IHostsStateTracker hostsStateTracker,
        ILocalSourceWatcherService localSourceWatcherService,
        ILocalSourceRefreshService localSourceRefreshService,
        ISystemHostsWorkflowService systemHostsWorkflowService)
    {
        this.profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        this.hostsStateTracker = hostsStateTracker ?? throw new ArgumentNullException(nameof(hostsStateTracker));
        this.localSourceWatcherService = localSourceWatcherService ?? throw new ArgumentNullException(nameof(localSourceWatcherService));
        this.localSourceRefreshService = localSourceRefreshService ?? throw new ArgumentNullException(nameof(localSourceRefreshService));
        this.systemHostsWorkflowService = systemHostsWorkflowService ?? throw new ArgumentNullException(nameof(systemHostsWorkflowService));
    }

    public async Task<bool> PersistConfigurationIfChangedAsync(
        bool minimizeToTrayOnClose,
        bool runAtStartup,
        IEnumerable<HostProfile> profiles,
        CancellationToken cancellationToken = default)
    {
        if (!hostsStateTracker.NeedsConfigurationSave(minimizeToTrayOnClose, runAtStartup, profiles))
        {
            return false;
        }

        try
        {
            await profileStore.SaveAsync(new AppConfig
            {
                MinimizeToTrayOnClose = minimizeToTrayOnClose,
                RunAtStartup = runAtStartup,
                Profiles = profiles.Where(source => !source.IsReadOnly).ToList()
            }, cancellationToken);

            hostsStateTracker.MarkConfigurationSaved(minimizeToTrayOnClose, runAtStartup, profiles);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<BackgroundManagementResult> RunPassAsync(
        BackgroundManagementRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        localSourceWatcherService.SyncWatchedSources(request.Profiles);
        await PersistConfigurationIfChangedAsync(
            request.MinimizeToTrayOnClose,
            request.RunAtStartup,
            request.Profiles,
            cancellationToken);

        var systemSource = GetSystemSource(request.Profiles);
        var initialSystemRefresh = await localSourceRefreshService.RefreshSystemSourceAsync(
            systemSource,
            request.SelectedProfile,
            announceWhenChanged: true,
            skipSelectedProfile: true,
            cancellationToken);

        var selectedProfileChanged = initialSystemRefresh.SelectedProfileChanged;
        var externalChangesSource = initialSystemRefresh.ExternalChangeDetected ? systemSource : null;
        var localContentChanged = initialSystemRefresh.Changed;
        var missingStateChangedSources = new List<HostProfile>();

        if (localSourceWatcherService.ConsumeDirty())
        {
            var localRefresh = await localSourceRefreshService.RefreshLocalSourcesAsync(
                request.Profiles,
                request.SelectedProfile,
                cancellationToken);

            localContentChanged |= localRefresh.AnyContentChanged;
            selectedProfileChanged |= localRefresh.SelectedProfileChanged;
            externalChangesSource ??= localRefresh.SelectedSourceWithExternalChanges;
            missingStateChangedSources.AddRange(localRefresh.MissingStateChangedSources);
        }

        if (localContentChanged)
        {
            await PersistConfigurationIfChangedAsync(
                request.MinimizeToTrayOnClose,
                request.RunAtStartup,
                request.Profiles,
                cancellationToken);
        }

        var needsElevatedApply = systemHostsWorkflowService.NeedsManagedApply(request.Profiles, systemSource);
        var applyEvaluation = hostsStateTracker.EvaluateManagedApply(request.Profiles, managedHostsMatch: !needsElevatedApply);
        if (!needsElevatedApply)
        {
            return new BackgroundManagementResult
            {
                SelectedProfileChanged = selectedProfileChanged,
                PendingElevatedHostsUpdate = false,
                SourceWithExternalChanges = externalChangesSource,
                MissingStateChangedSources = missingStateChangedSources
            };
        }

        if (!applyEvaluation.ShouldApply)
        {
            return new BackgroundManagementResult
            {
                SelectedProfileChanged = selectedProfileChanged,
                SourceWithExternalChanges = externalChangesSource,
                MissingStateChangedSources = missingStateChangedSources
            };
        }

        try
        {
            await systemHostsWorkflowService.ApplyManagedHostsAsync(request.Profiles, cancellationToken: cancellationToken);
            var postApplySystemRefresh = await localSourceRefreshService.RefreshSystemSourceAsync(
                systemSource,
                request.SelectedProfile,
                announceWhenChanged: true,
                skipSelectedProfile: false,
                cancellationToken);

            hostsStateTracker.MarkManagedApplySucceeded(request.Profiles);

            selectedProfileChanged |= postApplySystemRefresh.SelectedProfileChanged;
            var statusMessage = postApplySystemRefresh.RefreshPulseRequested
                ? "System Hosts refreshed from disk."
                : applyEvaluation.HadAppliedChangeBefore || localContentChanged || postApplySystemRefresh.Changed
                    ? "Background manager applied source changes to hosts file."
                    : null;

            return new BackgroundManagementResult
            {
                SelectedProfileChanged = selectedProfileChanged,
                PendingElevatedHostsUpdate = false,
                StatusMessage = statusMessage,
                SourceWithExternalChanges = externalChangesSource,
                MissingStateChangedSources = missingStateChangedSources
            };
        }
        catch (UnauthorizedAccessException)
        {
            hostsStateTracker.MarkManagedApplyAttempted(request.Profiles);
            var stillNeedsApply = systemHostsWorkflowService.NeedsManagedApply(request.Profiles, systemSource);

            return new BackgroundManagementResult
            {
                SelectedProfileChanged = selectedProfileChanged,
                PendingElevatedHostsUpdate = stillNeedsApply,
                StatusMessage = stillNeedsApply
                    ? systemHostsWorkflowService.GetPermissionDeniedMessage(forBackgroundApply: true)
                    : null,
                SourceWithExternalChanges = externalChangesSource,
                MissingStateChangedSources = missingStateChangedSources
            };
        }
        catch (Exception ex)
        {
            hostsStateTracker.MarkManagedApplyAttempted(request.Profiles);
            return new BackgroundManagementResult
            {
                SelectedProfileChanged = selectedProfileChanged,
                SourceWithExternalChanges = externalChangesSource,
                MissingStateChangedSources = missingStateChangedSources,
                StatusMessage = $"Background apply failed: {ex.Message}"
            };
        }
    }

    private static HostProfile? GetSystemSource(IEnumerable<HostProfile> profiles)
    {
        return profiles.FirstOrDefault(source => source.SourceType == SourceType.System && source.IsReadOnly);
    }
}
