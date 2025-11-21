using System.Windows;
using System.Windows.Controls;
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
}
