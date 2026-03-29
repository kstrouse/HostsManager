using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using HostsManager.Desktop.Models;

namespace HostsManager.Desktop.ViewModels;

public sealed class SourceListViewModel : ViewModelBase
{
    private readonly MainWindowViewModel owner;

    public SourceListViewModel(MainWindowViewModel owner)
    {
        this.owner = owner;
        owner.PropertyChanged += OnOwnerPropertyChanged;
    }

    public ObservableCollection<HostProfile> Profiles => owner.Profiles;

    public HostProfile? SelectedProfile
    {
        get => owner.SelectedProfile;
        set => owner.SelectedProfile = value;
    }

    public IRelayCommand<HostProfile?> DeleteProfileCommand => owner.DeleteProfileCommand;

    public void AddRemoteSource(RemoteTransport remoteTransport) => owner.AddRemoteSource(remoteTransport);

    public Task AddNewLocalSourceAsync(string path) => owner.AddNewLocalSourceAsync(path);

    public Task AddExistingLocalSourceAsync(string path) => owner.AddExistingLocalSourceAsync(path);

    public async Task HandleSourceToggledAsync(HostProfile? source)
    {
        await owner.HandleRemoteSourceToggledAsync(source);
        await owner.RequestImmediateReconcileAsync();
    }

    private void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedProfile))
            OnPropertyChanged(nameof(SelectedProfile));
    }
}
