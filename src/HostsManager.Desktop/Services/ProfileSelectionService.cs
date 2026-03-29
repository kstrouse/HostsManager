using System;
using System.Collections.Generic;
using System.Linq;
using HostsManager.Desktop.Models;

namespace HostsManager.Desktop.Services;

public sealed class SelectedProfileChange
{
    public bool DisableSystemHostsEditing { get; init; }
    public bool ShouldUpdateSelectedAzureSubscription { get; init; }
    public AzureSubscriptionOption? SelectedAzureSubscription { get; init; }
    public AzureSubscriptionOption? SubscriptionToInsert { get; init; }
    public bool ClearAzureZones { get; init; }
    public bool RefreshAzureZones { get; init; }
}

public sealed class AzureSubscriptionSelectionChange
{
    public string SubscriptionId { get; init; } = string.Empty;
    public string SubscriptionName { get; init; } = string.Empty;
}

public sealed class ProfileSelectionService : IProfileSelectionService
{
    private readonly IRemoteSourceSyncService remoteSourceSyncService;

    public ProfileSelectionService(IRemoteSourceSyncService remoteSourceSyncService)
    {
        this.remoteSourceSyncService = remoteSourceSyncService ?? throw new ArgumentNullException(nameof(remoteSourceSyncService));
    }

    public SelectedProfileChange EvaluateSelectedProfile(
        HostProfile? selectedProfile,
        bool isSystemHostsEditingEnabled,
        IEnumerable<AzureSubscriptionOption> subscriptions)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);

        var disableSystemHostsEditing =
            selectedProfile?.SourceType != SourceType.System &&
            isSystemHostsEditingEnabled;

        if (selectedProfile is null || selectedProfile.SourceType != SourceType.Remote)
        {
            return new SelectedProfileChange
            {
                DisableSystemHostsEditing = disableSystemHostsEditing,
                ShouldUpdateSelectedAzureSubscription = true,
                ClearAzureZones = true
            };
        }

        if (selectedProfile.RemoteTransport != RemoteTransport.AzurePrivateDns)
        {
            return new SelectedProfileChange
            {
                DisableSystemHostsEditing = disableSystemHostsEditing,
                ClearAzureZones = true
            };
        }

        if (string.IsNullOrWhiteSpace(selectedProfile.AzureSubscriptionId))
        {
            return new SelectedProfileChange
            {
                DisableSystemHostsEditing = disableSystemHostsEditing,
                ShouldUpdateSelectedAzureSubscription = true,
                ClearAzureZones = true
            };
        }

        var resolvedSubscription = remoteSourceSyncService.ResolveSelectedAzureSubscription(selectedProfile, subscriptions);
        var subscriptionToInsert = resolvedSubscription is not null &&
                                   !subscriptions.Any(subscription =>
                                       string.Equals(subscription.Id, resolvedSubscription.Id, StringComparison.OrdinalIgnoreCase))
            ? resolvedSubscription
            : null;

        return new SelectedProfileChange
        {
            DisableSystemHostsEditing = disableSystemHostsEditing,
            ShouldUpdateSelectedAzureSubscription = true,
            SelectedAzureSubscription = resolvedSubscription,
            SubscriptionToInsert = subscriptionToInsert,
            RefreshAzureZones = resolvedSubscription is not null
        };
    }

    public AzureSubscriptionSelectionChange? CreateAzureSubscriptionSelectionChange(
        HostProfile? selectedProfile,
        AzureSubscriptionOption? selectedSubscription)
    {
        if (selectedProfile is null || selectedProfile.SourceType != SourceType.Remote || selectedSubscription is null)
        {
            return null;
        }

        return new AzureSubscriptionSelectionChange
        {
            SubscriptionId = selectedSubscription.Id,
            SubscriptionName = selectedSubscription.Name
        };
    }
}
