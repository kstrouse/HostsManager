using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace HostsManager.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task SaveProfilesAsync()
    {
        try
        {
            await profilePersistenceService.SaveConfigurationAndMarkSavedAsync(
                MinimizeToTrayOnClose,
                RunAtStartup,
                Profiles);
            await backgroundManagementCoordinator.RunNowAsync();
            StatusMessage = "Sources saved.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }
}
