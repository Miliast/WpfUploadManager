using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UploadManager.Core.Configuration;
using UploadManager.Core.Formatting;
using UploadManager.Core.Services;
using UploadManager.Core.Validation;

namespace UploadManager.Core.ViewModels;

/// <summary>
/// One upload field on the right side of the main window.
/// </summary>
public sealed partial class UploadSlotViewModel : ObservableObject
{
    private readonly IFileDialogService _fileDialogService;

    public UploadSlotViewModel(UploadSlotDefinition definition, int number, IFileDialogService fileDialogService)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(fileDialogService);

        Definition = definition;
        Number = number;
        _fileDialogService = fileDialogService;
    }

    public UploadSlotDefinition Definition { get; }

    /// <summary>1-based position, shown as badge.</summary>
    public int Number { get; }

    public string Key => Definition.Key;

    public string Title => Definition.Title;

    public string Description => Definition.Description;

    public bool IsRequired => Definition.IsRequired;

    public string RequirementText => IsRequired ? "Pflicht" : "Optional";

    public string ConstraintsText =>
        $"{FileValidator.DescribeExtensions(Definition)} · max. {FileSizeFormatter.Format(Definition.MaxSizeBytes)}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileName), nameof(HasFile))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    public partial string? FilePath { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileSizeText))]
    public partial long FileSizeBytes { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(IsReady), nameof(IsInvalid), nameof(IsUploading), nameof(IsCompleted), nameof(IsSatisfied), nameof(StatusText))]
    public partial UploadSlotState State { get; private set; }

    /// <summary>Upload progress between 0 and 1.</summary>
    [ObservableProperty]
    public partial double Progress { get; private set; }

    /// <summary>While locked (e.g. during an upload) the file cannot be changed.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BrowseCommand), nameof(ClearCommand), nameof(DropFilesCommand))]
    public partial bool IsLocked { get; set; }

    public string? FileName => FilePath is null ? null : Path.GetFileName(FilePath);

    public string FileSizeText => FileSizeBytes > 0 ? FileSizeFormatter.Format(FileSizeBytes) : string.Empty;

    public bool HasFile => FilePath is not null;

    public bool HasError => ErrorMessage is not null;

    public bool IsEmpty => State == UploadSlotState.Empty;

    public bool IsReady => State == UploadSlotState.Ready;

    public bool IsInvalid => State == UploadSlotState.Invalid;

    public bool IsUploading => State == UploadSlotState.Uploading;

    public bool IsCompleted => State == UploadSlotState.Completed;

    /// <summary>
    /// <see langword="true"/> if this slot does not block an upload.
    /// </summary>
    public bool IsSatisfied =>
        State is UploadSlotState.Ready or UploadSlotState.Completed
        || (State == UploadSlotState.Empty && !IsRequired);

    public string StatusText => State switch
    {
        UploadSlotState.Empty => IsRequired ? "Datei fehlt" : "Keine Datei (optional)",
        UploadSlotState.Ready => "Bereit",
        UploadSlotState.Invalid => "Ungültig",
        UploadSlotState.Uploading => "Wird hochgeladen …",
        UploadSlotState.Completed => "Hochgeladen",
        _ => string.Empty,
    };

    /// <summary>
    /// Filter for the open file dialog, e.g. <c>Rechnung (*.pdf)|*.pdf|Alle Dateien (*.*)|*.*</c>.
    /// </summary>
    public string FileDialogFilter
    {
        get
        {
            const string allFiles = "Alle Dateien (*.*)|*.*";

            if (Definition.AllowedExtensions.Count == 0)
            {
                return allFiles;
            }

            var patterns = string.Join(';', Definition.AllowedExtensions
                .Select(e => "*" + UploadSlotDefinition.NormalizeExtension(e))
                .Distinct());

            return $"{Title.Replace('|', ' ')} ({patterns})|{patterns}|{allFiles}";
        }
    }

    public bool Accepts(string path) => Definition.Accepts(path);

    /// <summary>
    /// Assigns a file to this slot. Invalid files are kept (state <see cref="UploadSlotState.Invalid"/>)
    /// so the user sees why they were rejected.
    /// </summary>
    /// <returns><see langword="true"/> if the file is valid for this slot.</returns>
    public bool TryAssign(string path)
    {
        if (IsLocked)
        {
            return false;
        }

        var result = FileValidator.Validate(path, Definition);

        FilePath = path;
        FileSizeBytes = result.SizeBytes;
        ErrorMessage = result.ErrorMessage;
        Progress = 0;
        State = result.IsValid ? UploadSlotState.Ready : UploadSlotState.Invalid;

        return result.IsValid;
    }

    /// <summary>
    /// Validates the assigned file again (it may have been changed or deleted in the meantime).
    /// </summary>
    public bool Revalidate() => FilePath is not null && TryAssign(FilePath);

    [RelayCommand(CanExecute = nameof(CanClear))]
    public void Clear()
    {
        FilePath = null;
        FileSizeBytes = 0;
        ErrorMessage = null;
        Progress = 0;
        State = UploadSlotState.Empty;
    }

    internal void BeginUpload()
    {
        ErrorMessage = null;
        Progress = 0;
        State = UploadSlotState.Uploading;
    }

    internal void ReportProgress(double fraction)
    {
        if (State == UploadSlotState.Uploading)
        {
            Progress = Math.Clamp(fraction, 0, 1);
        }
    }

    internal void CompleteUpload()
    {
        Progress = 1;
        State = UploadSlotState.Completed;
    }

    /// <summary>Returns to <see cref="UploadSlotState.Ready"/> after a cancelled or failed upload.</summary>
    internal void ResetUpload()
    {
        Progress = 0;
        State = UploadSlotState.Ready;
    }

    private bool CanClear() => !IsLocked && HasFile;

    [RelayCommand(CanExecute = nameof(CanBrowse))]
    private void Browse()
    {
        var path = _fileDialogService.PickFile($"Datei für „{Title}“ auswählen", FileDialogFilter);
        if (path is not null)
        {
            TryAssign(path);
        }
    }

    private bool CanBrowse() => !IsLocked;

    [RelayCommand(CanExecute = nameof(CanDropFiles))]
    private void DropFiles(IReadOnlyList<string>? paths)
    {
        if (paths is { Count: > 0 })
        {
            TryAssign(paths[0]);
        }
    }

    private bool CanDropFiles(IReadOnlyList<string>? paths) => !IsLocked && paths is { Count: > 0 };
}
