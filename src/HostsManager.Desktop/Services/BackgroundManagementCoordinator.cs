using System;
using System.Threading;
using System.Threading.Tasks;

namespace HostsManager.Desktop.Services;

public sealed class BackgroundManagementCoordinator : IBackgroundManagementCoordinator
{
    private readonly IBackgroundManagementService backgroundManagementService;
    private readonly IUiTimer manageTimer;
    private readonly Action<Func<Task>> scheduleOnUiThread;
    private readonly Func<BackgroundManagementRequest> requestFactory;
    private readonly Action<BackgroundManagementResult> resultHandler;
    private bool isManageRunning;
    private bool isReconcileScheduled;
    private bool reconcileRequested;
    private bool isStarted;

    public BackgroundManagementCoordinator(
        IBackgroundManagementService backgroundManagementService,
        IUiTimer manageTimer,
        Action<Func<Task>> scheduleOnUiThread,
        Func<BackgroundManagementRequest> requestFactory,
        Action<BackgroundManagementResult> resultHandler)
    {
        this.backgroundManagementService = backgroundManagementService ?? throw new ArgumentNullException(nameof(backgroundManagementService));
        this.manageTimer = manageTimer ?? throw new ArgumentNullException(nameof(manageTimer));
        this.scheduleOnUiThread = scheduleOnUiThread ?? throw new ArgumentNullException(nameof(scheduleOnUiThread));
        this.requestFactory = requestFactory ?? throw new ArgumentNullException(nameof(requestFactory));
        this.resultHandler = resultHandler ?? throw new ArgumentNullException(nameof(resultHandler));

        this.manageTimer.Tick += OnManageTimerTick;
    }

    public void Start()
    {
        if (isStarted)
        {
            return;
        }

        isStarted = true;
        manageTimer.Start();
    }

    public Task RunNowAsync(CancellationToken cancellationToken = default)
    {
        return RunLoopAsync(cancellationToken);
    }

    public Task RequestImmediateReconcileAsync(CancellationToken cancellationToken = default)
    {
        reconcileRequested = true;
        if (!isManageRunning)
        {
            ScheduleReconcile();
        }

        return Task.CompletedTask;
    }

    private void ScheduleReconcile()
    {
        if (isReconcileScheduled)
        {
            return;
        }

        isReconcileScheduled = true;
        scheduleOnUiThread(async () => await RunLoopAsync());
    }

    private async void OnManageTimerTick(object? sender, EventArgs e)
    {
        await RunLoopAsync();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken = default)
    {
        if (isManageRunning)
        {
            return;
        }

        isManageRunning = true;
        isReconcileScheduled = false;
        try
        {
            do
            {
                reconcileRequested = false;
                var result = await backgroundManagementService.RunPassAsync(requestFactory(), cancellationToken);
                resultHandler(result);
            }
            while (reconcileRequested);
        }
        finally
        {
            isManageRunning = false;
            if (reconcileRequested)
            {
                ScheduleReconcile();
            }
        }
    }
}
