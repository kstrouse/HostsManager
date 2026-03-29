using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HostsManager.Desktop.Models;
using HostsManager.Desktop.ViewModels;

namespace HostsManager.Desktop.Views;

public partial class SourceListView : UserControl
{
    public SourceListView()
    {
        InitializeComponent();
    }

    private void OpenAddSourceMenuClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is null)
            return;

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Open();
    }

    private async void NewLocalSourceClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SourceListViewModel vm)
            return;

        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null)
            return;

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Create local hosts source",
            SuggestedFileName = "hosts-local.hosts",
            FileTypeChoices =
            [
                new FilePickerFileType("Hosts files")
                {
                    Patterns = ["*.hosts", "*.txt"]
                }
            ]
        });

        if (file is null)
            return;

        await vm.AddNewLocalSourceAsync(file.Path.LocalPath);
    }

    private async void AddLocalSourceClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SourceListViewModel vm)
            return;

        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null)
            return;

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add existing local hosts source",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Hosts files")
                {
                    Patterns = ["*.hosts", "*.txt"]
                }
            ]
        });

        if (files.Count == 0)
            return;

        await vm.AddExistingLocalSourceAsync(files[0].Path.LocalPath);
    }

    private async void NewRemoteSourceClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SourceListViewModel vm)
            return;

        var selectedProtocol = await ShowNewRemoteProtocolDialogAsync();
        if (!selectedProtocol.HasValue)
            return;

        vm.AddRemoteSource(selectedProtocol.Value);
    }

    private async void SourceEnabledChanged(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SourceListViewModel vm && sender is Control control)
            await vm.HandleSourceToggledAsync(control.DataContext as HostProfile);
    }

    private async Task<RemoteTransport?> ShowNewRemoteProtocolDialogAsync()
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
            return null;

        var protocolChoices = new[]
        {
            "HTTP/HTTPS URL",
            "Azure Private DNS"
        };

        var protocolComboBox = new ComboBox
        {
            ItemsSource = protocolChoices,
            SelectedIndex = 0,
            MinWidth = 260
        };

        RemoteTransport? selectedProtocol = null;

        var createButton = new Button
        {
            Content = "Create",
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
            Title = "New Remote Source",
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
                            Text = "Select protocol for the new remote source:"
                        },
                        protocolComboBox,
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            Spacing = 8,
                            Children =
                            {
                                cancelButton,
                                createButton
                            }
                        }
                    }
                }
            }
        };

        cancelButton.Click += (_, _) => dialog.Close();
        createButton.Click += (_, _) =>
        {
            selectedProtocol = protocolComboBox.SelectedIndex == 1
                ? RemoteTransport.AzurePrivateDns
                : RemoteTransport.Https;
            dialog.Close();
        };

        await dialog.ShowDialog(owner);
        return selectedProtocol;
    }
}
