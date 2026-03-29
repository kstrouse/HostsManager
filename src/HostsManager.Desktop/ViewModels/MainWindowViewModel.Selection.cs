using HostsManager.Desktop.Models;

namespace HostsManager.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    partial void OnSelectedProfileChanged(HostProfile? value)
    {
        if (value?.SourceType != SourceType.System && IsSystemHostsEditingEnabled)
            IsSystemHostsEditingEnabled = false;

        DismissSelectedSourceExternalChangeNotification();
        NotifySelectedProfileStateChanged();
    }

    partial void OnIsSystemHostsEditingEnabledChanged(bool value)
    {
        if (SelectedProfile?.SourceType != SourceType.System)
            return;

        StatusMessage = value
            ? "System hosts editing enabled. Review changes carefully before saving."
            : "System hosts editing disabled.";
    }

    private void NotifySelectedProfileStateChanged()
    {
        OnPropertyChanged(nameof(IsSelectedEntriesReadOnly));
        ReloadLocalSourceCommand.NotifyCanExecuteChanged();
        SaveEntriesToLocalCommand.NotifyCanExecuteChanged();
    }
}
