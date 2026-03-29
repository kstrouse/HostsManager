using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HostsManager.Desktop.Models;

namespace HostsManager.Desktop.Services;

public readonly record struct ManagedApplyEvaluation(bool ShouldApply, bool HadAppliedChangeBefore);

public sealed class HostsStateTracker : IHostsStateTracker
{
    private string lastAppliedSignature = string.Empty;
    private string lastAttemptedSignature = string.Empty;
    private string lastSavedSignature = string.Empty;

    public void MarkConfigurationSaved(bool minimizeToTrayOnClose, bool runAtStartup, IEnumerable<HostProfile> sources)
    {
        lastSavedSignature = BuildPersistenceSignature(minimizeToTrayOnClose, runAtStartup, sources);
    }

    public bool NeedsConfigurationSave(bool minimizeToTrayOnClose, bool runAtStartup, IEnumerable<HostProfile> sources)
    {
        var currentSignature = BuildPersistenceSignature(minimizeToTrayOnClose, runAtStartup, sources);
        return !string.Equals(currentSignature, lastSavedSignature, StringComparison.Ordinal);
    }

    public void InitializeManagedState(IEnumerable<HostProfile> sources, bool managedHostsMatch)
    {
        if (!managedHostsMatch)
        {
            return;
        }

        var signature = BuildManagedSignature(sources);
        lastAppliedSignature = signature;
        lastAttemptedSignature = signature;
    }

    public ManagedApplyEvaluation EvaluateManagedApply(IEnumerable<HostProfile> sources, bool managedHostsMatch)
    {
        var signature = BuildManagedSignature(sources);
        if (managedHostsMatch)
        {
            lastAppliedSignature = signature;
            lastAttemptedSignature = signature;
            return new ManagedApplyEvaluation(ShouldApply: false, HadAppliedChangeBefore: false);
        }

        if (string.Equals(signature, lastAttemptedSignature, StringComparison.Ordinal))
        {
            return new ManagedApplyEvaluation(ShouldApply: false, HadAppliedChangeBefore: false);
        }

        var hadAppliedChangeBefore = !string.IsNullOrEmpty(lastAppliedSignature) &&
            !string.Equals(signature, lastAppliedSignature, StringComparison.Ordinal);

        return new ManagedApplyEvaluation(ShouldApply: true, HadAppliedChangeBefore: hadAppliedChangeBefore);
    }

    public void MarkManagedApplySucceeded(IEnumerable<HostProfile> sources)
    {
        var signature = BuildManagedSignature(sources);
        lastAppliedSignature = signature;
        lastAttemptedSignature = signature;
    }

    public void MarkManagedApplyAttempted(IEnumerable<HostProfile> sources)
    {
        lastAttemptedSignature = BuildManagedSignature(sources);
    }

    private static IEnumerable<HostProfile> GetPersistedSources(IEnumerable<HostProfile> sources)
    {
        return sources.Where(source => !source.IsReadOnly);
    }

    private static string BuildManagedSignature(IEnumerable<HostProfile> sources)
    {
        var builder = new StringBuilder();

        foreach (var source in GetPersistedSources(sources))
        {
            builder.Append(source.Id).Append('|')
                .Append(source.IsEnabled).Append('|')
                .Append(source.SourceType).Append('|')
                .Append(source.LocalPath).Append('|')
                .Append(source.RemoteTransport).Append('|')
                .Append(source.RemoteLocation).Append('|')
                .Append(source.Entries).Append('\n');
        }

        return builder.ToString();
    }

    private static string BuildPersistenceSignature(bool minimizeToTrayOnClose, bool runAtStartup, IEnumerable<HostProfile> sources)
    {
        var builder = new StringBuilder();
        builder.Append("MinimizeToTrayOnClose=").Append(minimizeToTrayOnClose).Append('\n');
        builder.Append("RunAtStartup=").Append(runAtStartup).Append('\n');

        foreach (var source in GetPersistedSources(sources))
        {
            builder.Append(source.Id).Append('|')
                .Append(source.Name).Append('|')
                .Append(source.IsEnabled).Append('|')
                .Append(source.SourceType).Append('|')
                .Append(source.LocalPath).Append('|')
                .Append(source.RemoteTransport).Append('|')
                .Append(source.RemoteLocation).Append('|')
                .Append(source.AzureSubscriptionId).Append('|')
                .Append(source.AzureSubscriptionName).Append('|')
                .Append(source.AzureExcludedZones).Append('|')
                .Append(source.AutoRefreshFromRemote).Append('|')
                .Append(source.RefreshIntervalMinutes).Append('|')
                .Append(source.LastSyncedAtUtc?.ToString("O") ?? string.Empty).Append('|')
                .Append(source.Entries).Append('\n');
        }

        return builder.ToString();
    }
}
