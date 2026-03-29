using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using HostsManager.Desktop.Models;
using HostsManager.Desktop.Services;

namespace HostsManager.Desktop.ViewModels;

public sealed partial class SourceEditorPaneViewModel : ViewModelBase
{
    private readonly MainWindowViewModel owner;
    private readonly ILocalSourceService localSourceService;
    private readonly ISystemHostsWorkflowService systemHostsWorkflowService;
    private readonly ILocalSourceWatcherService localSourceWatcherService;
    private readonly Func<Task> saveProfilesAsync;
    private readonly Func<HostProfile, Task> saveSystemHostsDirectAsync;
    private readonly Action notifySelectedProfileChanged;
    private HostProfile? observedProfile;

    public SourceEditorPaneViewModel(
        MainWindowViewModel owner,
        ILocalSourceService localSourceService,
        ISystemHostsWorkflowService systemHostsWorkflowService,
        ILocalSourceWatcherService localSourceWatcherService,
        Func<Task> saveProfilesAsync,
        Func<HostProfile, Task> saveSystemHostsDirectAsync,
        Action notifySelectedProfileChanged)
    {
        this.owner = owner;
        this.localSourceService = localSourceService;
        this.systemHostsWorkflowService = systemHostsWorkflowService;
        this.localSourceWatcherService = localSourceWatcherService;
        this.saveProfilesAsync = saveProfilesAsync;
        this.saveSystemHostsDirectAsync = saveSystemHostsDirectAsync;
        this.notifySelectedProfileChanged = notifySelectedProfileChanged;
        owner.PropertyChanged += OnOwnerPropertyChanged;
        UpdateObservedProfile(owner.SelectedProfile);
    }

    public SelectedSourceDetailsViewModel SelectedSourceDetails => owner.SelectedSourceDetails;

    public LocalSourceEditorViewModel LocalEditor => owner.LocalEditor;

    public RemoteSourceEditorViewModel RemoteEditor => owner.RemoteEditor;

    public HostProfile? SelectedProfile => owner.SelectedProfile;

    public bool IsSelectedEntriesReadOnly => owner.IsSelectedEntriesReadOnly;

    public bool IsSystemHostsEditingEnabled => owner.IsSystemHostsEditingEnabled;

    public bool SelectedSourceChangedExternally => owner.SelectedSourceChangedExternally;

    public string SelectedSourceExternalChangeName => owner.SelectedSourceExternalChangeName;

    public string StatusMessage
    {
        get => owner.StatusMessage;
        set => owner.StatusMessage = value;
    }

    [RelayCommand(CanExecute = nameof(CanSaveSelectedSource))]
    private async Task SaveSelectedSourceAsync()
    {
        var selectedProfile = owner.SelectedProfile;
        if (selectedProfile is null)
            return;

        if (selectedProfile.SourceType == SourceType.Local)
        {
            if (string.IsNullOrWhiteSpace(selectedProfile.LocalPath))
            {
                owner.StatusMessage = "Local source path is empty.";
                return;
            }

            try
            {
                await File.WriteAllTextAsync(selectedProfile.LocalPath, selectedProfile.Entries ?? string.Empty);
                selectedProfile.LastLoadedFromDiskEntries = selectedProfile.Entries ?? string.Empty;
                localSourceWatcherService.MarkDirty();
                await saveProfilesAsync();
                owner.StatusMessage = $"Saved source: {selectedProfile.Name}";
            }
            catch (Exception ex)
            {
                owner.StatusMessage = $"Save local failed: {ex.Message}";
            }

            return;
        }

        if (selectedProfile.SourceType == SourceType.System)
        {
            if (!owner.IsSystemHostsEditingEnabled)
            {
                owner.StatusMessage = "Enable system hosts editing before saving direct edits.";
                return;
            }

            await saveSystemHostsDirectAsync(selectedProfile);
            return;
        }

        if (selectedProfile.IsReadOnly)
            return;

        await saveProfilesAsync();
        owner.StatusMessage = $"Saved source: {selectedProfile.Name}";
    }

    public async Task<bool> ReloadSelectedSourceFromDiskAsync()
    {
        var selectedProfile = owner.SelectedProfile;
        if (selectedProfile is null)
            return false;

        if (selectedProfile.SourceType is not (SourceType.Local or SourceType.System))
            return false;

        var changed = selectedProfile.SourceType == SourceType.System
            ? await systemHostsWorkflowService.ReloadSystemSourceAsync(selectedProfile)
            : await localSourceService.ReloadFromDiskAsync(selectedProfile);

        DismissSelectedSourceExternalChangeNotification();

        if (changed)
        {
            notifySelectedProfileChanged();
            owner.StatusMessage = $"Reloaded external changes for {selectedProfile.Name}.";
        }

        return changed;
    }

    public void DismissSelectedSourceExternalChangeNotification()
    {
        owner.SelectedSourceChangedExternally = false;
        owner.SelectedSourceExternalChangeName = string.Empty;
    }

    private void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainWindowViewModel.SelectedProfile):
                UpdateObservedProfile(owner.SelectedProfile);
                OnPropertyChanged(nameof(SelectedProfile));
                SaveSelectedSourceCommand.NotifyCanExecuteChanged();
                break;
            case nameof(MainWindowViewModel.IsSelectedEntriesReadOnly):
                OnPropertyChanged(nameof(IsSelectedEntriesReadOnly));
                break;
            case nameof(MainWindowViewModel.IsSystemHostsEditingEnabled):
                OnPropertyChanged(nameof(IsSystemHostsEditingEnabled));
                SaveSelectedSourceCommand.NotifyCanExecuteChanged();
                break;
            case nameof(MainWindowViewModel.SelectedSourceChangedExternally):
                OnPropertyChanged(nameof(SelectedSourceChangedExternally));
                break;
            case nameof(MainWindowViewModel.SelectedSourceExternalChangeName):
                OnPropertyChanged(nameof(SelectedSourceExternalChangeName));
                break;
            case nameof(MainWindowViewModel.StatusMessage):
                OnPropertyChanged(nameof(StatusMessage));
                break;
        }
    }

    private bool CanSaveSelectedSource() =>
        owner.SelectedProfile switch
        {
            null => false,
            { SourceType: SourceType.Local, IsMissingLocalFile: true } => false,
            { SourceType: SourceType.System } => owner.IsSystemHostsEditingEnabled,
            var profile => !profile.IsReadOnly
        };

    private void OnSelectedProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(HostProfile.SourceType) or nameof(HostProfile.IsMissingLocalFile) or nameof(HostProfile.IsReadOnly) or nameof(HostProfile.LocalPath)))
            return;

        OnPropertyChanged(nameof(SelectedProfile));
        OnPropertyChanged(nameof(IsSelectedEntriesReadOnly));
        SaveSelectedSourceCommand.NotifyCanExecuteChanged();
    }

    private void UpdateObservedProfile(HostProfile? profile)
    {
        if (ReferenceEquals(observedProfile, profile))
            return;

        if (observedProfile is not null)
            observedProfile.PropertyChanged -= OnSelectedProfilePropertyChanged;

        observedProfile = profile;

        if (observedProfile is not null)
            observedProfile.PropertyChanged += OnSelectedProfilePropertyChanged;
    }
}
