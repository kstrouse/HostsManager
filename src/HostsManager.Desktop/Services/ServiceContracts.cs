using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HostsManager.Desktop.Models;

namespace HostsManager.Desktop.Services;

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

public interface ILocalSourceService
{
    Task<HostProfile> CreateNewSourceAsync(string path, CancellationToken cancellationToken = default);
    Task<HostProfile> LoadExistingSourceAsync(string path, CancellationToken cancellationToken = default);
    Task RenameAsync(HostProfile source, string requestedFileName, CancellationToken cancellationToken = default);
    Task RecreateMissingFileAsync(HostProfile source, CancellationToken cancellationToken = default);
    Task<bool> HasDiskContentChangedAsync(HostProfile source, CancellationToken cancellationToken = default);
    Task<bool> ReloadFromDiskAsync(HostProfile source, CancellationToken cancellationToken = default);
    bool UpdateMissingFileState(HostProfile source);
}

public interface IRemoteSourceSyncService
{
    Task<IReadOnlyList<AzureSubscriptionOption>> ListAzureSubscriptionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AzureZoneSelectionItem>> GetAzureZoneSelectionsAsync(HostProfile profile, CancellationToken cancellationToken = default);
    Task<bool> SyncProfileAsync(HostProfile profile, CancellationToken cancellationToken = default);
    bool CanSyncRemoteSource(HostProfile source);
    bool ShouldAutoRefresh(HostProfile profile, DateTimeOffset now);
    string BuildExcludedZones(IEnumerable<AzureZoneSelectionItem> zones);
    AzureSubscriptionOption? ResolveSelectedAzureSubscription(HostProfile profile, IEnumerable<AzureSubscriptionOption> subscriptions);
}

public interface IHostsStateTracker
{
    void MarkConfigurationSaved(bool minimizeToTrayOnClose, bool runAtStartup, IEnumerable<HostProfile> sources);
    bool NeedsConfigurationSave(bool minimizeToTrayOnClose, bool runAtStartup, IEnumerable<HostProfile> sources);
    void InitializeManagedState(IEnumerable<HostProfile> sources, bool managedHostsMatch);
    ManagedApplyEvaluation EvaluateManagedApply(IEnumerable<HostProfile> sources, bool managedHostsMatch);
    void MarkManagedApplySucceeded(IEnumerable<HostProfile> sources);
    void MarkManagedApplyAttempted(IEnumerable<HostProfile> sources);
}

public interface ISystemHostsWorkflowService
{
    string GetHostsFilePath();
    Task<HostProfile> BuildSystemSourceAsync(CancellationToken cancellationToken = default);
    Task<bool> HasSystemSourceChangedAsync(HostProfile systemSource, CancellationToken cancellationToken = default);
    Task<bool> ReloadSystemSourceAsync(HostProfile systemSource, CancellationToken cancellationToken = default);
    Task ApplyManagedHostsAsync(IEnumerable<HostProfile> sources, bool allowPrivilegePrompt = false, CancellationToken cancellationToken = default);
    Task RestoreBackupAsync(bool allowPrivilegePrompt = false, CancellationToken cancellationToken = default);
    Task SaveRawHostsAsync(string content, bool allowPrivilegePrompt = false, CancellationToken cancellationToken = default);
    bool NeedsManagedApply(IEnumerable<HostProfile> sources, HostProfile? systemSource);
    string GetPermissionDeniedMessage(bool forBackgroundApply);
    bool CanRequestElevation();
    bool TryRelaunchElevated(StartupAction action, bool startInBackground, string? payloadPath = null);
    Task<string> WritePendingRawHostsPayloadAsync(string content, CancellationToken cancellationToken = default);
    Task<StartupActionExecutionResult> ExecuteStartupActionAsync(
        StartupAction action,
        IEnumerable<HostProfile> sources,
        string? payloadPath = null,
        CancellationToken cancellationToken = default);
}

public interface ILocalSourceWatcherService : IDisposable
{
    void SyncWatchedSources(IEnumerable<HostProfile> sources);
    void MarkDirty();
    bool ConsumeDirty();
}

public interface ILocalSourceRefreshService
{
    Task<SystemSourceRefreshResult> RefreshSystemSourceAsync(
        HostProfile? systemSource,
        HostProfile? selectedProfile,
        bool announceWhenChanged,
        bool skipSelectedProfile,
        CancellationToken cancellationToken = default);

    Task<LocalSourceRefreshResult> RefreshLocalSourcesAsync(
        IEnumerable<HostProfile> sources,
        HostProfile? selectedProfile,
        CancellationToken cancellationToken = default);
}

public interface IBackgroundManagementService
{
    Task<bool> PersistConfigurationIfChangedAsync(
        bool minimizeToTrayOnClose,
        bool runAtStartup,
        IEnumerable<HostProfile> profiles,
        CancellationToken cancellationToken = default);

    Task<BackgroundManagementResult> RunPassAsync(
        BackgroundManagementRequest request,
        CancellationToken cancellationToken = default);
}

public interface IBackgroundManagementCoordinator
{
    void Start();

    Task RunNowAsync(CancellationToken cancellationToken = default);

    Task RequestImmediateReconcileAsync(CancellationToken cancellationToken = default);
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
