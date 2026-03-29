using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using HostsManager.Desktop.Models;
using HostsManager.Desktop.ViewModels;

namespace HostsManager.Desktop.Views;

public partial class SourceEditorPaneView : UserControl
{
    private SourceEditorPaneViewModel? currentViewModel;
    private readonly DispatcherTimer editorSyncTimer;
    private readonly HostsEntriesEditorState editorState;
    private bool isExternalChangeDialogOpen;

    public SourceEditorPaneView()
    {
        InitializeComponent();

        AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);

        editorState = new HostsEntriesEditorState(
            this.FindControl<TextEditor>("HostsEntriesEditor"),
            this.FindControl<Button>("SaveSelectedSourceButton"),
            this.FindControl<TextBlock>("UnsavedIndicator"));
        editorState.InitializeEditor();

        editorSyncTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        editorSyncTimer.Tick += (_, _) =>
        {
            if (currentViewModel is not null)
                editorState.SyncFromSelectedProfile(currentViewModel);
        };
        editorSyncTimer.Start();

        DataContextChanged += (_, _) => BindViewModel();
        BindViewModel();
    }

    private void BindViewModel()
    {
        if (currentViewModel is not null)
            currentViewModel.PropertyChanged -= OnViewModelPropertyChanged;

        currentViewModel = DataContext as SourceEditorPaneViewModel;

        if (currentViewModel is null)
        {
            editorState.SetSaveButtonEnabled(false);
            return;
        }

        currentViewModel.PropertyChanged += OnViewModelPropertyChanged;
        editorState.SyncFromSelectedProfile(currentViewModel, force: true);
        editorState.UpdateSaveButtonAvailability(currentViewModel);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not SourceEditorPaneViewModel vm)
            return;

        if (e.PropertyName == nameof(SourceEditorPaneViewModel.SelectedProfile))
        {
            editorState.SyncFromSelectedProfile(vm, force: true);
            return;
        }

        if (e.PropertyName == nameof(SourceEditorPaneViewModel.IsSelectedEntriesReadOnly))
        {
            editorState.UpdateUnsavedIndicatorVisibility(vm);
            editorState.UpdateSaveButtonAvailability(vm);
            return;
        }

        if (e.PropertyName == nameof(SourceEditorPaneViewModel.SelectedSourceChangedExternally) &&
            vm.SelectedSourceChangedExternally)
        {
            _ = HandleSelectedSourceExternalChangeAsync(vm);
        }
    }

    private void SaveSelectedSourceClick(object? sender, RoutedEventArgs e)
    {
        if (currentViewModel is null)
            return;

        _ = SaveSelectedSourceAsync(currentViewModel, requireUnsavedEditorDraft: false);
    }

    private async void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.S)
            return;

        var modifiers = e.KeyModifiers;
        var isSaveShortcut = modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);
        if (!isSaveShortcut || currentViewModel is null || !editorState.IsEditorFocusedWithin)
            return;

        e.Handled = true;
        await SaveSelectedSourceAsync(currentViewModel, requireUnsavedEditorDraft: true);
    }

    private async Task SaveSelectedSourceAsync(SourceEditorPaneViewModel vm, bool requireUnsavedEditorDraft)
    {
        if (requireUnsavedEditorDraft && !editorState.HasPendingEditorDraft(vm))
            return;

        editorState.CommitEditorToSelectedProfile(vm);

        if (!vm.SaveSelectedSourceCommand.CanExecute(null))
            return;

        if (!vm.IsSelectedEntriesReadOnly && HostsSyntaxColorizer.HasIssues(editorState.CurrentText))
        {
            vm.StatusMessage = "Fix hosts entry issues before saving.";
            editorState.UpdateSaveButtonAvailability(vm);
            return;
        }

        var isDirectSystemSave = vm.SelectedProfile?.SourceType == SourceType.System && vm.IsSystemHostsEditingEnabled;
        if (isDirectSystemSave)
        {
            var confirmed = await ShowSystemHostsSaveConfirmationAsync();
            if (!confirmed)
            {
                vm.StatusMessage = "System hosts save canceled.";
                return;
            }
        }

        await vm.SaveSelectedSourceCommand.ExecuteAsync(null);
        await vm.RequestImmediateReconcileAsync();
        editorState.UpdateUnsavedIndicatorVisibility(vm);
        editorState.UpdateSaveButtonAvailability(vm);
    }

    private async Task HandleSelectedSourceExternalChangeAsync(SourceEditorPaneViewModel vm)
    {
        if (isExternalChangeDialogOpen)
            return;

        isExternalChangeDialogOpen = true;
        try
        {
            var sourceName = string.IsNullOrWhiteSpace(vm.SelectedSourceExternalChangeName)
                ? vm.SelectedProfile?.Name ?? "selected source"
                : vm.SelectedSourceExternalChangeName;

            var shouldReload = await ShowExternalChangeReloadPromptAsync(sourceName);
            if (!shouldReload)
            {
                vm.DismissSelectedSourceExternalChangeNotification();
                vm.StatusMessage = "External change detected. Kept current in-editor content.";
                return;
            }

            await vm.ReloadSelectedSourceFromDiskAsync();
            editorState.SyncFromSelectedProfile(vm, force: true);
            editorState.UpdateUnsavedIndicatorVisibility(vm);
            editorState.UpdateSaveButtonAvailability(vm);
        }
        finally
        {
            isExternalChangeDialogOpen = false;
        }
    }

    private async Task<bool> ShowSystemHostsSaveConfirmationAsync()
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
            return false;

        var confirmed = false;

        var saveButton = new Button
        {
            Content = "Save",
            MinWidth = 90,
            IsDefault = true
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 90,
            IsCancel = true
        };

        var dialog = new Window
        {
            Title = "Confirm System Hosts Save",
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = new Border
            {
                Padding = new Thickness(16),
                Child = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Save direct edits to the system hosts file?"
                        },
                        new TextBlock
                        {
                            Opacity = 0.72,
                            Text = "This writes the full hosts file and may override unmanaged manual changes if not reviewed."
                        },
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            Spacing = 8,
                            Children =
                            {
                                cancelButton,
                                saveButton
                            }
                        }
                    }
                }
            }
        };

        cancelButton.Click += (_, _) => dialog.Close();
        saveButton.Click += (_, _) =>
        {
            confirmed = true;
            dialog.Close();
        };

        await dialog.ShowDialog(owner);
        return confirmed;
    }

    private async Task<bool> ShowExternalChangeReloadPromptAsync(string sourceName)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
            return false;

        var shouldReload = false;

        var reloadButton = new Button
        {
            Content = "Reload",
            MinWidth = 90,
            IsDefault = true
        };

        var keepButton = new Button
        {
            Content = "Keep Current",
            MinWidth = 110,
            IsCancel = true
        };

        var dialog = new Window
        {
            Title = "External File Change Detected",
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = new Border
            {
                Padding = new Thickness(16),
                Child = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"{sourceName} changed on disk outside Hosts Manager."
                        },
                        new TextBlock
                        {
                            Opacity = 0.72,
                            Text = "Reload from disk now? Choosing Keep Current keeps what is currently in the editor."
                        },
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            Spacing = 8,
                            Children =
                            {
                                keepButton,
                                reloadButton
                            }
                        }
                    }
                }
            }
        };

        keepButton.Click += (_, _) => dialog.Close();
        reloadButton.Click += (_, _) =>
        {
            shouldReload = true;
            dialog.Close();
        };

        await dialog.ShowDialog(owner);
        return shouldReload;
    }
}

