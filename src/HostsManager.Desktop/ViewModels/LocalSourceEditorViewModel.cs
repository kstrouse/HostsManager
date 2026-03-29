using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using HostsManager.Desktop.Models;

namespace HostsManager.Desktop.ViewModels;

public sealed class LocalSourceEditorViewModel : ViewModelBase
{
    private readonly MainWindowViewModel owner;
    private HostProfile? observedProfile;

    public LocalSourceEditorViewModel(MainWindowViewModel owner)
    {
        this.owner = owner;
        owner.PropertyChanged += OnOwnerPropertyChanged;
        UpdateObservedProfile(owner.SelectedProfile);
    }

    public bool IsVisible => owner.SelectedProfile?.SourceType == SourceType.Local;

    public bool IsSelectedLocalFileMissing =>
        owner.SelectedProfile is { SourceType: SourceType.Local, IsMissingLocalFile: true };

    public string SelectedLocalFilePath =>
        owner.SelectedProfile?.SourceType == SourceType.Local
            ? owner.SelectedProfile.LocalPath
            : string.Empty;

    public IRelayCommand OpenSelectedLocalFolderCommand => owner.OpenSelectedLocalFolderCommand;

    public IAsyncRelayCommand RecreateMissingLocalFileCommand => owner.RecreateMissingLocalFileCommand;

    public async Task RenameSelectedLocalFileAsync(string? requestedFileName)
    {
        await owner.RenameSelectedLocalFileAsync(requestedFileName);
    }

    private void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.SelectedProfile))
            return;

        UpdateObservedProfile(owner.SelectedProfile);
        RaiseLocalStateChanged();
    }

    private void OnSelectedProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HostProfile.LocalPath) or nameof(HostProfile.IsMissingLocalFile) or nameof(HostProfile.SourceType))
            RaiseLocalStateChanged();
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

    private void RaiseLocalStateChanged()
    {
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(IsSelectedLocalFileMissing));
        OnPropertyChanged(nameof(SelectedLocalFilePath));
    }
}
