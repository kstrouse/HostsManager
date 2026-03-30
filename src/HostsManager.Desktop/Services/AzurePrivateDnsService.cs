using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Models;
using Azure.ResourceManager.PrivateDns;
using Azure.ResourceManager.PrivateDns.Models;
using HostsManager.Desktop.Models;

namespace HostsManager.Desktop.Services;

public class AzurePrivateDnsService : IAzurePrivateDnsService
{
    private readonly ArmClient armClient;

    public AzurePrivateDnsService(HttpClient httpClient)
    {
        _ = httpClient;

        var credential = new DefaultAzureCredential();

        armClient = new ArmClient(credential);
    }

    public async Task<IReadOnlyList<AzureSubscriptionOption>> ListSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        var subscriptions = new List<AzureSubscriptionOption>();

        await foreach (var subscription in armClient.GetSubscriptions().GetAllAsync(cancellationToken))
        {
            var state = subscription.Data.State?.ToString();
            if (!string.Equals(state, "Enabled", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var id = subscription.Data.SubscriptionId;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            subscriptions.Add(new AzureSubscriptionOption
            {
                Id = id,
                Name = subscription.Data.DisplayName ?? string.Empty
            });
        }

        return subscriptions
            .OrderBy(subscription => subscription.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(subscription => subscription.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<AzurePrivateDnsZoneInfo>> ListZonesAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            throw new InvalidOperationException("Subscription is required.");
        }

        var zones = await ListPrivateDnsZonesAsync(subscriptionId, cancellationToken);
        return zones
            .Select(zone => new AzurePrivateDnsZoneInfo
            {
                ZoneName = zone.ZoneName,
                ResourceGroup = zone.ResourceGroup
            })
            .OrderBy(zone => zone.ZoneName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(zone => zone.ResourceGroup, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<string> BuildHostsEntriesAsync(
        string subscriptionId,
        IEnumerable<AzurePrivateDnsZoneInfo> includedZones,
        bool stripPrivatelinkSubdomain = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            throw new InvalidOperationException("Subscription is required.");
        }

        var selectedZones = includedZones
            .Where(zone => !string.IsNullOrWhiteSpace(zone.ZoneName) && !string.IsNullOrWhiteSpace(zone.ResourceGroup))
            .ToList();

        if (selectedZones.Count == 0)
        {
            throw new InvalidOperationException("No Azure zones are enabled for sync.");
        }

        var builder = new StringBuilder();
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        var totalEntries = 0;

        foreach (var zone in selectedZones
                     .OrderBy(zone => zone.ZoneName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(zone => zone.ResourceGroup, StringComparer.OrdinalIgnoreCase))
        {
            var records = await ListARecordSetsAsync(subscriptionId, zone.ResourceGroup, zone.ZoneName, cancellationToken);
            var zoneHeaderWritten = false;

            foreach (var record in records)
            {
                foreach (var ip in record.IpAddresses)
                {
                    if (!zoneHeaderWritten)
                    {
                        builder.AppendLine();
                        builder.AppendLine($"# Zone: {zone.ZoneName}");
                        builder.AppendLine($"# Resource Group: {zone.ResourceGroup}");
                        zoneHeaderWritten = true;
                    }

                    var fqdn = ResolveRecordFqdn(record.RelativeName, zone.ZoneName, stripPrivatelinkSubdomain);
                    builder.AppendLine($"{ip}\t{fqdn}");
                    totalEntries++;
                }
            }
        }

        if (totalEntries == 0)
        {
            throw new InvalidOperationException("No A records with IPv4 addresses were found in Azure Private DNS zones after applying exclusions.");
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private async Task<IReadOnlyList<PrivateDnsZoneRef>> ListPrivateDnsZonesAsync(string subscriptionId, CancellationToken cancellationToken)
    {
        var subscriptionResource = armClient.GetSubscriptionResource(
            new ResourceIdentifier($"/subscriptions/{subscriptionId}"));

        var results = new List<PrivateDnsZoneRef>();

        await foreach (var zone in subscriptionResource.GetPrivateDnsZonesAsync(cancellationToken: cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(zone.Data.Name) || string.IsNullOrWhiteSpace(zone.Id.ResourceGroupName))
            {
                continue;
            }

            results.Add(new PrivateDnsZoneRef(zone.Data.Name, zone.Id.ResourceGroupName));
        }

        return results;
    }

    private async Task<IReadOnlyList<PrivateDnsRecordSet>> ListARecordSetsAsync(string subscriptionId, string resourceGroup, string zoneName, CancellationToken cancellationToken)
    {
        var zoneId = PrivateDnsZoneResource.CreateResourceIdentifier(subscriptionId, resourceGroup, zoneName);
        var zoneResource = armClient.GetPrivateDnsZoneResource(zoneId);
        var collection = zoneResource.GetPrivateDnsARecords();
        var results = new List<PrivateDnsRecordSet>();

        await foreach (var item in collection.GetAllAsync(cancellationToken: cancellationToken))
        {
            var relativeName = item.Data.Name;
            if (string.IsNullOrWhiteSpace(relativeName))
            {
                continue;
            }

            var ips = new List<string>();
            foreach (var record in item.Data.PrivateDnsARecords)
            {
                if (record.IPv4Address is null)
                {
                    continue;
                }

                ips.Add(record.IPv4Address.ToString());
            }

            if (ips.Count == 0)
            {
                continue;
            }

            results.Add(new PrivateDnsRecordSet(relativeName, ips));
        }

        return results;
    }

    internal static string ResolveRecordFqdn(string relativeName, string zoneName, bool stripPrivatelinkSubdomain = false)
    {
        var effectiveZoneName = stripPrivatelinkSubdomain
            ? StripPrivatelinkSubdomain(zoneName)
            : zoneName;

        if (relativeName == "@")
        {
            return effectiveZoneName.ToLowerInvariant();
        }

        if (relativeName.EndsWith($".{zoneName}", StringComparison.OrdinalIgnoreCase))
        {
            var prefix = relativeName[..^(zoneName.Length + 1)];
            return string.IsNullOrWhiteSpace(prefix)
                ? effectiveZoneName.ToLowerInvariant()
                : $"{prefix}.{effectiveZoneName}".ToLowerInvariant();
        }

        if (relativeName.EndsWith($".{effectiveZoneName}", StringComparison.OrdinalIgnoreCase))
        {
            return relativeName.ToLowerInvariant();
        }

        return $"{relativeName}.{effectiveZoneName}".ToLowerInvariant();
    }

    private static string StripPrivatelinkSubdomain(string zoneName)
    {
        const string PrivatelinkPrefix = "privatelink.";

        return zoneName.StartsWith(PrivatelinkPrefix, StringComparison.OrdinalIgnoreCase) &&
               zoneName.Length > PrivatelinkPrefix.Length
            ? zoneName[PrivatelinkPrefix.Length..]
            : zoneName;
    }

    private sealed record PrivateDnsZoneRef(string ZoneName, string ResourceGroup);
    private sealed record PrivateDnsRecordSet(string RelativeName, IReadOnlyList<string> IpAddresses);
}

