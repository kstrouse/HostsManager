using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HostsManager.Desktop.Models;

namespace HostsManager.Desktop.Services;

public readonly record struct SystemSourceRefreshResult(
    bool Changed,
    bool SelectedProfileChanged,
    bool ExternalChangeDetected,
    bool RefreshPulseRequested);

public sealed class LocalSourceRefreshResult
{
    public bool AnyContentChanged { get; init; }
    public bool SelectedProfileChanged { get; init; }
    public HostProfile? SelectedSourceWithExternalChanges { get; init; }
    public IReadOnlyList<HostProfile> MissingStateChangedSources { get; init; } = [];
}

public sealed class LocalSourceRefreshService : ILocalSourceRefreshService
{
    private readonly ILocalSourceService localSourceService;
    private readonly ISystemHostsWorkflowService systemHostsWorkflowService;

    public LocalSourceRefreshService(
        ILocalSourceService localSourceService,
        ISystemHostsWorkflowService systemHostsWorkflowService)
    {
        this.localSourceService = localSourceService ?? throw new ArgumentNullException(nameof(localSourceService));
        this.systemHostsWorkflowService = systemHostsWorkflowService ?? throw new ArgumentNullException(nameof(systemHostsWorkflowService));
    }

    public async Task<SystemSourceRefreshResult> RefreshSystemSourceAsync(
        HostProfile? systemSource,
        HostProfile? selectedProfile,
        bool announceWhenChanged,
        bool skipSelectedProfile,
        CancellationToken cancellationToken = default)
    {
        if (systemSource is null)
        {
            return default;
        }

        if (skipSelectedProfile && ReferenceEquals(selectedProfile, systemSource))
        {
            var externalChangeDetected = await systemHostsWorkflowService.HasSystemSourceChangedAsync(systemSource, cancellationToken);
            return new SystemSourceRefreshResult(
                Changed: false,
                SelectedProfileChanged: false,
                ExternalChangeDetected: externalChangeDetected,
                RefreshPulseRequested: false);
        }

        var changed = await systemHostsWorkflowService.ReloadSystemSourceAsync(systemSource, cancellationToken);
        var selectedProfileChanged = changed && ReferenceEquals(selectedProfile, systemSource);
        return new SystemSourceRefreshResult(
            Changed: changed,
            SelectedProfileChanged: selectedProfileChanged,
            ExternalChangeDetected: false,
            RefreshPulseRequested: selectedProfileChanged && announceWhenChanged);
    }

    public async Task<LocalSourceRefreshResult> RefreshLocalSourcesAsync(
        IEnumerable<HostProfile> sources,
        HostProfile? selectedProfile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var anyContentChanged = false;
        var selectedProfileChanged = false;
        HostProfile? selectedSourceWithExternalChanges = null;
        var missingStateChangedSources = new List<HostProfile>();

        foreach (var source in sources.Where(source =>
                     source.SourceType == SourceType.Local &&
                     !string.IsNullOrWhiteSpace(source.LocalPath)))
        {
            if (localSourceService.UpdateMissingFileState(source))
            {
                anyContentChanged = true;
                missingStateChangedSources.Add(source);
                if (ReferenceEquals(source, selectedProfile))
                {
                    selectedProfileChanged = true;
                }

                continue;
            }

            if (ReferenceEquals(source, selectedProfile))
            {
                if (await localSourceService.HasDiskContentChangedAsync(source, cancellationToken))
                {
                    selectedSourceWithExternalChanges = source;
                }

                continue;
            }

            if (await localSourceService.ReloadFromDiskAsync(source, cancellationToken))
            {
                anyContentChanged = true;
            }
        }

        return new LocalSourceRefreshResult
        {
            AnyContentChanged = anyContentChanged,
            SelectedProfileChanged = selectedProfileChanged,
            SelectedSourceWithExternalChanges = selectedSourceWithExternalChanges,
            MissingStateChangedSources = missingStateChangedSources
        };
    }
}
