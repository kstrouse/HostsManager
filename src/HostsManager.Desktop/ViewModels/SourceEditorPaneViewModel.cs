using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using HostsManager.Desktop.Models;

namespace HostsManager.Desktop.ViewModels;

public sealed class SourceEditorPaneViewModel : ViewModelBase
{
    private readonly MainWindowViewModel owner;

    public SourceEditorPaneViewModel(MainWindowViewModel owner)
    {
        this.owner = owner;
        owner.PropertyChanged += OnOwnerPropertyChanged;
    }

    public SelectedSourceDetailsViewModel SelectedSourceDetails => owner.SelectedSourceDetails;

    public LocalSourceEditorViewModel LocalEditor => owner.LocalEditor;

    public RemoteSourceEditorViewModel RemoteEditor => owner.RemoteEditor;

    public HostProfile? SelectedProfile => owner.SelectedProfile;

    public bool IsSelectedEntriesReadOnly => owner.IsSelectedEntriesReadOnly;

    public bool IsSystemHostsEditingEnabled => owner.IsSystemHostsEditingEnabled;

    public bool SelectedSourceChangedExternally => owner.SelectedSourceChangedExternally;

    public string SelectedSourceExternalChangeName => owner.SelectedSourceExternalChangeName;

    public IAsyncRelayCommand SaveSelectedSourceCommand => owner.SaveSelectedSourceCommand;

    public string StatusMessage
    {
        get => owner.StatusMessage;
        set => owner.StatusMessage = value;
    }

    public Task RequestImmediateReconcileAsync() => owner.RequestImmediateReconcileAsync();

    public Task<bool> ReloadSelectedSourceFromDiskAsync() => owner.ReloadSelectedSourceFromDiskAsync();

    public void DismissSelectedSourceExternalChangeNotification() => owner.DismissSelectedSourceExternalChangeNotification();

    private void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainWindowViewModel.SelectedProfile):
                OnPropertyChanged(nameof(SelectedProfile));
                break;
            case nameof(MainWindowViewModel.IsSelectedEntriesReadOnly):
                OnPropertyChanged(nameof(IsSelectedEntriesReadOnly));
                break;
            case nameof(MainWindowViewModel.IsSystemHostsEditingEnabled):
                OnPropertyChanged(nameof(IsSystemHostsEditingEnabled));
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
}
