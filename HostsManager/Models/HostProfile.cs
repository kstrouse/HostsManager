using System;
using System.Globalization;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HostsManager.Models;

public enum SourceType
{
    System,
    Local,
    Remote
}

public enum RemoteTransport
{
    Https,
    Http,
    Sftp,
    AzurePrivateDns
}

public partial class HostProfile : ObservableObject
{
    [ObservableProperty]
    private string id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string name = "New Source";

    [ObservableProperty]
    private bool isEnabled = true;

    [ObservableProperty]
    private SourceType sourceType = SourceType.Local;

    [ObservableProperty]
    private string localPath = string.Empty;

    [ObservableProperty]
    private RemoteTransport remoteTransport = RemoteTransport.Https;

    [ObservableProperty]
    private string remoteLocation = string.Empty;

    [ObservableProperty]
    private string azureSubscriptionId = string.Empty;

    [ObservableProperty]
    private string azureSubscriptionName = string.Empty;

    [ObservableProperty]
    private string azureExcludedZones = string.Empty;

    [ObservableProperty]
    private bool autoRefreshFromRemote;

    [ObservableProperty]
    private string refreshIntervalMinutes = "15";

    [ObservableProperty]
    private DateTimeOffset? lastSyncedAtUtc;

    [JsonIgnore]
    public DateTimeOffset? LastSyncedAtLocal => LastSyncedAtUtc?.ToLocalTime();

    [JsonIgnore]
    public string LastSyncedAtLocalDisplay => LastSyncedAtLocal?.ToString("G", CultureInfo.CurrentCulture) ?? string.Empty;

    [ObservableProperty]
    private string entries = string.Empty;

    [ObservableProperty]
    private bool isReadOnly;

    [ObservableProperty]
    private bool isMissingLocalFile;

    [JsonIgnore]
    public string? LastLoadedFromDiskEntries { get; set; }

    public string SourceTypeDisplay => SourceType switch
    {
        SourceType.Remote => $"Remote ({GetRemoteTransportDisplay(RemoteTransport)})",
        SourceType.Local => "Local",
        SourceType.System => "System",
        _ => SourceType.ToString()
    };

    [JsonIgnore]
    public bool IsRemoteSource => SourceType == SourceType.Remote;

    public string TransportDisplay => SourceType == SourceType.Remote
        ? GetRemoteTransportDisplay(RemoteTransport)
        : string.Empty;

    private static string GetRemoteTransportDisplay(RemoteTransport remoteTransport) =>
        remoteTransport switch
        {
            RemoteTransport.Http => "Http",
            RemoteTransport.Https => "Https",
            RemoteTransport.Sftp => "Sftp",
            RemoteTransport.AzurePrivateDns => "Azure Private DNS",
            _ => remoteTransport.ToString()
        };

    [JsonIgnore]
    public bool CanToggleEnabled => !IsReadOnly && !IsMissingLocalFile;

    partial void OnSourceTypeChanged(SourceType value)
    {
        OnPropertyChanged(nameof(SourceTypeDisplay));
        OnPropertyChanged(nameof(TransportDisplay));
        OnPropertyChanged(nameof(IsRemoteSource));
    }

    partial void OnRemoteTransportChanged(RemoteTransport value)
    {
        OnPropertyChanged(nameof(SourceTypeDisplay));
        OnPropertyChanged(nameof(TransportDisplay));
    }

    partial void OnLastSyncedAtUtcChanged(DateTimeOffset? value)
    {
        OnPropertyChanged(nameof(LastSyncedAtLocal));
        OnPropertyChanged(nameof(LastSyncedAtLocalDisplay));
    }

    partial void OnIsReadOnlyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanToggleEnabled));
    }

    partial void OnIsMissingLocalFileChanged(bool value)
    {
        OnPropertyChanged(nameof(CanToggleEnabled));
    }
}
