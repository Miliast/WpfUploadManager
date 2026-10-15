using UploadManager.Core.Tests.Fakes;
using UploadManager.Core.ViewModels;

namespace UploadManager.Core.Tests;

public sealed class FileExplorerViewModelTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeFileDialogService _dialogs = new();
    private readonly FakeNotificationService _notifications = new();

    public void Dispose() => _temp.Dispose();

    private FileExplorerViewModel CreateExplorer(bool showOnlySupported = true) =>
        new(_dialogs, _notifications, path => path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase), showOnlySupported);

    [Fact]
    public void Open_sets_root_path_and_name()
    {
        var explorer = CreateExplorer();
        Assert.False(explorer.HasRoot);
        Assert.False(explorer.RefreshCommand.CanExecute(null));

        Assert.True(explorer.Open(_temp.Path));

        Assert.Equal(Path.GetFullPath(_temp.Path), explorer.RootPath);
        Assert.Equal(Path.GetFileName(_temp.Path), explorer.RootName);
        Assert.True(explorer.HasRoot);
        Assert.True(explorer.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public void Missing_folder_shows_a_warning()
    {
        var explorer = CreateExplorer();

        Assert.False(explorer.Open(Path.Combine(_temp.Path, "does-not-exist")));

        Assert.False(explorer.HasRoot);
        Assert.Contains(_notifications.Messages, m => m.Level == "warning");
    }

    [Fact]
    public void OpenFolder_uses_the_dialog()
    {
        _dialogs.NextFolder = _temp.Path;
        var explorer = CreateExplorer();

        explorer.OpenFolderCommand.Execute(null);

        Assert.Equal(Path.GetFullPath(_temp.Path), explorer.RootPath);
    }

    [Fact]
    public void Opening_the_same_folder_again_requests_a_refresh()
    {
        var explorer = CreateExplorer();
        var refreshes = 0;
        explorer.RefreshRequested += (_, _) => refreshes++;

        explorer.Open(_temp.Path);
        explorer.Open(_temp.Path);
        explorer.RefreshCommand.Execute(null);

        Assert.Equal(2, refreshes);
    }

    [Fact]
    public void FileFilter_follows_the_toggle()
    {
        var explorer = CreateExplorer(showOnlySupported: true);
        var changed = new List<string?>();
        explorer.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.NotNull(explorer.FileFilter);
        Assert.True(explorer.FileFilter!("a.pdf"));
        Assert.False(explorer.FileFilter!("a.txt"));

        explorer.ShowOnlySupportedFiles = false;

        Assert.Null(explorer.FileFilter);
        Assert.Contains(nameof(FileExplorerViewModel.FileFilter), changed);
    }

    [Fact]
    public void SelectedFilePath_is_null_for_folders()
    {
        var file = _temp.CreateFile("doc.pdf");
        var folder = _temp.CreateDirectory("folder");
        var explorer = CreateExplorer();

        explorer.SelectedPath = folder;
        Assert.Null(explorer.SelectedFilePath);

        explorer.SelectedPath = file;
        Assert.Equal(file, explorer.SelectedFilePath);
    }

    [Fact]
    public void ActivateFile_raises_FileActivated_only_for_existing_files()
    {
        var file = _temp.CreateFile("doc.pdf");
        var explorer = CreateExplorer();
        var activated = new List<string>();
        explorer.FileActivated += (_, path) => activated.Add(path);

        explorer.ActivateFileCommand.Execute(file);
        explorer.ActivateFileCommand.Execute(Path.Combine(_temp.Path, "missing.pdf"));
        explorer.ActivateFileCommand.Execute(_temp.Path);

        Assert.Equal([file], activated);
    }
}
