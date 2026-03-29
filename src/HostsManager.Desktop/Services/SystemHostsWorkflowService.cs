using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using HostsManager.Desktop.Models;

namespace HostsManager.Desktop.Services;

public readonly record struct StartupActionExecutionResult(bool Performed, string StatusMessage);

public sealed class SystemHostsWorkflowService : ISystemHostsWorkflowService
{
    private readonly IHostsFileService hostsFileService;
    private readonly ILocalSourceService localSourceService;
    private readonly IProfileStore profileStore;
    private readonly IWindowsElevationService windowsElevationService;

    public SystemHostsWorkflowService(
        IHostsFileService hostsFileService,
        ILocalSourceService localSourceService,
        IProfileStore profileStore,
        IWindowsElevationService windowsElevationService)
    {
        this.hostsFileService = hostsFileService ?? throw new ArgumentNullException(nameof(hostsFileService));
        this.localSourceService = localSourceService ?? throw new ArgumentNullException(nameof(localSourceService));
        this.profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        this.windowsElevationService = windowsElevationService ?? throw new ArgumentNullException(nameof(windowsElevationService));
    }

    public string GetHostsFilePath()
    {
        return hostsFileService.GetHostsFilePath();
    }

    public async Task<HostProfile> BuildSystemSourceAsync(CancellationToken cancellationToken = default)
    {
        var hostsPath = hostsFileService.GetHostsFilePath();

        string text;
        try
        {
            text = await File.ReadAllTextAsync(hostsPath, cancellationToken);
        }
        catch (UnauthorizedAccessException ex)
        {
            text = "# Unable to read system hosts file (permission denied)." + Environment.NewLine +
                   "# " + ex.Message;
        }
        catch (Exception ex)
        {
            text = "# Unable to read system hosts file." + Environment.NewLine +
                   "# " + ex.Message;
        }

        return new HostProfile
        {
            Id = "system-hosts-source",
            Name = "System Hosts",
            IsEnabled = true,
            SourceType = SourceType.System,
            LocalPath = hostsPath,
            Entries = text,
            IsReadOnly = true,
            LastLoadedFromDiskEntries = text
        };
    }

    public Task<bool> HasSystemSourceChangedAsync(HostProfile systemSource, CancellationToken cancellationToken = default)
    {
        return localSourceService.HasDiskContentChangedAsync(systemSource, cancellationToken);
    }

    public Task<bool> ReloadSystemSourceAsync(HostProfile systemSource, CancellationToken cancellationToken = default)
    {
        return localSourceService.ReloadFromDiskAsync(systemSource, cancellationToken);
    }

    public Task ApplyManagedHostsAsync(IEnumerable<HostProfile> sources, bool allowPrivilegePrompt = false, CancellationToken cancellationToken = default)
    {
        return hostsFileService.ApplySourcesAsync(sources, allowPrivilegePrompt, cancellationToken);
    }

    public Task RestoreBackupAsync(bool allowPrivilegePrompt = false, CancellationToken cancellationToken = default)
    {
        return hostsFileService.RestoreBackupAsync(allowPrivilegePrompt, cancellationToken);
    }

    public Task SaveRawHostsAsync(string content, bool allowPrivilegePrompt = false, CancellationToken cancellationToken = default)
    {
        return hostsFileService.SaveRawHostsFileAsync(content, allowPrivilegePrompt, cancellationToken);
    }

    public bool NeedsManagedApply(IEnumerable<HostProfile> sources, HostProfile? systemSource)
    {
        if (systemSource is null)
        {
            return true;
        }

        return !hostsFileService.ManagedHostsMatch(sources, systemSource.Entries);
    }

    public string GetPermissionDeniedMessage(bool forBackgroundApply)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return forBackgroundApply
                ? "Background manager skipped hosts-file updates. Use Apply Hosts Now and approve the macOS admin prompt."
                : "Administrative access is required to modify /etc/hosts on macOS. Approve the macOS admin prompt and try again.";
        }

        return forBackgroundApply
            ? "Pending hosts changes need administrator approval. Click Apply to elevate."
            : "Administrator approval is required to modify the hosts file.";
    }

    public bool CanRequestElevation()
    {
        return windowsElevationService.IsSupported && !windowsElevationService.IsProcessElevated();
    }

    public bool TryRelaunchElevated(StartupAction action, bool startInBackground, string? payloadPath = null)
    {
        return windowsElevationService.TryRelaunchElevated(action, payloadPath, startInBackground);
    }

    public async Task<string> WritePendingRawHostsPayloadAsync(string content, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(profileStore.ConfigDirectory);
        var payloadPath = Path.Combine(profileStore.ConfigDirectory, "pending-system-hosts.txt");
        await File.WriteAllTextAsync(payloadPath, content, cancellationToken);
        return payloadPath;
    }

    public async Task<StartupActionExecutionResult> ExecuteStartupActionAsync(
        StartupAction action,
        IEnumerable<HostProfile> sources,
        string? payloadPath = null,
        CancellationToken cancellationToken = default)
    {
        switch (action)
        {
            case StartupAction.ApplyManagedHosts:
                await ApplyManagedHostsAsync(sources, cancellationToken: cancellationToken);
                return new StartupActionExecutionResult(true, "Applied enabled sources to system hosts file.");

            case StartupAction.RestoreBackup:
                await RestoreBackupAsync(cancellationToken: cancellationToken);
                return new StartupActionExecutionResult(true, "Hosts file restored from backup.");

            case StartupAction.SaveRawHosts:
                if (string.IsNullOrWhiteSpace(payloadPath) || !File.Exists(payloadPath))
                {
                    return new StartupActionExecutionResult(false, "Pending system hosts content was not found.");
                }

                var content = await File.ReadAllTextAsync(payloadPath, cancellationToken);
                await SaveRawHostsAsync(content, cancellationToken: cancellationToken);

                try
                {
                    File.Delete(payloadPath);
                }
                catch
                {
                }

                return new StartupActionExecutionResult(true, "System hosts file saved.");

            default:
                return new StartupActionExecutionResult(false, string.Empty);
        }
    }
}
