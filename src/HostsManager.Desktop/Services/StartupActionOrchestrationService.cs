using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HostsManager.Desktop.Models;

namespace HostsManager.Desktop.Services;

public sealed class ElevationLaunchResult
{
    public bool Relaunched { get; init; }
    public string? StatusMessage { get; init; }
}

public sealed class PendingStartupActionOutcome
{
    public StartupAction Action { get; init; }
    public bool RefreshSystemSourceSnapshot { get; init; }
    public bool MarkManagedApplySucceeded { get; init; }
    public bool MarkLocalSourcesDirty { get; init; }
    public bool DisableSystemHostsEditing { get; init; }
    public bool DismissSelectedSourceExternalChangeNotification { get; init; }
    public bool ClearPendingElevatedHostsUpdate { get; init; }
    public string StatusMessage { get; init; } = string.Empty;
}

public sealed class StartupActionOrchestrationService : IStartupActionOrchestrationService
{
    private readonly ISystemHostsWorkflowService systemHostsWorkflowService;
    private readonly Func<StartupAction> getPendingStartupAction;
    private readonly Func<string?> getStartupActionPayloadPath;
    private readonly Action consumeStartupAction;
    private readonly Func<bool> shouldStartInBackground;
    private readonly Action shutdownApplication;

    public StartupActionOrchestrationService(
        ISystemHostsWorkflowService systemHostsWorkflowService,
        Func<StartupAction> getPendingStartupAction,
        Func<string?> getStartupActionPayloadPath,
        Action consumeStartupAction,
        Func<bool> shouldStartInBackground,
        Action shutdownApplication)
    {
        this.systemHostsWorkflowService = systemHostsWorkflowService ?? throw new ArgumentNullException(nameof(systemHostsWorkflowService));
        this.getPendingStartupAction = getPendingStartupAction ?? throw new ArgumentNullException(nameof(getPendingStartupAction));
        this.getStartupActionPayloadPath = getStartupActionPayloadPath ?? throw new ArgumentNullException(nameof(getStartupActionPayloadPath));
        this.consumeStartupAction = consumeStartupAction ?? throw new ArgumentNullException(nameof(consumeStartupAction));
        this.shouldStartInBackground = shouldStartInBackground ?? throw new ArgumentNullException(nameof(shouldStartInBackground));
        this.shutdownApplication = shutdownApplication ?? throw new ArgumentNullException(nameof(shutdownApplication));
    }

    public bool HasPendingStartupAction()
    {
        return getPendingStartupAction() != StartupAction.None;
    }

    public async Task<ElevationLaunchResult> TryRelaunchElevatedAsync(
        StartupAction action,
        string? rawHostsContent = null,
        CancellationToken cancellationToken = default)
    {
        if (!systemHostsWorkflowService.CanRequestElevation())
        {
            return new ElevationLaunchResult();
        }

        string? payloadPath = null;
        if (action == StartupAction.SaveRawHosts)
        {
            payloadPath = await systemHostsWorkflowService.WritePendingRawHostsPayloadAsync(
                rawHostsContent ?? string.Empty,
                cancellationToken);
        }

        var relaunched = systemHostsWorkflowService.TryRelaunchElevated(
            action,
            shouldStartInBackground(),
            payloadPath);

        if (!relaunched)
        {
            return new ElevationLaunchResult
            {
                StatusMessage = "Administrator approval was canceled or failed."
            };
        }

        shutdownApplication();
        return new ElevationLaunchResult
        {
            Relaunched = true
        };
    }

    public async Task<PendingStartupActionOutcome?> ExecutePendingStartupActionAsync(
        IEnumerable<HostProfile> profiles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        var action = getPendingStartupAction();
        if (action == StartupAction.None)
        {
            return null;
        }

        try
        {
            var result = await systemHostsWorkflowService.ExecuteStartupActionAsync(
                action,
                profiles,
                getStartupActionPayloadPath(),
                cancellationToken);

            return action switch
            {
                StartupAction.ApplyManagedHosts => new PendingStartupActionOutcome
                {
                    Action = action,
                    RefreshSystemSourceSnapshot = result.Performed,
                    MarkManagedApplySucceeded = result.Performed,
                    ClearPendingElevatedHostsUpdate = result.Performed,
                    StatusMessage = result.StatusMessage
                },
                StartupAction.RestoreBackup => new PendingStartupActionOutcome
                {
                    Action = action,
                    RefreshSystemSourceSnapshot = result.Performed,
                    ClearPendingElevatedHostsUpdate = result.Performed,
                    StatusMessage = result.StatusMessage
                },
                StartupAction.SaveRawHosts => new PendingStartupActionOutcome
                {
                    Action = action,
                    RefreshSystemSourceSnapshot = result.Performed,
                    MarkLocalSourcesDirty = result.Performed,
                    DisableSystemHostsEditing = result.Performed,
                    DismissSelectedSourceExternalChangeNotification = result.Performed,
                    ClearPendingElevatedHostsUpdate = result.Performed,
                    StatusMessage = result.StatusMessage
                },
                _ => new PendingStartupActionOutcome
                {
                    Action = action,
                    StatusMessage = result.StatusMessage
                }
            };
        }
        catch (Exception ex)
        {
            return new PendingStartupActionOutcome
            {
                Action = action,
                StatusMessage = $"Startup action failed: {ex.Message}"
            };
        }
        finally
        {
            consumeStartupAction();
        }
    }
}
