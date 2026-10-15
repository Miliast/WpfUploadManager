using System.Windows;
using System.Windows.Controls;
using UploadManager.Core.ViewModels;

namespace UploadManager.App.Views;

public partial class FileExplorerView : UserControl
{
    public FileExplorerView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is FileExplorerViewModel oldViewModel)
        {
            oldViewModel.RefreshRequested -= OnRefreshRequested;
        }

        if (e.NewValue is FileExplorerViewModel newViewModel)
        {
            newViewModel.RefreshRequested += OnRefreshRequested;
        }
    }

    private void OnRefreshRequested(object? sender, EventArgs e) => FileTree.Refresh();
}