internal sealed class HostsEntriesEditorState
{
    private readonly TextEditor? hostsEntriesEditor;
    private readonly Button? saveSelectedSourceButton;
    private readonly TextBlock? unsavedIndicator;
    private bool isSyncingEditorText;
    private string? editorSourceProfileId;

    public HostsEntriesEditorState(TextEditor? hostsEntriesEditor, Button? saveSelectedSourceButton, TextBlock? unsavedIndicator)
    {
        this.hostsEntriesEditor = hostsEntriesEditor;
        this.saveSelectedSourceButton = saveSelectedSourceButton;
        this.unsavedIndicator = unsavedIndicator;
    }

    public string? CurrentText => hostsEntriesEditor?.Text;

    public bool IsEditorFocusedWithin => hostsEntriesEditor?.IsKeyboardFocusWithin == true;

    public void InitializeEditor()
    {
        if (hostsEntriesEditor is null)
            return;

        hostsEntriesEditor.TextChanged += HostsEntriesEditorTextChanged;

        if (hostsEntriesEditor.TextArea is not null)
        {
            foreach (var leftMargin in hostsEntriesEditor.TextArea.LeftMargins)
            {
                if (leftMargin.GetType().Name.Contains("LineNumberMargin", StringComparison.Ordinal))
                    leftMargin.Margin = new Thickness(0, 0, 10, 0);
            }
        }

        if (hostsEntriesEditor.TextArea?.TextView is not null)
            hostsEntriesEditor.TextArea.TextView.LineTransformers.Add(new HostsSyntaxColorizer());
    }

