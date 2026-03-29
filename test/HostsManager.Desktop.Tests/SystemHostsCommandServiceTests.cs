namespace HostsManager.Desktop.Tests;

public sealed class SystemHostsCommandServiceTests
{
    [Fact]
    public async Task ApplyManagedHostsAsync_ReturnsSuccessEffects()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var workflow = new StubSystemHostsWorkflowService();
        var service = new SystemHostsCommandService(workflow);
        var profiles = CreateProfiles();

        var result = await service.ApplyManagedHostsAsync(profiles, cancellationToken);

        Assert.True(workflow.ApplyManagedHostsCalled);
        Assert.True(result.RefreshSystemSourceSnapshot);
        Assert.True(result.MarkManagedApplySucceeded);
        Assert.False(result.MarkLocalSourcesDirty);
        Assert.False(result.PendingElevatedHostsUpdate);
        Assert.Equal("Applied enabled sources to system hosts file.", result.StatusMessage);
    }

    [Fact]
    public async Task RestoreBackupAsync_ReturnsPendingStatusWhenUnauthorized()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var workflow = new StubSystemHostsWorkflowService
        {
            RestoreBackupException = new UnauthorizedAccessException("denied")
        };
        var service = new SystemHostsCommandService(workflow);

        var result = await service.RestoreBackupAsync(cancellationToken);

        Assert.True(workflow.RestoreBackupCalled);
        Assert.False(result.RefreshSystemSourceSnapshot);
        Assert.True(result.PendingElevatedHostsUpdate);
        Assert.Equal(workflow.PermissionDeniedMessage, result.StatusMessage);
    }

    [Fact]
    public async Task SaveRawHostsAsync_ReturnsSuccessUiEffects()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var workflow = new StubSystemHostsWorkflowService();
        var service = new SystemHostsCommandService(workflow);

        var result = await service.SaveRawHostsAsync("127.0.0.1 localhost", cancellationToken);

        Assert.True(workflow.SaveRawHostsCalled);
        Assert.Equal("127.0.0.1 localhost", workflow.LastSavedRawHostsContent);
        Assert.True(result.RefreshSystemSourceSnapshot);
        Assert.True(result.MarkLocalSourcesDirty);
        Assert.True(result.DisableSystemHostsEditing);
        Assert.True(result.DismissSelectedSourceExternalChangeNotification);
        Assert.False(result.PendingElevatedHostsUpdate);
        Assert.Equal("System hosts file saved.", result.StatusMessage);
    }

    [Fact]
    public async Task SaveRawHostsAsync_ReturnsFailureMessageOnUnexpectedException()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var workflow = new StubSystemHostsWorkflowService
        {
            SaveRawHostsException = new InvalidOperationException("boom")
        };
        var service = new SystemHostsCommandService(workflow);

        var result = await service.SaveRawHostsAsync("127.0.0.1 localhost", cancellationToken);

        Assert.Null(result.PendingElevatedHostsUpdate);
        Assert.Equal("Save system hosts failed: boom", result.StatusMessage);
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
                IsReadOnly = true
            }
        ];
    }

    private sealed class StubSystemHostsWorkflowService : ISystemHostsWorkflowService
    {
        public string PermissionDeniedMessage { get; init; } = "Administrator approval is required to modify the hosts file.";
        public bool ApplyManagedHostsCalled { get; private set; }
        public bool RestoreBackupCalled { get; private set; }
        public bool SaveRawHostsCalled { get; private set; }
        public string? LastSavedRawHostsContent { get; private set; }
        public Exception? RestoreBackupException { get; init; }
        public Exception? SaveRawHostsException { get; init; }

        public string GetHostsFilePath() => throw new NotSupportedException();
        public Task<HostProfile> BuildSystemSourceAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasSystemSourceChangedAsync(HostProfile systemSource, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ReloadSystemSourceAsync(HostProfile systemSource, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task ApplyManagedHostsAsync(IEnumerable<HostProfile> sources, bool allowPrivilegePrompt = false, CancellationToken cancellationToken = default)
        {
            ApplyManagedHostsCalled = true;
            return Task.CompletedTask;
        }

        public Task RestoreBackupAsync(bool allowPrivilegePrompt = false, CancellationToken cancellationToken = default)
        {
            RestoreBackupCalled = true;
            return RestoreBackupException is null
                ? Task.CompletedTask
                : Task.FromException(RestoreBackupException);
        }

        public Task SaveRawHostsAsync(string content, bool allowPrivilegePrompt = false, CancellationToken cancellationToken = default)
        {
            SaveRawHostsCalled = true;
            LastSavedRawHostsContent = content;
            return SaveRawHostsException is null
                ? Task.CompletedTask
                : Task.FromException(SaveRawHostsException);
        }

        public bool NeedsManagedApply(IEnumerable<HostProfile> sources, HostProfile? systemSource) => throw new NotSupportedException();
        public string GetPermissionDeniedMessage(bool forBackgroundApply) => PermissionDeniedMessage;
        public bool CanRequestElevation() => throw new NotSupportedException();
        public bool TryRelaunchElevated(StartupAction action, bool startInBackground, string? payloadPath = null) => throw new NotSupportedException();
        public Task<string> WritePendingRawHostsPayloadAsync(string content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StartupActionExecutionResult> ExecuteStartupActionAsync(StartupAction action, IEnumerable<HostProfile> sources, string? payloadPath = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
