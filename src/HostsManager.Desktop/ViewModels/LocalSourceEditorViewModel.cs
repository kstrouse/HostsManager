using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using HostsManager.Desktop.Models;
using HostsManager.Desktop.Services;

namespace HostsManager.Desktop.ViewModels;

public sealed partial class LocalSourceEditorViewModel : ViewModelBase
{
    private readonly MainWindowViewModel owner;
    private readonly ILocalSourceService localSourceService;
    private readonly ILocalSourceWatcherService localSourceWatcherService;
    private readonly Func<Task> saveProfilesAsync;
    private readonly Func<Task> runBackgroundManagementAsync;
    private readonly Action notifySelectedProfileChanged;
    private readonly Action notifySelectedEntriesReadOnlyChanged;
    private readonly Action<ProcessStartInfo> startProcess;
    private HostProfile? observedProfile;

    public LocalSourceEditorViewModel(
        MainWindowViewModel owner,
        ILocalSourceService localSourceService,
        ILocalSourceWatcherService localSourceWatcherService,
        Func<Task> saveProfilesAsync,
        Func<Task> runBackgroundManagementAsync,
        Action notifySelectedProfileChanged,
        Action notifySelectedEntriesReadOnlyChanged,
        Action<ProcessStartInfo> startProcess)
    {
        this.owner = owner;
        this.localSourceService = localSourceService;
        this.localSourceWatcherService = localSourceWatcherService;
        this.saveProfilesAsync = saveProfilesAsync;
        this.runBackgroundManagementAsync = runBackgroundManagementAsync;
        this.notifySelectedProfileChanged = notifySelectedProfileChanged;
        this.notifySelectedEntriesReadOnlyChanged = notifySelectedEntriesReadOnlyChanged;
        this.startProcess = startProcess;
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

    public async Task RenameSelectedLocalFileAsync(string? requestedFileName)
    {
        var selectedProfile = owner.SelectedProfile;
        if (selectedProfile is null || selectedProfile.SourceType != SourceType.Local)
            return;

        var requestedName = (requestedFileName ?? string.Empty).Trim();

        try
        {
            await localSourceService.RenameAsync(selectedProfile, requestedName);
            localSourceWatcherService.MarkDirty();
            notifySelectedProfileChanged();
            await saveProfilesAsync();
            owner.StatusMessage = $"Renamed local file to {Path.GetFileName(selectedProfile.LocalPath)}.";
        }
        catch (InvalidOperationException ex)
        {
            owner.StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            owner.StatusMessage = $"Rename failed: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenSelectedLocalFolder))]
    private void OpenSelectedLocalFolder()
    {
        var selectedProfile = owner.SelectedProfile;
        if (selectedProfile is null || selectedProfile.SourceType != SourceType.Local)
            return;

        try
        {
            var folder = Path.GetDirectoryName(selectedProfile.LocalPath);
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                owner.StatusMessage = "Local source folder not found.";
                return;
            }

            startProcess(BuildOpenFolderStartInfo(folder));
        }
        catch (Exception ex)
        {
            owner.StatusMessage = $"Open folder failed: {ex.Message}";
        }
    }

    private bool CanOpenSelectedLocalFolder() =>
        owner.SelectedProfile is { SourceType: SourceType.Local } profile &&
        !string.IsNullOrWhiteSpace(profile.LocalPath);

    [RelayCommand(CanExecute = nameof(CanRecreateMissingLocalFile))]
    private async Task RecreateMissingLocalFileAsync()
    {
        if (owner.SelectedProfile is not { SourceType: SourceType.Local, IsMissingLocalFile: true } profile ||
            string.IsNullOrWhiteSpace(profile.LocalPath))
            return;

        try
        {
            await localSourceService.RecreateMissingFileAsync(profile);
            localSourceWatcherService.MarkDirty();

            notifySelectedProfileChanged();
            notifySelectedEntriesReadOnlyChanged();
            await saveProfilesAsync();
            await runBackgroundManagementAsync();
            owner.StatusMessage = $"Re-created local source file: {Path.GetFileName(profile.LocalPath)}";
        }
        catch (InvalidOperationException ex)
        {
            owner.StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            owner.StatusMessage = $"Re-create local file failed: {ex.Message}";
        }
    }

    private bool CanRecreateMissingLocalFile() =>
        owner.SelectedProfile is { SourceType: SourceType.Local, IsMissingLocalFile: true } profile &&
        !string.IsNullOrWhiteSpace(profile.LocalPath);

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
        OpenSelectedLocalFolderCommand.NotifyCanExecuteChanged();
        RecreateMissingLocalFileCommand.NotifyCanExecuteChanged();
    }

    private static ProcessStartInfo BuildOpenFolderStartInfo(string folder)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folder}\"",
                UseShellExecute = true
            };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return new ProcessStartInfo
            {
                FileName = "open",
                Arguments = $"\"{folder}\"",
                UseShellExecute = false
            };

        return new ProcessStartInfo
        {
            FileName = "xdg-open",
            Arguments = $"\"{folder}\"",
            UseShellExecute = false
        };
    }
}
