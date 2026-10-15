using UploadManager.Core.Tests.Fakes;
using UploadManager.Core.ViewModels;

namespace UploadManager.Core.Tests;

public sealed class FileExplorerViewModelTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeFileDialogService _dialogs = new();
    private readonly FakeNotificationService _notifications = new();

    public void Dispose() => _temp.Dispose();

    private FileExplorerViewModel CreateExplorer(bool showOnlySupported = false) =>
        new(_dialogs, _notifications, path => path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase), showOnlySupported);

    [Fact]
    public async Task LoadAsync_lists_folders_first_then_files()
    {
        _temp.CreateFile("b.pdf");
        _temp.CreateFile("a.txt");
        _temp.CreateDirectory("zeta");
        _temp.CreateDirectory("alpha");
        var explorer = CreateExplorer();

        Assert.True(await explorer.LoadAsync(_temp.Path));

        Assert.Equal(["alpha", "zeta", "a.txt", "b.pdf"], explorer.RootNodes.Select(c => c.Name));
        Assert.Equal(Path.GetFileName(_temp.Path), explorer.RootName);
        Assert.False(explorer.IsRootEmpty);
        Assert.True(explorer.HasRoot);
        Assert.True(explorer.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task Unsupported_files_can_be_hidden()
    {
        _temp.CreateFile("keep.pdf");
        _temp.CreateFile("hide.txt");
        var explorer = CreateExplorer(showOnlySupported: true);

        await explorer.LoadAsync(_temp.Path);

        Assert.Equal(["keep.pdf"], explorer.RootNodes.Select(c => c.Name));
    }

    [Fact]
    public async Task Folders_load_their_children_lazily()
    {
        _temp.CreateFile(Path.Combine("sub", "inner.pdf"));
        var explorer = CreateExplorer();
        await explorer.LoadAsync(_temp.Path);

        var folder = Assert.Single(explorer.RootNodes);
        Assert.True(Assert.Single(folder.Children).IsPlaceholder);

        await folder.LoadChildrenAsync();

        Assert.Equal("inner.pdf", Assert.Single(folder.Children).Name);
        Assert.True(folder.Children[0].IsFile);
    }

    [Fact]
    public async Task Missing_folder_shows_a_warning()
    {
        var explorer = CreateExplorer();

        Assert.False(await explorer.LoadAsync(Path.Combine(_temp.Path, "does-not-exist")));

        Assert.False(explorer.HasRoot);
        Assert.Contains(_notifications.Messages, m => m.Level == "warning");
    }

    [Fact]
    public async Task OpenFolder_uses_the_dialog()
    {
        _dialogs.NextFolder = _temp.Path;
        var explorer = CreateExplorer();

        await explorer.OpenFolderCommand.ExecuteAsync(null);

        Assert.Equal(Path.GetFullPath(_temp.Path), explorer.RootPath);
    }

    [Fact]
    public async Task Activating_a_file_raises_FileActivated()
    {
        _temp.CreateFile("doc.pdf");
        _temp.CreateDirectory("folder");
        var explorer = CreateExplorer();
        await explorer.LoadAsync(_temp.Path);
        var activated = new List<string>();
        explorer.FileActivated += (_, node) => activated.Add(node.Name);

        foreach (var node in explorer.RootNodes)
        {
            explorer.ActivateNodeCommand.Execute(node);
        }

        Assert.Equal(["doc.pdf"], activated);
    }

    [Fact]
    public async Task Refresh_keeps_expanded_folders_and_picks_up_new_files()
    {
        _temp.CreateFile(Path.Combine("sub", "one.pdf"));
        var explorer = CreateExplorer();
        await explorer.LoadAsync(_temp.Path);
        var folder = explorer.RootNodes[0];
        await folder.LoadChildrenAsync();
        folder.IsExpanded = true;

        _temp.CreateFile(Path.Combine("sub", "two.pdf"));
        await explorer.RefreshCommand.ExecuteAsync(null);

        var refreshed = explorer.RootNodes[0];
        Assert.True(refreshed.IsExpanded);
        Assert.Equal(["one.pdf", "two.pdf"], refreshed.Children.Select(c => c.Name));
    }

    [Fact]
    public async Task SelectedFile_is_null_for_folders()
    {
        _temp.CreateFile("doc.pdf");
        _temp.CreateDirectory("folder");
        var explorer = CreateExplorer();
        await explorer.LoadAsync(_temp.Path);

        explorer.SelectedNode = explorer.RootNodes[0];
        Assert.Null(explorer.SelectedFile);

        explorer.SelectedNode = explorer.RootNodes[1];
        Assert.NotNull(explorer.SelectedFile);
    }

    [Fact]
    public async Task Folders_show_their_item_count_and_files_their_size()
    {
        _temp.CreateFile(Path.Combine("docs", "a.pdf"));
        _temp.CreateFile(Path.Combine("docs", "b.txt"));
        _temp.CreateDirectory(Path.Combine("docs", "nested"));
        _temp.CreateFile("file.pdf", 2048);
        var explorer = CreateExplorer();

        await explorer.LoadAsync(_temp.Path);

        var folder = explorer.RootNodes[0];
        Assert.Equal(3, folder.ChildCount);
        Assert.Equal("3", folder.DetailText);
        Assert.Equal(".pdf", explorer.RootNodes[1].Extension);
        Assert.Equal(explorer.RootNodes[1].SizeText, explorer.RootNodes[1].DetailText);
    }

    [Fact]
    public async Task Item_count_respects_the_file_filter()
    {
        _temp.CreateFile(Path.Combine("docs", "a.pdf"));
        _temp.CreateFile(Path.Combine("docs", "b.txt"));
        var explorer = CreateExplorer(showOnlySupported: true);

        await explorer.LoadAsync(_temp.Path);

        Assert.Equal(1, explorer.RootNodes[0].ChildCount);
    }

    [Fact]
    public async Task Empty_folders_have_no_expander_placeholder()
    {
        _temp.CreateDirectory("empty");
        var explorer = CreateExplorer();

        await explorer.LoadAsync(_temp.Path);

        Assert.Equal(0, explorer.RootNodes[0].ChildCount);
        Assert.Empty(explorer.RootNodes[0].Children);
    }

    [Fact]
    public async Task Depth_increases_with_nesting()
    {
        _temp.CreateFile(Path.Combine("a", "b", "c.pdf"));
        var explorer = CreateExplorer();
        await explorer.LoadAsync(_temp.Path);

        var a = explorer.RootNodes[0];
        await a.LoadChildrenAsync();
        var b = a.Children[0];
        await b.LoadChildrenAsync();

        Assert.Equal(0, a.Depth);
        Assert.Equal(1, b.Depth);
        Assert.Equal(2, b.Children[0].Depth);
    }

    [Fact]
    public async Task Empty_root_folder_is_reported()
    {
        var explorer = CreateExplorer(showOnlySupported: true);
        _temp.CreateFile("ignored.txt");

        await explorer.LoadAsync(_temp.Path);

        Assert.True(explorer.HasRoot);
        Assert.True(explorer.IsRootEmpty);
    }
}
