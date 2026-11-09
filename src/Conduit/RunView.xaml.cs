using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Conduit;

/// <summary>
/// Screens 3–6. Code-behind only forwards clicks and keeps the scroll positions sensible:
/// the log follows new lines while you're at its end, the test list jumps to the top when you switch node or filter.
/// </summary>
public partial class RunView : UserControl
{
    private Workspace? _ws;

    public RunView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        LogScroll.IsVisibleChanged += (_, _) => ScrollLogToEnd();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_ws is not null)
        {
            _ws.VisibleLog.CollectionChanged -= OnLogChanged;
            _ws.PropertyChanged -= OnWorkspaceChanged;
        }
        _ws = e.NewValue as Workspace;
        if (_ws is not null)
        {
            _ws.VisibleLog.CollectionChanged += OnLogChanged;
            _ws.PropertyChanged += OnWorkspaceChanged;
        }
    }

    private void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Workspace.SelectedNode) or nameof(Workspace.StateFilter)) TestScroll.ScrollToTop();
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Follow the log only while the user hasn't scrolled up to read something.
        bool atEnd = LogScroll.VerticalOffset >= LogScroll.ScrollableHeight - 24;
        if (atEnd || e.Action == NotifyCollectionChangedAction.Reset) ScrollLogToEnd();
    }

    private void ScrollLogToEnd() => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(LogScroll.ScrollToEnd));

    private void OnStopSelected(object sender, RoutedEventArgs e) => _ws?.StopSelected();

    private void OnShowMore(object sender, RoutedEventArgs e)
    {
        if (_ws is not null) _ws.ShowAll = !_ws.ShowAll;
    }

    private void OnCopyLog(object sender, RoutedEventArgs e)
    {
        if (_ws is not null) App.CopyText(_ws.LogText());
    }

    private void OnCloseLogs(object sender, RoutedEventArgs e) => _ws?.ToggleLogs();
}
