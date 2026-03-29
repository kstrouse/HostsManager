namespace HostsManager.Desktop.Tests;

public sealed class StartupActionOrchestrationServiceTests
{
    [Fact]
    public void HasPendingStartupAction_ReturnsTrueOnlyWhenActionIsPresent()
    {
        var action = StartupAction.None;
        var service = CreateService(getPendingStartupAction: () => action);

        Assert.False(service.HasPendingStartupAction());

        action = StartupAction.ApplyManagedHosts;

        Assert.True(service.HasPendingStartupAction());
    }

    [Fact]
    public async Task TryRelaunchElevatedAsync_ReturnsEmptyResultWhenElevationIsUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var workflow = new StubSystemHostsWorkflowService
        {
            CanRequestElevationResult = false
        };
        var shutdownCount = 0;
        var service = CreateService(
            workflow,
            shutdownApplication: () => shutdownCount++);

        var result = await service.TryRelaunchElevatedAsync(StartupAction.ApplyManagedHosts, cancellationToken: cancellationToken);

        Assert.False(result.Relaunched);
        Assert.Null(result.StatusMessage);
        Assert.False(workflow.TryRelaunchElevatedCalled);
        Assert.Equal(0, shutdownCount);
    }

    [Fact]
    public async Task TryRelaunchElevatedAsync_SaveRawHostsWritesPayloadAndShutsDownOnSuccess()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var workflow = new StubSystemHostsWorkflowService
        {
            CanRequestElevationResult = true,
            TryRelaunchElevatedResult = true,
            WritePendingRawHostsPayloadAsyncHandler = (_, _) => Task.FromResult("C:/temp/payload.txt")
        };
        var shutdownCount = 0;
        var service = CreateService(
            workflow,
            shouldStartInBackground: () => true,
            shutdownApplication: () => shutdownCount++);

        var result = await service.TryRelaunchElevatedAsync(
            StartupAction.SaveRawHosts,
            "127.0.0.1 localhost",
            cancellationToken);

        Assert.True(result.Relaunched);
        Assert.Null(result.StatusMessage);
        Assert.Equal(StartupAction.SaveRawHosts, workflow.LastTryRelaunchAction);
        Assert.Equal("C:/temp/payload.txt", workflow.LastTryRelaunchPayloadPath);
        Assert.True(workflow.LastTryRelaunchStartInBackground);
        Assert.Equal("127.0.0.1 localhost", workflow.LastWrittenPayloadContent);
        Assert.Equal(1, shutdownCount);
    }

    [Fact]
    public async Task TryRelaunchElevatedAsync_ReturnsCanceledMessageWhenRelaunchFails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var workflow = new StubSystemHostsWorkflowService
        {
            CanRequestElevationResult = true,
            TryRelaunchElevatedResult = false
        };
        var shutdownCount = 0;
        var service = CreateService(
            workflow,
            shutdownApplication: () => shutdownCount++);

        var result = await service.TryRelaunchElevatedAsync(
            StartupAction.ApplyManagedHosts,
            cancellationToken: cancellationToken);

        Assert.False(result.Relaunched);
        Assert.Equal("Administrator approval was canceled or failed.", result.StatusMessage);
        Assert.Equal(0, shutdownCount);
    }

    [Fact]
    public async Task ExecutePendingStartupActionAsync_ReturnsNullWhenNoPendingActionExists()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var consumeCount = 0;
        var service = CreateService(
            getPendingStartupAction: () => StartupAction.None,
            consumeStartupAction: () => consumeCount++);

        var result = await service.ExecutePendingStartupActionAsync([], cancellationToken);

        Assert.Null(result);
        Assert.Equal(0, consumeCount);
    }

    [Fact]
    public async Task ExecutePendingStartupActionAsync_MapsApplyManagedHostsEffectsAndConsumesAction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var consumeCount = 0;
        var workflow = new StubSystemHostsWorkflowService
        {
            ExecuteStartupActionAsyncHandler = (_, _, _, _) => Task.FromResult(
                new StartupActionExecutionResult(true, "Applied enabled sources to system hosts file."))
        };
        var service = CreateService(
            workflow,
            getPendingStartupAction: () => StartupAction.ApplyManagedHosts,
            consumeStartupAction: () => consumeCount++);

        var result = await service.ExecutePendingStartupActionAsync(CreateProfiles(), cancellationToken);

        Assert.NotNull(result);
        Assert.Equal(StartupAction.ApplyManagedHosts, result.Action);
        Assert.True(result.RefreshSystemSourceSnapshot);
        Assert.True(result.MarkManagedApplySucceeded);
        Assert.True(result.ClearPendingElevatedHostsUpdate);
        Assert.Equal("Applied enabled sources to system hosts file.", result.StatusMessage);
        Assert.Equal(1, consumeCount);
    }

    [Fact]
    public async Task ExecutePendingStartupActionAsync_MapsSaveRawHostsEffectsAndPayload()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var workflow = new StubSystemHostsWorkflowService
        {
            ExecuteStartupActionAsyncHandler = (_, _, payloadPath, _) => Task.FromResult(
                new StartupActionExecutionResult(payloadPath == "C:/temp/payload.txt", "System hosts file saved."))
        };
        var service = CreateService(
            workflow,
            getPendingStartupAction: () => StartupAction.SaveRawHosts,
            getStartupActionPayloadPath: () => "C:/temp/payload.txt");

        var result = await service.ExecutePendingStartupActionAsync(CreateProfiles(), cancellationToken);

        Assert.NotNull(result);
        Assert.True(result.RefreshSystemSourceSnapshot);
        Assert.True(result.MarkLocalSourcesDirty);
        Assert.True(result.DisableSystemHostsEditing);
        Assert.True(result.DismissSelectedSourceExternalChangeNotification);
        Assert.True(result.ClearPendingElevatedHostsUpdate);
        Assert.Equal("C:/temp/payload.txt", workflow.LastExecutePayloadPath);
    }

    [Fact]
    public async Task ExecutePendingStartupActionAsync_ReturnsFailureMessageAndConsumesActionOnException()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var consumeCount = 0;
        var workflow = new StubSystemHostsWorkflowService
        {
            ExecuteStartupActionAsyncHandler = (_, _, _, _) => Task.FromException<StartupActionExecutionResult>(
                new InvalidOperationException("boom"))
        };
        var service = CreateService(
            workflow,
            getPendingStartupAction: () => StartupAction.RestoreBackup,
            consumeStartupAction: () => consumeCount++);

        var result = await service.ExecutePendingStartupActionAsync(CreateProfiles(), cancellationToken);

        Assert.NotNull(result);
        Assert.Equal(StartupAction.RestoreBackup, result.Action);
        Assert.Equal("Startup action failed: boom", result.StatusMessage);
        Assert.False(result.RefreshSystemSourceSnapshot);
        Assert.Equal(1, consumeCount);
    }

    private static StartupActionOrchestrationService CreateService(
        StubSystemHostsWorkflowService? workflow = null,
        Func<StartupAction>? getPendingStartupAction = null,
        Func<string?>? getStartupActionPayloadPath = null,
        Action? consumeStartupAction = null,
        Func<bool>? shouldStartInBackground = null,
        Action? shutdownApplication = null)
    {
        return new StartupActionOrchestrationService(
            workflow ?? new StubSystemHostsWorkflowService(),
            getPendingStartupAction ?? (() => StartupAction.None),
            getStartupActionPayloadPath ?? (() => null),
            consumeStartupAction ?? (() => { }),
            shouldStartInBackground ?? (() => false),
            shutdownApplication ?? (() => { }));
    }

    private static IReadOnlyList<HostProfile> CreateProfiles()
    {
        return
        [
            new HostProfile
            {
                Id = "system-hosts-source",
                Name = "System Hosts",
                SourceType = SourceType.System,
                IsReadOnly = true,
                LocalPath = "C:/temp/hosts",
                Entries = "127.0.0.1 localhost"
            }
        ];
    }

    private sealed class StubSystemHostsWorkflowService : ISystemHostsWorkflowService
    {
        public bool CanRequestElevationResult { get; init; }
        public bool TryRelaunchElevatedResult { get; init; }
        public bool TryRelaunchElevatedCalled { get; private set; }
        public StartupAction? LastTryRelaunchAction { get; private set; }
        public bool LastTryRelaunchStartInBackground { get; private set; }
        public string? LastTryRelaunchPayloadPath { get; private set; }
        public string? LastWrittenPayloadContent { get; private set; }
        public string? LastExecutePayloadPath { get; private set; }

        public Func<string, CancellationToken, Task<string>>? WritePendingRawHostsPayloadAsyncHandler { get; init; }

        public Func<StartupAction, IEnumerable<HostProfile>, string?, CancellationToken, Task<StartupActionExecutionResult>>? ExecuteStartupActionAsyncHandler { get; init; }

        public string GetHostsFilePath() => throw new NotSupportedException();
        public Task<HostProfile> BuildSystemSourceAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasSystemSourceChangedAsync(HostProfile systemSource, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ReloadSystemSourceAsync(HostProfile systemSource, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ApplyManagedHostsAsync(IEnumerable<HostProfile> sources, bool allowPrivilegePrompt = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RestoreBackupAsync(bool allowPrivilegePrompt = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveRawHostsAsync(string content, bool allowPrivilegePrompt = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public bool NeedsManagedApply(IEnumerable<HostProfile> sources, HostProfile? systemSource) => throw new NotSupportedException();
        public string GetPermissionDeniedMessage(bool forBackgroundApply) => throw new NotSupportedException();

        public bool CanRequestElevation() => CanRequestElevationResult;

        public bool TryRelaunchElevated(StartupAction action, bool startInBackground, string? payloadPath = null)
        {
            TryRelaunchElevatedCalled = true;
            LastTryRelaunchAction = action;
            LastTryRelaunchStartInBackground = startInBackground;
            LastTryRelaunchPayloadPath = payloadPath;
            return TryRelaunchElevatedResult;
        }

        public Task<string> WritePendingRawHostsPayloadAsync(string content, CancellationToken cancellationToken = default)
        {
            LastWrittenPayloadContent = content;
            return WritePendingRawHostsPayloadAsyncHandler is null
                ? Task.FromResult("C:/temp/pending-system-hosts.txt")
                : WritePendingRawHostsPayloadAsyncHandler(content, cancellationToken);
        }

        public Task<StartupActionExecutionResult> ExecuteStartupActionAsync(StartupAction action, IEnumerable<HostProfile> sources, string? payloadPath = null, CancellationToken cancellationToken = default)
        {
            LastExecutePayloadPath = payloadPath;
            return ExecuteStartupActionAsyncHandler is null
                ? Task.FromResult(new StartupActionExecutionResult(false, string.Empty))
                : ExecuteStartupActionAsyncHandler(action, sources, payloadPath, cancellationToken);
        }
    }
}
