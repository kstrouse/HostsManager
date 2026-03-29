using System.Threading.Tasks;
using System.Linq;
using HostsManager.Desktop.Services;

namespace HostsManager.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    public Task RequestImmediateReconcileAsync()
    {
        return backgroundManagementCoordinator.RequestImmediateReconcileAsync();
    }

    private BackgroundManagementRequest BuildBackgroundManagementRequest()
    {
        return new BackgroundManagementRequest
        {
            MinimizeToTrayOnClose = MinimizeToTrayOnClose,
            RunAtStartup = RunAtStartup,
            Profiles = Profiles.ToList(),
            SelectedProfile = SelectedProfile
        };
    }

    private void ApplyBackgroundManagementResult(BackgroundManagementResult result)
    {
        foreach (var source in result.MissingStateChangedSources)
            ApplyMissingLocalSourceStateChanged(source);

        if (result.SourceWithExternalChanges is not null)
            SetSelectedSourceExternalChangeNotification(result.SourceWithExternalChanges);

        if (result.SelectedProfileChanged)
            OnPropertyChanged(nameof(SelectedProfile));

        HasPendingElevatedHostsUpdate = result.HasPendingElevatedHostsUpdate;
        if (!string.IsNullOrWhiteSpace(result.StatusMessage))
            StatusMessage = result.StatusMessage;
    }
}
