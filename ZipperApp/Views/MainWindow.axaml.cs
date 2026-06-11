using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using ZipperApp.ViewModels;

namespace ZipperApp.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BindBrowseCommands();
    }

    private void BindBrowseCommands()
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.BrowseInputCommand = new AsyncRelayCommand(async () =>
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select Input Folder",
                    AllowMultiple = false
                });
                if (folders.Any())
                    vm.InputPath = folders[0].Path.LocalPath;
            });

            vm.BrowseOutputCommand = new AsyncRelayCommand(async () =>
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select Output Directory",
                    AllowMultiple = false
                });
                if (folders.Any())
                    vm.OutputDir = folders[0].Path.LocalPath;
            });
        }
    }
}
