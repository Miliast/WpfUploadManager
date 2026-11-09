using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Microsoft.Win32;

namespace Conduit;

/// <summary>
/// Screens 1 + 2. Code-behind only for what XAML can't do: folder/file dialogs, drag &amp; drop
/// (from the sidebar tree and from Explorer) and the auto-fill glow.
/// </summary>
public partial class SetupView : UserControl
{
    /// <summary>Drag format of our own tree rows (relative path) – real files additionally travel as FileDrop.</summary>
    private const string RelFormat = "Conduit.RelativePath";

    private Point? _pressedAt;

    public SetupView() => InitializeComponent();

    private Workspace Ws => (Workspace)DataContext;

    // ── Dialogs ────────────────────────────────────────────────────────────

    public void PickProjectFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Open project folder" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) Ws.OpenProject(dialog.FolderName);
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e) => PickProjectFolder();

    private void OnSample(object sender, RoutedEventArgs e) => Ws.LoadSample(single: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));

    private void OnFetchX(object sender, RoutedEventArgs e) => _ = Ws.FetchXAsync();

    private void OnBrowseField(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Slot slot) return;
        var owner = Window.GetWindow(this);
        string? path;
        if (slot.IsFolder)
        {
            var dialog = new OpenFolderDialog { Title = "Choose the test folder", InitialDirectory = Ws.ProjectRoot ?? "" };
            path = dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
        }
        else
        {
            string filter = slot.Index switch
            {
                1 => "package.json|package.json",
                2 => "Source files|*.ts;*.tsx;*.js",
                3 => "Dockerfile|Dockerfile*",
                _ => "Pipeline config|*.yml;*.yaml",
            };
            var dialog = new OpenFileDialog { Title = $"Choose {slot.Title.ToLowerInvariant()}", Filter = filter, InitialDirectory = Ws.ProjectRoot ?? "" };
            path = dialog.ShowDialog(owner) == true ? dialog.FileName : null;
        }
        if (path is not null) Ws.Assign(slot.Index, Ws.External(path));
    }

    // ── Sidebar tree: click toggles a folder, dragging starts drag & drop ──

    private void OnTreeRowDown(object sender, MouseButtonEventArgs e) => _pressedAt = e.GetPosition((IInputElement)sender);

    private void OnTreeRowUp(object sender, MouseButtonEventArgs e)
    {
        if (_pressedAt is null) return;
        _pressedAt = null;
        if (((FrameworkElement)sender).DataContext is TreeRow { IsDir: true } row) Ws.ToggleFolder(row);
    }

    private void OnTreeRowMove(object sender, MouseEventArgs e)
    {
        var element = (FrameworkElement)sender;
        if (_pressedAt is not { } start || e.LeftButton != MouseButtonState.Pressed || element.DataContext is not TreeRow row) return;
        var delta = e.GetPosition(element) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _pressedAt = null;
        var data = new DataObject();
        data.SetData(RelFormat, row.Rel);
        if (Ws.FindItem(row.Rel)?.FullPath is { } full) data.SetData(DataFormats.FileDrop, new[] { full }); // also droppable into Explorer
        DragDrop.DoDragDrop(element, data, DragDropEffects.Copy);
    }

    // ── Drop targets ───────────────────────────────────────────────────────

    /// <summary>A single field: lights up ("Drop to replace ci.yml") when the dragged item belongs into it.</summary>
    private void OnFieldDragOver(object sender, DragEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not Slot slot) return;
        bool fits = Dropped(e.Data).Any(d => Workspace.SlotFor(d.Name, d.IsDir) == slot.Index);
        slot.IsDragOver = fits;
        e.Effects = fits ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFieldDragLeave(object sender, DragEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is Slot slot) slot.IsDragOver = false;
    }

    private void OnFieldDrop(object sender, DragEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not Slot slot) return;
        slot.IsDragOver = false;
        var item = Dropped(e.Data).FirstOrDefault(d => Workspace.SlotFor(d.Name, d.IsDir) == slot.Index);
        if (item is not null) Ws.Assign(slot.Index, item);
        e.Handled = true;
    }

    /// <summary>Anywhere else on the page: every dropped file goes into its matching field.</summary>
    private void OnPageDragOver(object sender, DragEventArgs e)
    {
        e.Effects = Dropped(e.Data).Any(d => Workspace.SlotFor(d.Name, d.IsDir) >= 0) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnPageDrop(object sender, DragEventArgs e)
    {
        foreach (var item in Dropped(e.Data)) Ws.Assign(Workspace.SlotFor(item.Name, item.IsDir), item);
    }

    /// <summary>A folder dropped on the sidebar opens as the project.</summary>
    private void OnSidebarDragOver(object sender, DragEventArgs e)
    {
        e.Effects = Dropped(e.Data).Any(d => d.IsDir && d.FullPath is not null && d.Rel is null) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnSidebarDrop(object sender, DragEventArgs e)
    {
        var folder = Dropped(e.Data).FirstOrDefault(d => d.IsDir && d.FullPath is not null && d.Rel is null);
        if (folder?.FullPath is { } path) Ws.OpenProject(path);
    }

    /// <summary>Everything in a drag: our own tree items and/or real files from Explorer.</summary>
    private List<FileItem> Dropped(IDataObject data)
    {
        if (data.GetData(RelFormat) is string rel) return Ws.FindItem(rel) is { } item ? [item] : [];
        if (data.GetData(DataFormats.FileDrop) is string[] paths) return [.. paths.Select(Ws.External)];
        return [];
    }

    // ── Auto-fill: the glow flows out of the button, then the files drop into their fields one after another ──

    private async void OnAutoFill(object sender, RoutedEventArgs e)
    {
        var matches = Ws.Matches();
        if (Ws.IsFilling || matches.Count == 0) return;
        Ws.IsFilling = true;
        try
        {
            Glow.Play(AutoFillButton.TranslatePoint(new Point(AutoFillButton.ActualWidth / 2, AutoFillButton.ActualHeight / 2), Glow));
            await Task.Delay(260); // let the light reach the fields first
            foreach (var (slot, item) in matches)
            {
                if (!Ws.IsSetup) break;
                Ws.Assign(slot, item);
                if (FieldRow(slot) is { IsVisible: true } row)
                    Glow.Land(row.TransformToVisual(Glow).TransformBounds(new Rect(row.RenderSize)), row.CornerRadius.TopLeft);
                await Task.Delay(140);
            }
        }
        finally
        {
            Ws.IsFilling = false;
        }
    }

    /// <summary>The "Row" border inside the template of field <paramref name="index"/>.</summary>
    private Border? FieldRow(int index) =>
        Fields.ItemContainerGenerator.ContainerFromIndex(index) is ContentPresenter presenter
            ? presenter.ContentTemplate?.FindName("Row", presenter) as Border
            : null;
}
