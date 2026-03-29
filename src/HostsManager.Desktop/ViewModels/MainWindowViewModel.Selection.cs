using HostsManager.Desktop.Models;
using HostsManager.Desktop.Services;

namespace HostsManager.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    partial void OnSelectedProfileChanged(HostProfile? value)
    {
        var change = profileSelectionService.EvaluateSelectedProfile(value, IsSystemHostsEditingEnabled, AzureSubscriptions);

        if (change.DisableSystemHostsEditing)
            IsSystemHostsEditingEnabled = false;

        DismissSelectedSourceExternalChangeNotification();

        ApplySelectedProfileChange(change);
        NotifySelectedProfileStateChanged();
    }

    partial void OnSelectedAzureSubscriptionChanged(AzureSubscriptionOption? value)
    {
        if (isSyncingSelectedAzureSubscription)
            return;

        var change = profileSelectionService.CreateAzureSubscriptionSelectionChange(SelectedProfile, value);
        if (change is null)
            return;

        var selectedProfile = SelectedProfile;
        if (selectedProfile is null)
            return;

        selectedProfile.AzureSubscriptionId = change.SubscriptionId;
        selectedProfile.AzureSubscriptionName = change.SubscriptionName;
        _ = RefreshAzureZonesForCurrentSelectionAsync();
        OnPropertyChanged(nameof(CanRefreshAzureZones));
    }

    partial void OnIsAzureSubscriptionsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanLoadAzureSubscriptions));
    }

    partial void OnIsAzureZonesLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRefreshAzureZones));
    }

    partial void OnIsSystemHostsEditingEnabledChanged(bool value)
    {
        if (!IsSystemSelected)
            return;

        StatusMessage = value
            ? "System hosts editing enabled. Review changes carefully before saving."
            : "System hosts editing disabled.";
    }

    private void ApplySelectedProfileChange(SelectedProfileChange change)
    {
        if (change.ClearAzureZones)
            ReplaceAzureZones([]);

        if (change.SubscriptionToInsert is not null)
            AzureSubscriptions.Insert(0, change.SubscriptionToInsert);

        if (change.ShouldUpdateSelectedAzureSubscription)
        {
            isSyncingSelectedAzureSubscription = true;
            try
            {
                SelectedAzureSubscription = change.SelectedAzureSubscription;
            }
            finally
            {
                isSyncingSelectedAzureSubscription = false;
            }
        }

        if (change.RefreshAzureZones)
            _ = RefreshAzureZonesForCurrentSelectionAsync();
    }

    private void NotifySelectedProfileStateChanged()
    {
        OnPropertyChanged(nameof(IsSelectedSourceReadOnly));
        OnPropertyChanged(nameof(IsSelectedEntriesReadOnly));
        OnPropertyChanged(nameof(SelectedSourceTypeDisplay));
        OnPropertyChanged(nameof(IsSystemSelected));
        OnPropertyChanged(nameof(IsRemoteSelected));
        OnPropertyChanged(nameof(IsHttpRemoteSelected));
        OnPropertyChanged(nameof(IsAzurePrivateDnsRemoteSelected));
        OnPropertyChanged(nameof(CanRefreshAzureZones));
        ReloadLocalSourceCommand.NotifyCanExecuteChanged();
        SaveEntriesToLocalCommand.NotifyCanExecuteChanged();
        OpenSelectedLocalFolderCommand.NotifyCanExecuteChanged();
        ReadSelectedRemoteHostsCommand.NotifyCanExecuteChanged();
        DeleteProfileCommand.NotifyCanExecuteChanged();
        RecreateMissingLocalFileCommand.NotifyCanExecuteChanged();
    }
}