    public void SyncFromSelectedProfile(SourceEditorPaneViewModel vm, bool force = false)
    {
        if (hostsEntriesEditor is null)
            return;

        var nextText = vm.SelectedProfile?.Entries ?? string.Empty;
        var nextProfileId = vm.SelectedProfile?.Id;

        if (isSyncingEditorText)
            return;

        var isSameSelectedProfile = string.Equals(editorSourceProfileId, nextProfileId, StringComparison.Ordinal);
        if (!force && isSameSelectedProfile && HasPendingEditorDraft(vm))
            return;

        if (string.Equals(hostsEntriesEditor.Text, nextText, StringComparison.Ordinal))
        {
            editorSourceProfileId = nextProfileId;
            return;
        }

        var previousCaretOffset = hostsEntriesEditor.CaretOffset;

        isSyncingEditorText = true;
        try
        {
            hostsEntriesEditor.Text = nextText;
            hostsEntriesEditor.CaretOffset = Math.Min(previousCaretOffset, hostsEntriesEditor.Text?.Length ?? 0);
            hostsEntriesEditor.TextArea?.TextView?.InvalidateVisual();
            editorSourceProfileId = nextProfileId;
        }
        finally
        {
            isSyncingEditorText = false;
        }

        UpdateUnsavedIndicatorVisibility(vm);
        UpdateSaveButtonAvailability(vm);
    }

    public void CommitEditorToSelectedProfile(SourceEditorPaneViewModel vm)
    {
        if (hostsEntriesEditor is null || vm.SelectedProfile is null || vm.IsSelectedEntriesReadOnly)
            return;

        var editorText = hostsEntriesEditor.Text ?? string.Empty;
        if (string.Equals(editorText, vm.SelectedProfile.Entries, StringComparison.Ordinal))
            return;

        vm.SelectedProfile.Entries = editorText;
        editorSourceProfileId = vm.SelectedProfile.Id;
        UpdateUnsavedIndicatorVisibility(vm);
        UpdateSaveButtonAvailability(vm);
    }

    public bool HasPendingEditorDraft(SourceEditorPaneViewModel vm)
    {
        if (hostsEntriesEditor is null || vm.SelectedProfile is null || vm.IsSelectedEntriesReadOnly)
            return false;

        var editorText = hostsEntriesEditor.Text ?? string.Empty;
        var profileText = vm.SelectedProfile.Entries ?? string.Empty;
        return !string.Equals(editorText, profileText, StringComparison.Ordinal);
    }

    public void UpdateUnsavedIndicatorVisibility(SourceEditorPaneViewModel vm)
    {
        if (unsavedIndicator is null)
            return;

        var hasEditableSelection = vm.SelectedProfile is not null && !vm.IsSelectedEntriesReadOnly;
        if (!hasEditableSelection || hostsEntriesEditor is null)
        {
            unsavedIndicator.IsVisible = false;
            return;
        }

        var editorText = hostsEntriesEditor.Text ?? string.Empty;
        var profileText = vm.SelectedProfile!.Entries ?? string.Empty;
        unsavedIndicator.IsVisible = !string.Equals(editorText, profileText, StringComparison.Ordinal);
    }

    public void UpdateSaveButtonAvailability(SourceEditorPaneViewModel vm)
    {
        if (saveSelectedSourceButton is null)
            return;

        var canSaveByCommand = vm.SaveSelectedSourceCommand.CanExecute(null);
        if (!canSaveByCommand || hostsEntriesEditor is null)
        {
            saveSelectedSourceButton.IsEnabled = false;
            return;
        }

        if (vm.IsSelectedEntriesReadOnly)
        {
            saveSelectedSourceButton.IsEnabled = true;
            return;
        }

        var hasIssues = HostsSyntaxColorizer.HasIssues(hostsEntriesEditor.Text);
        saveSelectedSourceButton.IsEnabled = !hasIssues;
    }

    public void SetSaveButtonEnabled(bool isEnabled)
    {
        if (saveSelectedSourceButton is not null)
            saveSelectedSourceButton.IsEnabled = isEnabled;
    }

    private void HostsEntriesEditorTextChanged(object? sender, EventArgs e)
    {
        if (isSyncingEditorText)
            return;

        hostsEntriesEditor?.TextArea?.TextView?.InvalidateVisual();
    }
}
