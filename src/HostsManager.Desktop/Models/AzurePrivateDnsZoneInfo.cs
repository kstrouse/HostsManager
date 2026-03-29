namespace HostsManager.Desktop.Models;

public class AzurePrivateDnsZoneInfo
{
    public string ZoneName { get; set; } = string.Empty;
    public string ResourceGroup { get; set; } = string.Empty;

    public string DisplayLabel => $"{ZoneName} ({ResourceGroup})";
}
