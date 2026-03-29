using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using HostsManager.Desktop.Models;
using HostsManager.Desktop.Services;

namespace HostsManager.Desktop.ViewModels;

public sealed partial class SourceListViewModel : ViewModelBase
{
    private readonly MainWindowViewModel owner;
    private readonly ILocalSourceService localSourceService;
    private readonly ILocalSourceWatcherService localSourceWatcherService;
    private readonly Func<HostProfile?, Task> handleRemoteSourceToggledAsync;
    private readonly Func<Task> requestImmediateReconcileAsync;

    public SourceListViewModel(
        MainWindowViewModel owner,
        ILocalSourceService localSourceService,
        ILocalSourceWatcherService localSourceWatcherService,
        Func<HostProfile?, Task> handleRemoteSourceToggledAsync,
        Func<Task> requestImmediateReconcileAsync)
    {
        this.owner = owner;
        this.localSourceService = localSourceService;
        this.localSourceWatcherService = localSourceWatcherService;
        this.handleRemoteSourceToggledAsync = handleRemoteSourceToggledAsync;
        this.requestImmediateReconcileAsync = requestImmediateReconcileAsync;
        owner.PropertyChanged += OnOwnerPropertyChanged;
    }

    public ObservableCollection<HostProfile> Profiles => owner.Profiles;

    public HostProfile? SelectedProfile
    {
        get => owner.SelectedProfile;
        set => owner.SelectedProfile = value;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteProfile))]
    private void DeleteProfile(HostProfile? source)
    {
        var target = source ?? owner.SelectedProfile;
        if (target is null)
            return;

        if (target.IsReadOnly)
        {
            owner.StatusMessage = "Read-only source cannot be deleted.";
            return;
        }

        var index = owner.Profiles.IndexOf(target);
        if (index < 0)
            return;

        owner.Profiles.Remove(target);
        owner.SelectedProfile = owner.Profiles.Count == 0
            ? null
            : owner.Profiles[Math.Clamp(index, 0, owner.Profiles.Count - 1)];

        localSourceWatcherService.MarkDirty();
        owner.StatusMessage = "Source removed.";
    }

    public void AddRemoteSource(RemoteTransport remoteTransport)
    {
        var sourceIndex = owner.Profiles.Count(source => !source.IsReadOnly) + 1;
        var profile = new HostProfile
        {
            Name = $"Remote Source {sourceIndex}",
            IsEnabled = true,
            SourceType = SourceType.Remote,
            RemoteTransport = remoteTransport,
            RefreshIntervalMinutes = "15",
            Entries = string.Empty
        };

        owner.Profiles.Add(profile);
        owner.SelectedProfile = profile;
        localSourceWatcherService.MarkDirty();
        owner.StatusMessage = $"New remote source created ({GetRemoteTransportDisplay(remoteTransport)}).";
    }

    public async Task AddNewLocalSourceAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var source = await localSourceService.CreateNewSourceAsync(path);
            owner.Profiles.Add(source);
            owner.SelectedProfile = source;
            localSourceWatcherService.MarkDirty();
            owner.StatusMessage = "Local source created and added.";
        }
        catch (Exception ex)
        {
            owner.StatusMessage = $"Create local source failed: {ex.Message}";
        }
    }

    public async Task AddExistingLocalSourceAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var source = await localSourceService.LoadExistingSourceAsync(path);
            owner.Profiles.Add(source);
            owner.SelectedProfile = source;
            localSourceWatcherService.MarkDirty();
            owner.StatusMessage = "Existing local source added.";
        }
        catch (Exception ex)
        {
            owner.StatusMessage = $"Add local source failed: {ex.Message}";
        }
    }

    public async Task HandleSourceToggledAsync(HostProfile? source)
    {
        await handleRemoteSourceToggledAsync(source);
        await requestImmediateReconcileAsync();
    }

    private void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedProfile))
        {
            OnPropertyChanged(nameof(SelectedProfile));
            DeleteProfileCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanDeleteProfile(HostProfile? source)
    {
        var target = source ?? owner.SelectedProfile;
        return target is not null && !target.IsReadOnly;
    }

    private static string GetRemoteTransportDisplay(RemoteTransport remoteTransport) =>
        remoteTransport switch
        {
            RemoteTransport.Http or RemoteTransport.Https => "HTTP/HTTPS",
            RemoteTransport.AzurePrivateDns => "Azure Private DNS",
            _ => remoteTransport.ToString()
        };
}
