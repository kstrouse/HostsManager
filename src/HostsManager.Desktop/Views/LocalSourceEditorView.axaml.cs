using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HostsManager.Desktop.ViewModels;

namespace HostsManager.Desktop.Views;

public partial class LocalSourceEditorView : UserControl
{
    public LocalSourceEditorView()
    {
        InitializeComponent();
    }

    private async void RenameLocalFileClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LocalSourceEditorViewModel vm || string.IsNullOrWhiteSpace(vm.SelectedLocalFilePath))
            return;

        var currentFileName = Path.GetFileName(vm.SelectedLocalFilePath);
        var requestedName = await ShowRenameLocalFileDialogAsync(currentFileName);
        if (requestedName is null)
            return;

        await vm.RenameSelectedLocalFileAsync(requestedName);
    }

    private async Task<string?> ShowRenameLocalFileDialogAsync(string currentFileName)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
            return null;

        var fileNameTextBox = new TextBox
        {
            MinWidth = 320,
            Text = currentFileName
        };

        string? result = null;

        var renameButton = new Button
        {
            Content = "Rename",
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
            Title = "Rename Local File",
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
                            Text = "Enter a new file name:"
                        },
                        fileNameTextBox,
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            Spacing = 8,
                            Children =
                            {
                                cancelButton,
                                renameButton
                            }
                        }
                    }
                }
            }
        };

        cancelButton.Click += (_, _) => dialog.Close();
        renameButton.Click += (_, _) =>
        {
            result = fileNameTextBox.Text;
            dialog.Close();
        };

        await dialog.ShowDialog(owner);
        return result;
    }
}
