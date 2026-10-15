using UploadManager.Core.Tests.Fakes;
using UploadManager.Core.ViewModels;

namespace UploadManager.Core.Tests;

public sealed class UploadSlotViewModelTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeFileDialogService _dialogs = new();

    public void Dispose() => _temp.Dispose();

    private UploadSlotViewModel CreateSlot(bool required = true) => new(TestData.Pdf(required), 1, _dialogs);

    [Fact]
    public void New_slot_is_empty()
    {
        var slot = CreateSlot();

        Assert.Equal(UploadSlotState.Empty, slot.State);
        Assert.False(slot.HasFile);
        Assert.False(slot.IsSatisfied);
        Assert.Equal("Pflicht", slot.RequirementText);
        Assert.False(slot.ClearCommand.CanExecute(null));
    }

    [Fact]
    public void Optional_empty_slot_is_satisfied() => Assert.True(CreateSlot(required: false).IsSatisfied);

    [Fact]
    public void Assigning_a_valid_file_makes_the_slot_ready()
    {
        var slot = CreateSlot();
        var path = _temp.CreateFile("contract.pdf", 42);

        Assert.True(slot.TryAssign(path));

        Assert.Equal(UploadSlotState.Ready, slot.State);
        Assert.Equal("contract.pdf", slot.FileName);
        Assert.Equal(42, slot.FileSizeBytes);
        Assert.True(slot.IsSatisfied);
        Assert.False(slot.HasError);
        Assert.True(slot.ClearCommand.CanExecute(null));
    }

    [Fact]
    public void Assigning_an_invalid_file_keeps_it_with_an_error()
    {
        var slot = CreateSlot();
        var path = _temp.CreateFile("photo.png");

        Assert.False(slot.TryAssign(path));

        Assert.Equal(UploadSlotState.Invalid, slot.State);
        Assert.Equal(path, slot.FilePath);
        Assert.True(slot.HasError);
        Assert.False(slot.IsSatisfied);
    }

    [Fact]
    public void Clear_resets_the_slot()
    {
        var slot = CreateSlot();
        slot.TryAssign(_temp.CreateFile("a.pdf"));

        slot.ClearCommand.Execute(null);

        Assert.Equal(UploadSlotState.Empty, slot.State);
        Assert.Null(slot.FilePath);
        Assert.Null(slot.ErrorMessage);
        Assert.Equal(0, slot.FileSizeBytes);
    }

    [Fact]
    public void Browse_assigns_the_file_picked_in_the_dialog()
    {
        var slot = CreateSlot();
        _dialogs.NextFile = _temp.CreateFile("picked.pdf");

        slot.BrowseCommand.Execute(null);

        Assert.Equal(UploadSlotState.Ready, slot.State);
        Assert.Contains("*.pdf", _dialogs.LastFilter);
    }

    [Fact]
    public void Cancelled_browse_dialog_changes_nothing()
    {
        var slot = CreateSlot();
        _dialogs.NextFile = null;

        slot.BrowseCommand.Execute(null);

        Assert.Equal(UploadSlotState.Empty, slot.State);
    }

    [Fact]
    public void Drop_assigns_the_first_file()
    {
        var slot = CreateSlot();
        var first = _temp.CreateFile("first.pdf");
        var second = _temp.CreateFile("second.pdf");
        string[] dropped = [first, second];

        Assert.True(slot.DropFilesCommand.CanExecute(dropped));
        slot.DropFilesCommand.Execute(dropped);

        Assert.Equal(first, slot.FilePath);
    }

    [Fact]
    public void Locked_slot_rejects_changes()
    {
        var slot = CreateSlot();
        slot.IsLocked = true;

        Assert.False(slot.TryAssign(_temp.CreateFile("a.pdf")));
        Assert.False(slot.BrowseCommand.CanExecute(null));
        Assert.False(slot.DropFilesCommand.CanExecute(new[] { "x.pdf" }));
        Assert.Equal(UploadSlotState.Empty, slot.State);
    }

    [Fact]
    public void Revalidate_detects_deleted_files()
    {
        var slot = CreateSlot();
        var path = _temp.CreateFile("a.pdf");
        slot.TryAssign(path);

        File.Delete(path);

        Assert.False(slot.Revalidate());
        Assert.Equal(UploadSlotState.Invalid, slot.State);
    }

    [Fact]
    public void Upload_lifecycle_updates_state_and_progress()
    {
        var slot = CreateSlot();
        slot.TryAssign(_temp.CreateFile("a.pdf"));

        slot.BeginUpload();
        slot.ReportProgress(0.5);
        Assert.Equal(UploadSlotState.Uploading, slot.State);
        Assert.Equal(0.5, slot.Progress);

        slot.CompleteUpload();
        Assert.Equal(UploadSlotState.Completed, slot.State);
        Assert.Equal(1, slot.Progress);

        // Late progress reports must not change a completed slot.
        slot.ReportProgress(0.2);
        Assert.Equal(1, slot.Progress);
    }
}
