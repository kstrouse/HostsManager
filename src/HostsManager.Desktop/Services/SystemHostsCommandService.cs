using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HostsManager.Desktop.Models;

namespace HostsManager.Desktop.Services;

public sealed class SystemHostsCommandResult
{
    public bool RefreshSystemSourceSnapshot { get; init; }
    public bool MarkManagedApplySucceeded { get; init; }
    public bool MarkLocalSourcesDirty { get; init; }
    public bool DisableSystemHostsEditing { get; init; }
    public bool DismissSelectedSourceExternalChangeNotification { get; init; }
    public bool? PendingElevatedHostsUpdate { get; init; }
    public string? StatusMessage { get; init; }
}

public sealed class SystemHostsCommandService : ISystemHostsCommandService
{
    private readonly ISystemHostsWorkflowService systemHostsWorkflowService;

    public SystemHostsCommandService(ISystemHostsWorkflowService systemHostsWorkflowService)
    {
        this.systemHostsWorkflowService = systemHostsWorkflowService ?? throw new ArgumentNullException(nameof(systemHostsWorkflowService));
    }

    public async Task<SystemHostsCommandResult> ApplyManagedHostsAsync(
        IEnumerable<HostProfile> profiles,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await systemHostsWorkflowService.ApplyManagedHostsAsync(
                profiles,
                allowPrivilegePrompt: true,
                cancellationToken);

            return new SystemHostsCommandResult
            {
                RefreshSystemSourceSnapshot = true,
                MarkManagedApplySucceeded = true,
                PendingElevatedHostsUpdate = false,
                StatusMessage = "Applied enabled sources to system hosts file."
            };
        }
        catch (UnauthorizedAccessException)
        {
            return new SystemHostsCommandResult
            {
                PendingElevatedHostsUpdate = true,
                StatusMessage = systemHostsWorkflowService.GetPermissionDeniedMessage(forBackgroundApply: false)
            };
        }
        catch (Exception ex)
        {
            return new SystemHostsCommandResult
            {
                StatusMessage = $"Apply failed: {ex.Message}"
            };
        }
    }

    public async Task<SystemHostsCommandResult> RestoreBackupAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await systemHostsWorkflowService.RestoreBackupAsync(
                allowPrivilegePrompt: true,
                cancellationToken);

            return new SystemHostsCommandResult
            {
                RefreshSystemSourceSnapshot = true,
                PendingElevatedHostsUpdate = false,
                StatusMessage = "Hosts file restored from backup."
            };
        }
        catch (UnauthorizedAccessException)
        {
            return new SystemHostsCommandResult
            {
                PendingElevatedHostsUpdate = true,
                StatusMessage = systemHostsWorkflowService.GetPermissionDeniedMessage(forBackgroundApply: false)
            };
        }
        catch (Exception ex)
        {
            return new SystemHostsCommandResult
            {
                StatusMessage = $"Restore failed: {ex.Message}"
            };
        }
    }

    public async Task<SystemHostsCommandResult> SaveRawHostsAsync(
        string content,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await systemHostsWorkflowService.SaveRawHostsAsync(
                content,
                allowPrivilegePrompt: true,
                cancellationToken);

            return new SystemHostsCommandResult
            {
                RefreshSystemSourceSnapshot = true,
                MarkLocalSourcesDirty = true,
                DisableSystemHostsEditing = true,
                DismissSelectedSourceExternalChangeNotification = true,
                PendingElevatedHostsUpdate = false,
                StatusMessage = "System hosts file saved."
            };
        }
        catch (UnauthorizedAccessException)
        {
            return new SystemHostsCommandResult
            {
                PendingElevatedHostsUpdate = true,
                StatusMessage = systemHostsWorkflowService.GetPermissionDeniedMessage(forBackgroundApply: false)
            };
        }
        catch (Exception ex)
        {
            return new SystemHostsCommandResult
            {
                StatusMessage = $"Save system hosts failed: {ex.Message}"
            };
        }
    }
}
