using CommunityToolkit.Mvvm.ComponentModel;

namespace HostsManager.Desktop.Models;

public partial class AzureZoneSelectionItem : ObservableObject
{
    public string ZoneName { get; set; } = string.Empty;
    public string ResourceGroup { get; set; } = string.Empty;

    [ObservableProperty]
    private bool isEnabled = true;

    public string DisplayLabel => $"{ZoneName} ({ResourceGroup})";
}
