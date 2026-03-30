namespace HostsManager.Desktop.Tests;

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HostsManager.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch
        {
        }
    }
}

internal sealed class ManualTimer : IUiTimer
{
    public event EventHandler? Tick;

    public TimeSpan Interval { get; set; }

    public int StartCallCount { get; private set; }

    public int StopCallCount { get; private set; }

    public void Start()
    {
        StartCallCount++;
    }

    public void Stop()
    {
        StopCallCount++;
    }

    public void Fire()
    {
        Tick?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed class FakeStartupRegistrationService : IStartupRegistrationService
{
    public bool IsSupported { get; set; }

    public bool? LastSetEnabled { get; private set; }

    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(LastSetEnabled ?? false);
    }

    public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        LastSetEnabled = enabled;
        return Task.CompletedTask;
    }
}

internal sealed class FakeWindowsElevationService : IWindowsElevationService
{
    public bool IsSupported { get; set; }

    public bool IsProcessElevated()
    {
        return false;
    }

    public bool TryRelaunchElevated(StartupAction action, string? payloadPath = null, bool startInBackground = false)
    {
        return false;
    }
}

internal sealed class FakeAzurePrivateDnsService : IAzurePrivateDnsService
{
    public IReadOnlyList<AzureSubscriptionOption> Subscriptions { get; set; } = [];
    public IReadOnlyList<AzurePrivateDnsZoneInfo> Zones { get; set; } = [];
    public string HostsEntries { get; set; } = string.Empty;
    public int ListZonesCallCount { get; private set; }
    public bool? LastStripPrivatelinkSubdomain { get; private set; }

    public Task<IReadOnlyList<AzureSubscriptionOption>> ListSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Subscriptions);
    }

    public Task<IReadOnlyList<AzurePrivateDnsZoneInfo>> ListZonesAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        ListZonesCallCount++;
        return Task.FromResult(Zones);
    }

    public Task<string> BuildHostsEntriesAsync(
        string subscriptionId,
        IEnumerable<AzurePrivateDnsZoneInfo> includedZones,
        bool stripPrivatelinkSubdomain = false,
        CancellationToken cancellationToken = default)
    {
        LastStripPrivatelinkSubdomain = stripPrivatelinkSubdomain;
        return Task.FromResult(HostsEntries);
    }
}

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> handler;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        this.handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(handler(request));
    }
}
