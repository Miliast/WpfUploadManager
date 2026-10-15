using System.Windows;
using System.Windows.Input;

namespace UploadManager.App.Behaviors;

/// <summary>
/// Attached behavior that turns any element into a drop target for files
/// (from Windows Explorer or from the in-app file tree).
/// </summary>
/// <example>
/// <code>&lt;Grid behaviors:FileDrop.Command="{Binding DropFilesCommand}" /&gt;</code>
/// The command receives the dropped paths as <c>string[]</c>.
/// </example>
public static class FileDrop
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command",
        typeof(ICommand),
        typeof(FileDrop),
        new PropertyMetadata(null, OnCommandChanged));

    private static readonly DependencyPropertyKey IsDragOverPropertyKey = DependencyProperty.RegisterAttachedReadOnly(
        "IsDragOver",
        typeof(bool),
        typeof(FileDrop),
        new PropertyMetadata(false));

    /// <summary>
    /// <see langword="true"/> while acceptable files are dragged over the element (for visual feedback).
    /// </summary>
    public static readonly DependencyProperty IsDragOverProperty = IsDragOverPropertyKey.DependencyProperty;

    public static ICommand? GetCommand(DependencyObject element) => (ICommand?)element.GetValue(CommandProperty);

    public static void SetCommand(DependencyObject element, ICommand? value) => element.SetValue(CommandProperty, value);

    public static bool GetIsDragOver(DependencyObject element) => (bool)element.GetValue(IsDragOverProperty);

    private static void SetIsDragOver(DependencyObject element, bool value) => element.SetValue(IsDragOverPropertyKey, value);

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        element.DragEnter -= OnDragOver;
        element.DragOver -= OnDragOver;
        element.DragLeave -= OnDragLeave;
        element.Drop -= OnDrop;

        element.AllowDrop = e.NewValue is not null;

        if (e.NewValue is not null)
        {
            element.DragEnter += OnDragOver;
            element.DragOver += OnDragOver;
            element.DragLeave += OnDragLeave;
            element.Drop += OnDrop;
        }
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        var element = (UIElement)sender;
        var canDrop = TryGetPaths(e.Data, out var paths) && GetCommand(element)?.CanExecute(paths) == true;

        e.Effects = canDrop ? DragDropEffects.Copy : DragDropEffects.None;
        SetIsDragOver(element, canDrop);
        e.Handled = true;
    }

    private static void OnDragLeave(object sender, DragEventArgs e)
    {
        SetIsDragOver((UIElement)sender, false);
        e.Handled = true;
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        var element = (UIElement)sender;
        SetIsDragOver(element, false);

        var command = GetCommand(element);
        if (TryGetPaths(e.Data, out var paths) && command?.CanExecute(paths) == true)
        {
            command.Execute(paths);
        }

        e.Handled = true;
    }

    private static bool TryGetPaths(IDataObject data, out string[] paths)
    {
        paths = data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] dropped
            ? dropped
            : [];

        return paths.Length > 0;
    }
}
