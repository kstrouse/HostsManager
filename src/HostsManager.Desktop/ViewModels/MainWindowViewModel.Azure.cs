using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using HostsManager.Desktop.Models;
using HostsManager.Desktop.Services;

namespace HostsManager.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task LoadAzureSubscriptionsAsync()
    {
        if (IsAzureSubscriptionsLoading)
            return;

        IsAzureSubscriptionsLoading = true;
        StatusMessage = "Loading Azure subscriptions...";

        var result = await azureProfileCommandService.LoadSubscriptionsAsync(
            SelectedProfile,
            IsSystemHostsEditingEnabled);

        ReplaceAzureSubscriptions(result.Subscriptions);
        if (result.SelectedProfileChange is not null)
            ApplySelectedProfileChange(result.SelectedProfileChange);

        if (!string.IsNullOrWhiteSpace(result.StatusMessage))
            StatusMessage = result.StatusMessage;

        IsAzureSubscriptionsLoading = false;
    }

    [RelayCommand]
    private async Task RefreshAzureZonesAsync()
    {
        await LoadAndApplyAzureZonesAsync(
            () => azureProfileCommandService.RefreshZonesAsync(SelectedProfile));
    }

    private Task ApplyAzureZonesLoadResultAsync(AzureZonesLoadResult result)
    {
        if (result.ShouldReplaceZones)
            ReplaceAzureZones(result.Zones);

        if (!string.IsNullOrWhiteSpace(result.StatusMessage))
            StatusMessage = result.StatusMessage;

        return Task.CompletedTask;
    }

    private async Task LoadAndApplyAzureZonesAsync(Func<Task<AzureZonesLoadResult>> loadAsync)
    {
        IsAzureZonesLoading = true;
        try
        {
            await ApplyAzureZonesLoadResultAsync(await loadAsync());
        }
        finally
        {
            IsAzureZonesLoading = false;
        }
    }

    private void ReplaceAzureSubscriptions(IEnumerable<AzureSubscriptionOption> subscriptions)
    {
        AzureSubscriptions.Clear();
        foreach (var subscription in subscriptions)
            AzureSubscriptions.Add(subscription);
    }

    private void ReplaceAzureZones(IEnumerable<AzureZoneSelectionItem> zones)
    {
        foreach (var zone in AzureZones)
            zone.PropertyChanged -= OnAzureZoneSelectionChanged;

        AzureZones.Clear();

        foreach (var zone in zones)
        {
            zone.PropertyChanged += OnAzureZoneSelectionChanged;
            AzureZones.Add(zone);
        }

        UpdateSelectedProfileExcludedZonesFromSelection();
    }

    private void OnAzureZoneSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AzureZoneSelectionItem.IsEnabled))
            UpdateSelectedProfileExcludedZonesFromSelection();
    }

    private void UpdateSelectedProfileExcludedZonesFromSelection()
    {
        if (SelectedProfile is null ||
            SelectedProfile.SourceType != SourceType.Remote ||
            SelectedProfile.RemoteTransport != RemoteTransport.AzurePrivateDns)
            return;

        SelectedProfile.AzureExcludedZones = remoteSourceSyncService.BuildExcludedZones(AzureZones);
    }

    private async Task RefreshAzureZonesForCurrentSelectionAsync()
    {
        await LoadAndApplyAzureZonesAsync(
            () => azureProfileCommandService.RefreshZonesForSelectionAsync(SelectedProfile));
    }
}
