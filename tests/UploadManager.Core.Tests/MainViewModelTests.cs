using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UploadManager.Core.Services;
using UploadManager.Core.Tests.Fakes;
using UploadManager.Core.ViewModels;

namespace UploadManager.Core.Tests;

public sealed class MainViewModelTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeFileDialogService _dialogs = new();
    private readonly FakeNotificationService _notifications = new();
    private readonly FakeThemeService _theme = new();
    private readonly InMemoryUserSettingsStore _settings = new();
    private readonly FakeUploadService _upload = new();

    public void Dispose() => _temp.Dispose();

    private MainViewModel CreateViewModel() => new(
        Options.Create(TestData.Options()),
        _upload,
        _dialogs,
        _notifications,
        _theme,
        _settings,
        NullLogger<MainViewModel>.Instance);

    private void FillRequiredSlots(MainViewModel viewModel)
    {
        viewModel.Slots[0].TryAssign(_temp.CreateFile("doc.pdf"));
        viewModel.Slots[1].TryAssign(_temp.CreateFile("data.csv"));
        viewModel.Slots[2].TryAssign(_temp.CreateFile("config.json"));
    }

    [Fact]
    public void Creates_one_slot_per_definition()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(4, viewModel.Slots.Count);
        Assert.Equal([1, 2, 3, 4], viewModel.Slots.Select(s => s.Number));
        Assert.Equal(3, viewModel.RequiredSlotCount);
        Assert.Equal("0 von 3 Pflichtfeldern bereit", viewModel.ReadinessText);
    }

    [Fact]
    public void Upload_is_only_possible_when_all_required_slots_are_valid()
    {
        var viewModel = CreateViewModel();
        Assert.False(viewModel.UploadCommand.CanExecute(null));

        viewModel.Slots[0].TryAssign(_temp.CreateFile("doc.pdf"));
        viewModel.Slots[1].TryAssign(_temp.CreateFile("data.csv"));
        Assert.False(viewModel.UploadCommand.CanExecute(null));

        viewModel.Slots[2].TryAssign(_temp.CreateFile("config.json"));
        Assert.True(viewModel.UploadCommand.CanExecute(null));
        Assert.Equal("3 von 3 Pflichtfeldern bereit", viewModel.ReadinessText);
    }

    [Fact]
    public void Invalid_optional_file_blocks_the_upload()
    {
        var viewModel = CreateViewModel();
        FillRequiredSlots(viewModel);

        viewModel.Slots[3].TryAssign(_temp.CreateFile("notes.txt"));

        Assert.False(viewModel.UploadCommand.CanExecute(null));
        Assert.Equal("Bitte ungültige Dateien ersetzen", viewModel.ReadinessText);
    }

    [Fact]
    public async Task Successful_upload_completes_all_ready_slots()
    {
        var viewModel = CreateViewModel();
        FillRequiredSlots(viewModel);

        await viewModel.UploadCommand.ExecuteAsync(null);

        Assert.Equal(["document", "data", "config"], _upload.UploadedFiles!.Select(f => f.SlotKey));
        Assert.All(viewModel.Slots.Take(3), s => Assert.Equal(UploadSlotState.Completed, s.State));
        Assert.Equal(UploadSlotState.Empty, viewModel.Slots[3].State);
        Assert.False(viewModel.IsUploading);
        Assert.All(viewModel.Slots, s => Assert.False(s.IsLocked));
        Assert.Equal(1, viewModel.OverallProgress);
        Assert.Contains(_notifications.Messages, m => m.Level == "success");
        Assert.False(viewModel.UploadCommand.CanExecute(null));
    }

    [Fact]
    public async Task Failed_upload_returns_slots_to_ready()
    {
        _upload.ExceptionToThrow = new HttpRequestException("Server nicht erreichbar");
        var viewModel = CreateViewModel();
        FillRequiredSlots(viewModel);

        await viewModel.UploadCommand.ExecuteAsync(null);

        Assert.All(viewModel.Slots.Take(3), s => Assert.Equal(UploadSlotState.Ready, s.State));
        Assert.Contains(_notifications.Messages, m => m.Level == "error" && m.Message.Contains("Server nicht erreichbar"));
        Assert.True(viewModel.UploadCommand.CanExecute(null));
    }

    [Fact]
    public async Task Upload_can_be_cancelled()
    {
        _upload.Gate = new TaskCompletionSource();
        var viewModel = CreateViewModel();
        FillRequiredSlots(viewModel);

        var running = viewModel.UploadCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsUploading);
        Assert.All(viewModel.Slots, s => Assert.True(s.IsLocked));
        Assert.Equal(UploadSlotState.Uploading, viewModel.Slots[0].State);

        viewModel.UploadCancelCommand.Execute(null);
        await running;

        Assert.False(viewModel.IsUploading);
        Assert.All(viewModel.Slots.Take(3), s => Assert.Equal(UploadSlotState.Ready, s.State));
        Assert.Contains(_notifications.Messages, m => m.Level == "info");
    }

    [Fact]
    public async Task Files_changed_after_assignment_block_the_upload()
    {
        var viewModel = CreateViewModel();
        FillRequiredSlots(viewModel);
        File.Delete(viewModel.Slots[1].FilePath!);

        await viewModel.UploadCommand.ExecuteAsync(null);

        Assert.Null(_upload.UploadedFiles);
        Assert.Equal(UploadSlotState.Invalid, viewModel.Slots[1].State);
        Assert.Contains(_notifications.Messages, m => m.Level == "warning");
    }

    [Fact]
    public async Task Simulated_upload_reports_progress_and_completes()
    {
        var service = new SimulatedUploadService(NullLogger<SimulatedUploadService>.Instance) { StepDelay = TimeSpan.Zero };
        var reports = new List<UploadProgress>();
        var file = _temp.CreateFile("doc.pdf", 1000);

        await service.UploadAsync([new UploadFile("document", file, 1000)], new SyncProgress<UploadProgress>(reports.Add), CancellationToken.None);

        Assert.NotEmpty(reports);
        Assert.Equal(1, reports[^1].Fraction);
    }

    [Fact]
    public void AssignToMatchingSlot_uses_the_first_empty_matching_slot()
    {
        var viewModel = CreateViewModel();

        var slot = viewModel.AssignToMatchingSlot(_temp.CreateFile("table.xlsx"));

        Assert.Same(viewModel.Slots[1], slot);
        Assert.Equal(UploadSlotState.Ready, slot!.State);
    }

    [Fact]
    public void AssignToMatchingSlot_warns_when_no_slot_accepts_the_file()
    {
        var viewModel = CreateViewModel();

        Assert.Null(viewModel.AssignToMatchingSlot(_temp.CreateFile("movie.mp4")));
        Assert.Contains(_notifications.Messages, m => m.Level == "warning");
    }

    [Fact]
    public void AssignToMatchingSlot_does_not_overwrite_filled_slots()
    {
        var viewModel = CreateViewModel();
        var first = _temp.CreateFile("first.pdf");
        viewModel.AssignToMatchingSlot(first);

        Assert.Null(viewModel.AssignToMatchingSlot(_temp.CreateFile("second.pdf")));
        Assert.Equal(first, viewModel.Slots[0].FilePath);
    }

    [Fact]
    public void AssignToMatchingSlot_detects_already_assigned_files()
    {
        var viewModel = CreateViewModel();
        var path = _temp.CreateFile("doc.pdf");
        viewModel.AssignToMatchingSlot(path);

        Assert.Same(viewModel.Slots[0], viewModel.AssignToMatchingSlot(path));
        Assert.Contains(_notifications.Messages, m => m.Title == "Bereits zugewiesen");
    }

    [Fact]
    public async Task AssignSelectedFile_assigns_the_selected_tree_file()
    {
        _temp.CreateFile("doc.pdf");
        var viewModel = CreateViewModel();
        await viewModel.Explorer.LoadAsync(_temp.Path);
        var slot = viewModel.Slots[0];

        Assert.False(viewModel.AssignSelectedFileCommand.CanExecute(slot));
        viewModel.Explorer.SelectedNode = viewModel.Explorer.RootNodes[0].Children.Single(n => n.Name == "doc.pdf");
        Assert.True(viewModel.AssignSelectedFileCommand.CanExecute(slot));

        viewModel.AssignSelectedFileCommand.Execute(slot);

        Assert.Equal("doc.pdf", slot.FileName);
    }

    [Fact]
    public async Task Double_click_in_the_tree_assigns_to_a_matching_slot()
    {
        _temp.CreateFile("settings.json");
        var viewModel = CreateViewModel();
        await viewModel.Explorer.LoadAsync(_temp.Path);

        viewModel.Explorer.ActivateNodeCommand.Execute(viewModel.Explorer.RootNodes[0].Children[0]);

        Assert.Equal("settings.json", viewModel.Slots[2].FileName);
    }

    [Fact]
    public void Reset_clears_all_slots()
    {
        var viewModel = CreateViewModel();
        FillRequiredSlots(viewModel);

        viewModel.ResetCommand.Execute(null);

        Assert.All(viewModel.Slots, s => Assert.Equal(UploadSlotState.Empty, s.State));
        Assert.False(viewModel.ResetCommand.CanExecute(null));
    }

    [Fact]
    public void ToggleTheme_switches_and_persists_the_theme()
    {
        var viewModel = CreateViewModel();
        viewModel.Initialize();
        Assert.False(viewModel.IsDarkTheme);

        viewModel.ToggleThemeCommand.Execute(null);

        Assert.True(viewModel.IsDarkTheme);
        Assert.Equal(AppTheme.Dark, _settings.Settings.Theme);
    }

    [Fact]
    public async Task Last_folder_is_saved_and_restored()
    {
        var first = CreateViewModel();
        await first.Explorer.LoadAsync(_temp.Path);
        Assert.Equal(Path.GetFullPath(_temp.Path), _settings.Settings.LastFolder);

        var second = CreateViewModel();
        await second.RestoreSessionAsync();

        Assert.Equal(Path.GetFullPath(_temp.Path), second.Explorer.RootPath);
    }

    /// <summary>Reports synchronously (unlike <see cref="Progress{T}"/>), so assertions are deterministic.</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
