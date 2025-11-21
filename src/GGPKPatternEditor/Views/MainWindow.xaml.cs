using System.Windows;
using System.Windows.Controls;
using GGPKPatternEditor.Models;
using GGPKPatternEditor.ViewModels;

namespace GGPKPatternEditor.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void FileListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.ViewFileContentCommand.CanExecute(null))
        {
            vm.ViewFileContentCommand.Execute(null);
        }
    }

    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel vm && e.NewValue is TreeNode node)
        {
            if (!node.IsDirectory && node.Record != null)
            {
                vm.SelectedFile = node.Record;
                if (vm.ViewFileContentCommand.CanExecute(null))
                {
                    vm.ViewFileContentCommand.Execute(null);
                }
            }
        }
    }
}
