using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HostsManager.Desktop.Models;

namespace HostsManager.Desktop.Services;

public sealed class AzureSubscriptionsLoadResult
{
    public IReadOnlyList<AzureSubscriptionOption> Subscriptions { get; init; } = [];
    public SelectedProfileChange? SelectedProfileChange { get; init; }
    public string? StatusMessage { get; init; }
}

public sealed class AzureZonesLoadResult
{
    public IReadOnlyList<AzureZoneSelectionItem> Zones { get; init; } = [];
    public bool ShouldReplaceZones { get; init; }
    public string? StatusMessage { get; init; }
}

public sealed class AzureProfileCommandService : IAzureProfileCommandService
{
    private readonly IRemoteSourceSyncService remoteSourceSyncService;
    private readonly IProfileSelectionService profileSelectionService;

    public AzureProfileCommandService(
        IRemoteSourceSyncService remoteSourceSyncService,
        IProfileSelectionService profileSelectionService)
    {
        this.remoteSourceSyncService = remoteSourceSyncService ?? throw new ArgumentNullException(nameof(remoteSourceSyncService));
        this.profileSelectionService = profileSelectionService ?? throw new ArgumentNullException(nameof(profileSelectionService));
    }

    public async Task<AzureSubscriptionsLoadResult> LoadSubscriptionsAsync(
        HostProfile? selectedProfile,
        bool isSystemHostsEditingEnabled,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var subscriptions = await remoteSourceSyncService.ListAzureSubscriptionsAsync(cancellationToken);
            return new AzureSubscriptionsLoadResult
            {
                Subscriptions = subscriptions,
                SelectedProfileChange = profileSelectionService.EvaluateSelectedProfile(
                    selectedProfile,
                    isSystemHostsEditingEnabled,
                    subscriptions),
                StatusMessage = subscriptions.Count == 0
                    ? "No enabled Azure subscriptions found."
                    : $"Loaded {subscriptions.Count} Azure subscription(s)."
            };
        }
        catch (Exception ex)
        {
            return new AzureSubscriptionsLoadResult
            {
                StatusMessage = $"Azure connect failed: {ex.Message}"
            };
        }
    }

    public async Task<AzureZonesLoadResult> RefreshZonesAsync(
        HostProfile? selectedProfile,
        CancellationToken cancellationToken = default)
    {
        if (selectedProfile is null ||
            selectedProfile.SourceType != SourceType.Remote ||
            selectedProfile.RemoteTransport != RemoteTransport.AzurePrivateDns)
        {
            return new AzureZonesLoadResult
            {
                StatusMessage = "Select an Azure Private DNS remote source first."
            };
        }

        if (string.IsNullOrWhiteSpace(selectedProfile.AzureSubscriptionId))
        {
            return new AzureZonesLoadResult
            {
                StatusMessage = "Select an Azure subscription first."
            };
        }

        try
        {
            var zones = await remoteSourceSyncService.GetAzureZoneSelectionsAsync(selectedProfile, cancellationToken);
            return new AzureZonesLoadResult
            {
                Zones = zones,
                ShouldReplaceZones = true,
                StatusMessage = zones.Count == 0
                    ? "No Azure Private DNS zones found for this subscription."
                    : $"Loaded {zones.Count} Azure zone(s)."
            };
        }
        catch (Exception ex)
        {
            return new AzureZonesLoadResult
            {
                StatusMessage = $"Loading zones failed: {ex.Message}"
            };
        }
    }

    public async Task<AzureZonesLoadResult> RefreshZonesForSelectionAsync(
        HostProfile? selectedProfile,
        CancellationToken cancellationToken = default)
    {
        if (selectedProfile is null ||
            selectedProfile.SourceType != SourceType.Remote ||
            selectedProfile.RemoteTransport != RemoteTransport.AzurePrivateDns ||
            string.IsNullOrWhiteSpace(selectedProfile.AzureSubscriptionId))
        {
            return new AzureZonesLoadResult
            {
                ShouldReplaceZones = true
            };
        }

        try
        {
            return new AzureZonesLoadResult
            {
                Zones = await remoteSourceSyncService.GetAzureZoneSelectionsAsync(selectedProfile, cancellationToken),
                ShouldReplaceZones = true
            };
        }
        catch
        {
            return new AzureZonesLoadResult
            {
                ShouldReplaceZones = true
            };
        }
    }
}
