using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UploadManager.Core.ViewModels;

namespace UploadManager.App.Views;

/// <summary>
/// View-only interaction logic for the file tree (selection, activation, drag source).
/// </summary>
public partial class FileExplorerView : UserControl
{
    private Point _dragStartPoint;
    private FileSystemNodeViewModel? _dragCandidate;

    public FileExplorerView()
    {
        InitializeComponent();
    }

    private FileExplorerViewModel? ViewModel => DataContext as FileExplorerViewModel;

    private void OnTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.SelectedNode = e.NewValue as FileSystemNodeViewModel;
        }
    }

    private void OnTreeMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindNode(e.OriginalSource) is { IsFile: true } node && ViewModel is { } viewModel)
        {
            viewModel.ActivateNodeCommand.Execute(node);
            e.Handled = true;
        }
    }

    private void OnTreeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ViewModel is { SelectedFile: { } file } viewModel)
        {
            viewModel.ActivateNodeCommand.Execute(file);
            e.Handled = true;
        }
    }

    private void OnTreePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(FileTree);
        _dragCandidate = FindNode(e.OriginalSource) is { IsFile: true } node ? node : null;
    }

    private void OnTreePreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidate is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(FileTree);
        if (Math.Abs(position.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var node = _dragCandidate;
        _dragCandidate = null;

        // FileDrop format: the upload fields accept it exactly like files dragged from Windows Explorer.
        var data = new DataObject(DataFormats.FileDrop, new[] { node.FullPath });
        DragDrop.DoDragDrop(FileTree, data, DragDropEffects.Copy);
    }

    private static FileSystemNodeViewModel? FindNode(object? source)
    {
        var current = source as DependencyObject;
        while (current is not null and not TreeViewItem)
        {
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return (current as TreeViewItem)?.DataContext as FileSystemNodeViewModel;
    }
}
