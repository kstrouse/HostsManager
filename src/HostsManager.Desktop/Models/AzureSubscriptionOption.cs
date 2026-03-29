namespace HostsManager.Desktop.Models;

public class AzureSubscriptionOption
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public string DisplayLabel => string.IsNullOrWhiteSpace(Name)
        ? Id
        : $"{Name} ({Id})";
}
