namespace HostsManager.Desktop.Tests;

public sealed class BackgroundManagementCoordinatorTests
{
    [Fact]
    public void Start_StartsManageTimerOnlyOnce()
    {
        var timer = new ManualTimer();
        var coordinator = CreateCoordinator(timer: timer);

        coordinator.Start();
        coordinator.Start();

        Assert.Equal(1, timer.StartCallCount);
    }

    [Fact]
    public async Task RequestImmediateReconcileAsync_CoalescesPendingUiSchedules()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scheduledActions = new List<Func<Task>>();
        var service = new StubBackgroundManagementService();
        var appliedCount = 0;
        var coordinator = CreateCoordinator(
            service,
            scheduleOnUiThread: scheduledActions.Add,
            resultHandler: _ => appliedCount++);

        await coordinator.RequestImmediateReconcileAsync(cancellationToken);
        await coordinator.RequestImmediateReconcileAsync(cancellationToken);

        Assert.Single(scheduledActions);

        await scheduledActions[0]();

        Assert.Equal(1, service.RunPassCalls);
        Assert.Equal(1, appliedCount);
    }

    [Fact]
    public async Task RequestImmediateReconcileAsync_WhileRunning_ReconcilesWithinCurrentLoop()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scheduledActions = new List<Func<Task>>();
        var firstPassStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueFirstPass = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new StubBackgroundManagementService
        {
            RunPassAsyncHandler = async (_, _) =>
            {
                if (firstPassStarted.TrySetResult(true))
                {
                    await continueFirstPass.Task;
                }

                return new BackgroundManagementResult();
            }
        };
        var coordinator = CreateCoordinator(service, scheduleOnUiThread: scheduledActions.Add);

        var runTask = coordinator.RunNowAsync(cancellationToken);
        await firstPassStarted.Task;

        await coordinator.RequestImmediateReconcileAsync(cancellationToken);
        continueFirstPass.SetResult(true);

        await runTask;

        Assert.Equal(2, service.RunPassCalls);
        Assert.Empty(scheduledActions);
    }

    private static BackgroundManagementCoordinator CreateCoordinator(
        StubBackgroundManagementService? service = null,
        ManualTimer? timer = null,
        Action<Func<Task>>? scheduleOnUiThread = null,
        Func<BackgroundManagementRequest>? requestFactory = null,
        Action<BackgroundManagementResult>? resultHandler = null)
    {
        return new BackgroundManagementCoordinator(
            service ?? new StubBackgroundManagementService(),
            timer ?? new ManualTimer(),
            scheduleOnUiThread ?? (action => throw new InvalidOperationException($"Unexpected scheduled action: {action}")),
            requestFactory ?? (() => new BackgroundManagementRequest()),
            resultHandler ?? (_ => { }));
    }

    private sealed class StubBackgroundManagementService : IBackgroundManagementService
    {
        public int RunPassCalls { get; private set; }

        public Func<BackgroundManagementRequest, CancellationToken, Task<BackgroundManagementResult>>? RunPassAsyncHandler { get; init; }

        public Task<bool> PersistConfigurationIfChangedAsync(bool minimizeToTrayOnClose, bool runAtStartup, IEnumerable<HostProfile> profiles, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<BackgroundManagementResult> RunPassAsync(BackgroundManagementRequest request, CancellationToken cancellationToken = default)
        {
            RunPassCalls++;
            return RunPassAsyncHandler is null
                ? Task.FromResult(new BackgroundManagementResult())
                : RunPassAsyncHandler(request, cancellationToken);
        }
    }
}
