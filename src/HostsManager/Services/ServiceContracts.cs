using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HostsManager.Models;

namespace HostsManager.Services;

public interface IProfileStore
{
    string ConfigDirectory { get; }
    string ProfilesFilePath { get; }
    Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default);
}

public interface IHostsFileService
{
    string GetHostsFilePath();
    string GetBackupFilePath();
    Task ApplySourcesAsync(IEnumerable<HostProfile> sources, bool allowPrivilegePrompt = false, CancellationToken cancellationToken = default);
    Task RestoreBackupAsync(bool allowPrivilegePrompt = false, CancellationToken cancellationToken = default);
    Task SaveRawHostsFileAsync(string content, bool allowPrivilegePrompt = false, CancellationToken cancellationToken = default);
    bool ManagedHostsMatch(IEnumerable<HostProfile> sources, string? currentHostsContent);
}

public interface IStartupRegistrationService
{
    bool IsSupported { get; }
    Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default);
    Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
}

public interface IWindowsElevationService
{
    bool IsSupported { get; }
    bool IsProcessElevated();
    bool TryRelaunchElevated(StartupAction action, string? payloadPath = null, bool startInBackground = false);
}

public interface IAzurePrivateDnsService
{
    Task<IReadOnlyList<AzureSubscriptionOption>> ListSubscriptionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AzurePrivateDnsZoneInfo>> ListZonesAsync(string subscriptionId, CancellationToken cancellationToken = default);
    Task<string> BuildHostsEntriesAsync(string subscriptionId, IEnumerable<AzurePrivateDnsZoneInfo> includedZones, CancellationToken cancellationToken = default);
}

public interface IUiTimer
{
    event EventHandler? Tick;
    TimeSpan Interval { get; set; }
    void Start();
    void Stop();
}