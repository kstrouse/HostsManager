using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HostsManager.Desktop.Models;

namespace HostsManager.Desktop.Services;

public sealed class RemoteSourceSyncService : IRemoteSourceSyncService
{
    private readonly HttpClient httpClient;
    private readonly IAzurePrivateDnsService azurePrivateDnsService;

    public RemoteSourceSyncService(HttpClient httpClient, IAzurePrivateDnsService azurePrivateDnsService)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.azurePrivateDnsService = azurePrivateDnsService ?? throw new ArgumentNullException(nameof(azurePrivateDnsService));
    }

    public Task<IReadOnlyList<AzureSubscriptionOption>> ListAzureSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        return azurePrivateDnsService.ListSubscriptionsAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AzureZoneSelectionItem>> GetAzureZoneSelectionsAsync(HostProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (string.IsNullOrWhiteSpace(profile.AzureSubscriptionId))
        {
            return [];
        }

        var zones = await azurePrivateDnsService.ListZonesAsync(profile.AzureSubscriptionId, cancellationToken);
        var disabledKeys = ParseExcludedZones(profile.AzureExcludedZones)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return zones.Select(zone =>
        {
            var compositeKey = BuildZoneCompositeKey(zone.ZoneName, zone.ResourceGroup);
            var isDisabled = disabledKeys.Contains(compositeKey) || disabledKeys.Contains(zone.ZoneName);
            return new AzureZoneSelectionItem
            {
                ZoneName = zone.ZoneName,
                ResourceGroup = zone.ResourceGroup,
                IsEnabled = !isDisabled
            };
        }).ToList();
    }

    public async Task<bool> SyncProfileAsync(HostProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.SourceType != SourceType.Remote)
        {
            return false;
        }

        if (profile.RemoteTransport == RemoteTransport.AzurePrivateDns)
        {
            if (string.IsNullOrWhiteSpace(profile.AzureSubscriptionId))
            {
                return false;
            }

            var zoneSelections = await GetAzureZoneSelectionsAsync(profile, cancellationToken);
            var enabledZones = zoneSelections
                .Where(zone => zone.IsEnabled)
                .Select(zone => new AzurePrivateDnsZoneInfo
                {
                    ZoneName = zone.ZoneName,
                    ResourceGroup = zone.ResourceGroup
                })
                .ToList();

            var entries = await azurePrivateDnsService.BuildHostsEntriesAsync(
                profile.AzureSubscriptionId,
                enabledZones,
                profile.AzureStripPrivatelinkSubdomain,
                cancellationToken);

            var changed = !string.Equals(profile.Entries, entries, StringComparison.Ordinal);
            profile.Entries = entries;
            profile.LastSyncedAtUtc = DateTimeOffset.UtcNow;
            return changed;
        }

        if (!TryGetHttpUri(profile, out var uri))
        {
            return false;
        }

        var remote = await httpClient.GetStringAsync(uri, cancellationToken);
        var httpChanged = !string.Equals(profile.Entries, remote, StringComparison.Ordinal);
        profile.Entries = remote;
        profile.LastSyncedAtUtc = DateTimeOffset.UtcNow;
        return httpChanged;
    }

    public bool CanSyncRemoteSource(HostProfile source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.SourceType != SourceType.Remote || !source.IsEnabled)
        {
            return false;
        }

        if (source.RemoteTransport == RemoteTransport.AzurePrivateDns)
        {
            return !string.IsNullOrWhiteSpace(source.AzureSubscriptionId);
        }

        return TryGetHttpUri(source, out _);
    }

    public bool ShouldAutoRefresh(HostProfile profile, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (!profile.AutoRefreshFromRemote)
        {
            return false;
        }

        var interval = ParseIntervalMinutes(profile.RefreshIntervalMinutes);
        var last = profile.LastSyncedAtUtc;
        return !last.HasValue || (now - last.Value) >= TimeSpan.FromMinutes(interval);
    }

    public string BuildExcludedZones(IEnumerable<AzureZoneSelectionItem> zones)
    {
        ArgumentNullException.ThrowIfNull(zones);

        var excluded = zones
            .Where(zone => !zone.IsEnabled)
            .Select(zone => BuildZoneCompositeKey(zone.ZoneName, zone.ResourceGroup))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return string.Join(Environment.NewLine, excluded);
    }

    public AzureSubscriptionOption? ResolveSelectedAzureSubscription(HostProfile profile, IEnumerable<AzureSubscriptionOption> subscriptions)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(subscriptions);

        if (string.IsNullOrWhiteSpace(profile.AzureSubscriptionId))
        {
            return null;
        }

        return subscriptions.FirstOrDefault(subscription =>
                   string.Equals(subscription.Id, profile.AzureSubscriptionId, StringComparison.OrdinalIgnoreCase)) ??
               new AzureSubscriptionOption
               {
                   Id = profile.AzureSubscriptionId,
                   Name = profile.AzureSubscriptionName
               };
    }

    private static bool TryGetHttpUri(HostProfile source, out Uri? uri)
    {
        if (source.RemoteTransport is not (RemoteTransport.Http or RemoteTransport.Https))
        {
            uri = null;
            return false;
        }

        if (!Uri.TryCreate(source.RemoteLocation, UriKind.Absolute, out uri))
        {
            return false;
        }

        return uri.Scheme is "http" or "https";
    }

    private static IReadOnlyCollection<string> ParseExcludedZones(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return [];
        }

        return input
            .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildZoneCompositeKey(string zoneName, string resourceGroup)
    {
        return $"{zoneName}|{resourceGroup}";
    }

    private static int ParseIntervalMinutes(string? input)
    {
        if (!int.TryParse(input, out var value) || value < 1)
        {
            return 15;
        }

        return Math.Min(value, 1440);
    }
}
