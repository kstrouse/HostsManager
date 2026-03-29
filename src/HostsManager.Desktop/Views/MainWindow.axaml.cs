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

public partial class MainWindow : Window
{
    public bool CloseToTrayEnabled { get; set; }
    private MainWindowViewModel? currentViewModel;
    private bool isInitialized;
    private bool startHiddenToTray;
    private TextEditor? hostsEntriesEditor;
    private Button? saveSelectedSourceButton;
    private readonly DispatcherTimer editorSyncTimer;
    private bool isSyncingEditorText;
    private TextBlock? unsavedIndicator;
    private string? editorSourceProfileId;
    private bool isExternalChangeDialogOpen;

    public MainWindow()
    {
        InitializeComponent();

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);

        editorSyncTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        editorSyncTimer.Tick += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                SyncEditorFromSelectedProfile(vm);
            }
        };
        editorSyncTimer.Start();

        hostsEntriesEditor = this.FindControl<TextEditor>("HostsEntriesEditor");
        saveSelectedSourceButton = this.FindControl<Button>("SaveSelectedSourceButton");
        unsavedIndicator = this.FindControl<TextBlock>("UnsavedIndicator");
        if (hostsEntriesEditor is not null)
        {
            hostsEntriesEditor.TextChanged += HostsEntriesEditorTextChanged;

            if (hostsEntriesEditor.TextArea is not null)
            {
                foreach (var leftMargin in hostsEntriesEditor.TextArea.LeftMargins)
                {
                    if (leftMargin.GetType().Name.Contains("LineNumberMargin", StringComparison.Ordinal))
                    {
                        leftMargin.Margin = new Thickness(0, 0, 10, 0);
                    }
                }
            }

            if (hostsEntriesEditor.TextArea?.TextView is not null)
            {
                hostsEntriesEditor.TextArea.TextView.LineTransformers.Add(new HostsSyntaxColorizer());
            }
        }

        DataContextChanged += (_, _) => BindCloseBehaviorToViewModel();
        BindCloseBehaviorToViewModel();

        Closing += (_, e) =>
        {
            if (!CloseToTrayEnabled)
            {
                return;
            }

            e.Cancel = true;
            Hide();
        };

        Opened += async (_, _) =>
        {
            if (!isInitialized && DataContext is MainWindowViewModel vm)
            {
                await vm.InitializeAsync();
                SyncEditorFromSelectedProfile(vm);
                isInitialized = true;
            }

            if (startHiddenToTray)
            {
                startHiddenToTray = false;
                Hide();
                ShowInTaskbar = true;
                ShowActivated = true;
                Opacity = 1;
                WindowState = WindowState.Normal;
            }
        };
    }

    public void ConfigureStartupHiddenToTray()
    {
        startHiddenToTray = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Opacity = 0;
        WindowState = WindowState.Minimized;
    }

    private void HostsEntriesEditorTextChanged(object? sender, EventArgs e)
    {
        if (isSyncingEditorText)
        {
            return;
        }

        if (DataContext is MainWindowViewModel vm)
        {
            UpdateUnsavedIndicatorVisibility(vm);
            UpdateSaveButtonAvailability(vm);
        }

        hostsEntriesEditor?.TextArea?.TextView?.InvalidateVisual();
    }

    private async void SaveSelectedSourceClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        await SaveSelectedSourceAsync(vm, requireUnsavedEditorDraft: false);
    }

    private async Task SaveSelectedSourceAsync(MainWindowViewModel vm, bool requireUnsavedEditorDraft)
    {
        if (requireUnsavedEditorDraft && !HasPendingEditorDraft(vm))
        {
            return;
        }

        CommitEditorToSelectedProfile(vm);

        if (!vm.SaveSelectedSourceCommand.CanExecute(null))
        {
            return;
        }

        if (!vm.IsSelectedEntriesReadOnly && HostsSyntaxColorizer.HasIssues(hostsEntriesEditor?.Text))
        {
            vm.StatusMessage = "Fix hosts entry issues before saving.";
            UpdateSaveButtonAvailability(vm);
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
        UpdateUnsavedIndicatorVisibility(vm);
        UpdateSaveButtonAvailability(vm);
    }

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.S)
        {
            return;
        }

        var modifiers = e.KeyModifiers;
        var isSaveShortcut = modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);
        if (!isSaveShortcut)
        {
            return;
        }

        if (DataContext is not MainWindowViewModel vm || hostsEntriesEditor is null)
        {
            return;
        }

        if (!hostsEntriesEditor.IsKeyboardFocusWithin)
        {
            return;
        }

        e.Handled = true;
        await SaveSelectedSourceAsync(vm, requireUnsavedEditorDraft: true);
    }

    public void ShowFromTray()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        Activate();
    }

    private void BindCloseBehaviorToViewModel()
    {
        if (currentViewModel is not null)
        {
            currentViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        currentViewModel = DataContext as MainWindowViewModel;

        if (currentViewModel is not null)
        {
            currentViewModel.PropertyChanged += OnViewModelPropertyChanged;
            CloseToTrayEnabled = currentViewModel.MinimizeToTrayOnClose;
            SyncEditorFromSelectedProfile(currentViewModel);
            UpdateSaveButtonAvailability(currentViewModel);
        }
        else
        {
            CloseToTrayEnabled = false;
            if (saveSelectedSourceButton is not null)
            {
                saveSelectedSourceButton.IsEnabled = false;
            }
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.MinimizeToTrayOnClose) && sender is MainWindowViewModel vm)
        {
            CloseToTrayEnabled = vm.MinimizeToTrayOnClose;
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.SelectedProfile) && sender is MainWindowViewModel vm2)
        {
            SyncEditorFromSelectedProfile(vm2, force: true);
            UpdateUnsavedIndicatorVisibility(vm2);
            UpdateSaveButtonAvailability(vm2);
            return;
        }

        if ((e.PropertyName == nameof(MainWindowViewModel.IsSystemHostsEditingEnabled) ||
             e.PropertyName == nameof(MainWindowViewModel.IsSelectedEntriesReadOnly)) &&
            sender is MainWindowViewModel vm3)
        {
            UpdateUnsavedIndicatorVisibility(vm3);
            UpdateSaveButtonAvailability(vm3);
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.SelectedSourceChangedExternally) &&
            sender is MainWindowViewModel vm4 &&
            vm4.SelectedSourceChangedExternally)
        {
            _ = HandleSelectedSourceExternalChangeAsync(vm4);
        }
    }

    private void SyncEditorFromSelectedProfile(MainWindowViewModel vm, bool force = false)
    {
        if (hostsEntriesEditor is null)
        {
            return;
        }

        var nextText = vm.SelectedProfile?.Entries ?? string.Empty;
        var nextProfileId = vm.SelectedProfile?.Id;

        if (isSyncingEditorText)
        {
            return;
        }

        var isSameSelectedProfile = string.Equals(editorSourceProfileId, nextProfileId, StringComparison.Ordinal);
        if (!force && isSameSelectedProfile && HasPendingEditorDraft(vm))
        {
            return;
        }

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

    private void CommitEditorToSelectedProfile(MainWindowViewModel vm)
    {
        if (hostsEntriesEditor is null || vm.SelectedProfile is null || vm.IsSelectedEntriesReadOnly)
        {
            return;
        }

        var editorText = hostsEntriesEditor.Text ?? string.Empty;
        if (string.Equals(editorText, vm.SelectedProfile.Entries, StringComparison.Ordinal))
        {
            return;
        }

        vm.SelectedProfile.Entries = editorText;
        editorSourceProfileId = vm.SelectedProfile.Id;
        UpdateUnsavedIndicatorVisibility(vm);
        UpdateSaveButtonAvailability(vm);
    }

    private bool HasPendingEditorDraft(MainWindowViewModel vm)
    {
        if (hostsEntriesEditor is null || vm.SelectedProfile is null || vm.IsSelectedEntriesReadOnly)
        {
            return false;
        }

        var editorText = hostsEntriesEditor.Text ?? string.Empty;
        var profileText = vm.SelectedProfile.Entries ?? string.Empty;
        return !string.Equals(editorText, profileText, StringComparison.Ordinal);
    }

    private void UpdateUnsavedIndicatorVisibility(MainWindowViewModel vm)
    {
        if (unsavedIndicator is null)
        {
            return;
        }

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

    private void UpdateSaveButtonAvailability(MainWindowViewModel vm)
    {
        if (saveSelectedSourceButton is null)
        {
            return;
        }

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


    private async Task<bool> ShowSystemHostsSaveConfirmationAsync()
    {
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

        await dialog.ShowDialog(this);
        return confirmed;
    }

    private async Task HandleSelectedSourceExternalChangeAsync(MainWindowViewModel vm)
    {
        if (isExternalChangeDialogOpen || !vm.SelectedSourceChangedExternally)
        {
            return;
        }

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
            SyncEditorFromSelectedProfile(vm, force: true);
            UpdateUnsavedIndicatorVisibility(vm);
            UpdateSaveButtonAvailability(vm);
        }
        finally
        {
            isExternalChangeDialogOpen = false;
        }
    }

    private async Task<bool> ShowExternalChangeReloadPromptAsync(string sourceName)
    {
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

        await dialog.ShowDialog(this);
        return shouldReload;
    }

}
