using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UploadManager.Core.Configuration;
using UploadManager.Core.Services;

namespace UploadManager.Core.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IUploadService _uploadService;
    private readonly INotificationService _notificationService;
    private readonly IThemeService _themeService;
    private readonly IUserSettingsStore _settingsStore;
    private readonly ILogger<MainViewModel> _logger;
    private readonly UserSettings _settings;

    public MainViewModel(
        IOptions<UploadOptions> options,
        IUploadService uploadService,
        IFileDialogService fileDialogService,
        INotificationService notificationService,
        IThemeService themeService,
        IUserSettingsStore settingsStore,
        ILogger<MainViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _uploadService = uploadService;
        _notificationService = notificationService;
        _themeService = themeService;
        _settingsStore = settingsStore;
        _logger = logger;
        _settings = settingsStore.Load();

        Slots = options.Value.Slots
            .Select((definition, index) => new UploadSlotViewModel(definition, index + 1, fileDialogService))
            .ToList()
            .AsReadOnly();

        foreach (var slot in Slots)
        {
            slot.PropertyChanged += OnSlotPropertyChanged;
        }

        Explorer = new FileExplorerViewModel(fileDialogService, notificationService, IsSupportedByAnySlot, _settings.ShowOnlySupportedFiles);
        Explorer.FileActivated += (_, node) => AssignToMatchingSlot(node.FullPath);
        Explorer.PropertyChanged += OnExplorerPropertyChanged;

        StatusText = "Bereit";
        UploadTargetText = options.Value.UseSimulation
            ? "Simulationsmodus – es werden keine Daten gesendet"
            : $"Ziel: {options.Value.Endpoint}";
    }

    public FileExplorerViewModel Explorer { get; }

    public IReadOnlyList<UploadSlotViewModel> Slots { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UploadCommand), nameof(ResetCommand), nameof(AssignSelectedFileCommand))]
    public partial bool IsUploading { get; private set; }

    /// <summary>Overall upload progress between 0 and 1.</summary>
    [ObservableProperty]
    public partial double OverallProgress { get; private set; }

    [ObservableProperty]
    public partial string StatusText { get; private set; }

    [ObservableProperty]
    public partial bool IsDarkTheme { get; private set; }

    /// <summary>Where uploads go, shown in the status bar.</summary>
    public string UploadTargetText { get; }

    public int RequiredSlotCount => Slots.Count(s => s.IsRequired);

    public int SatisfiedRequiredSlotCount => Slots.Count(s => s.IsRequired && s.IsSatisfied);

    public bool IsReadyForUpload => Slots.All(s => s.IsSatisfied) && Slots.Any(s => s.IsReady);

    public string ReadinessText
    {
        get
        {
            if (Slots.Any(s => s.IsInvalid))
            {
                return "Bitte ungültige Dateien ersetzen";
            }

            if (Slots.Count > 0 && Slots.All(s => s.IsCompleted || (s.IsEmpty && !s.IsRequired)) && Slots.Any(s => s.IsCompleted))
            {
                return "Alle Dateien wurden hochgeladen";
            }

            return RequiredSlotCount == 0
                ? $"{Slots.Count(s => s.IsReady)} Datei(en) bereit"
                : $"{SatisfiedRequiredSlotCount} von {RequiredSlotCount} Pflichtfeldern bereit";
        }
    }

    /// <summary>
    /// Applies the saved theme. Call before the main window is shown to avoid flicker.
    /// </summary>
    public void Initialize()
    {
        _themeService.Apply(_settings.Theme);
        IsDarkTheme = _themeService.ActualTheme == AppTheme.Dark;
    }

    /// <summary>
    /// Re-opens the folder from the last session, if it still exists.
    /// </summary>
    public async Task RestoreSessionAsync()
    {
        if (!string.IsNullOrWhiteSpace(_settings.LastFolder) && Directory.Exists(_settings.LastFolder))
        {
            await Explorer.LoadAsync(_settings.LastFolder);
        }
    }

    /// <summary>
    /// Assigns a file to the first suitable slot: an empty one, otherwise one with an invalid file.
    /// </summary>
    /// <returns>The slot the file was assigned to, or <see langword="null"/>.</returns>
    public UploadSlotViewModel? AssignToMatchingSlot(string path)
    {
        if (IsUploading)
        {
            return null;
        }

        var fileName = Path.GetFileName(path);

        var existing = Slots.FirstOrDefault(s => string.Equals(s.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            _notificationService.ShowInfo("Bereits zugewiesen", $"„{fileName}“ ist bereits dem Feld „{existing.Title}“ zugewiesen.");
            return existing;
        }

        var candidates = Slots.Where(s => s.Accepts(path)).ToList();
        if (candidates.Count == 0)
        {
            _notificationService.ShowWarning("Kein passendes Feld", $"Für „{fileName}“ gibt es kein Upload-Feld, das diesen Dateityp akzeptiert.");
            return null;
        }

        var target = candidates.FirstOrDefault(s => s.IsEmpty) ?? candidates.FirstOrDefault(s => s.IsInvalid);
        if (target is null)
        {
            _notificationService.ShowInfo(
                "Passende Felder belegt",
                "Ziehe die Datei direkt auf ein Feld oder nutze „Markierte übernehmen“, um eine Datei zu ersetzen.");
            return null;
        }

        Assign(target, path);
        return target;
    }

    [RelayCommand(CanExecute = nameof(CanAssignSelectedFile))]
    private void AssignSelectedFile(UploadSlotViewModel? slot)
    {
        if (slot is not null && Explorer.SelectedFile is { } file)
        {
            Assign(slot, file.FullPath);
        }
    }

    private bool CanAssignSelectedFile(UploadSlotViewModel? slot) =>
        !IsUploading && slot is not null && Explorer.SelectedFile is not null;

    private bool CanUpload() => !IsUploading && IsReadyForUpload;

    [RelayCommand(CanExecute = nameof(CanUpload), IncludeCancelCommand = true)]
    private async Task UploadAsync(CancellationToken cancellationToken)
    {
        var changed = Slots.Where(s => s.IsReady && !s.Revalidate()).ToList();
        if (changed.Count > 0)
        {
            _notificationService.ShowWarning(
                "Dateien haben sich geändert",
                $"Bitte prüfe: {string.Join(", ", changed.Select(s => s.Title))}.");
            return;
        }

        var slotsToUpload = Slots.Where(s => s.IsReady).ToList();
        var files = slotsToUpload.Select(s => new UploadFile(s.Key, s.FilePath!, s.FileSizeBytes)).ToList();
        var slotsByKey = slotsToUpload.ToDictionary(s => s.Key);
        var bytesSentBySlot = new Dictionary<string, long>();
        var totalBytes = Math.Max(1, files.Sum(f => f.SizeBytes));

        // Progress<T> marshals the callbacks to the UI thread (captured synchronization context).
        var progress = new Progress<UploadProgress>(p =>
        {
            if (!slotsByKey.TryGetValue(p.SlotKey, out var slot))
            {
                return;
            }

            slot.ReportProgress(p.Fraction);

            lock (bytesSentBySlot)
            {
                bytesSentBySlot[p.SlotKey] = p.BytesSent;
                if (IsUploading)
                {
                    OverallProgress = Math.Clamp((double)bytesSentBySlot.Values.Sum() / totalBytes, 0, 1);
                }
            }
        });

        IsUploading = true;
        OverallProgress = 0;
        StatusText = $"{files.Count} Datei(en) werden hochgeladen …";

        foreach (var slot in Slots)
        {
            slot.IsLocked = true;
        }

        foreach (var slot in slotsToUpload)
        {
            slot.BeginUpload();
        }

        try
        {
            await _uploadService.UploadAsync(files, progress, cancellationToken);

            foreach (var slot in slotsToUpload)
            {
                slot.CompleteUpload();
            }

            OverallProgress = 1;
            StatusText = $"{files.Count} Datei(en) erfolgreich hochgeladen ({DateTime.Now:t} Uhr).";
            _notificationService.ShowSuccess("Upload abgeschlossen", $"{files.Count} Datei(en) wurden erfolgreich hochgeladen.");
        }
        catch (OperationCanceledException)
        {
            foreach (var slot in slotsToUpload)
            {
                slot.ResetUpload();
            }

            OverallProgress = 0;
            StatusText = "Upload abgebrochen.";
            _notificationService.ShowInfo("Abgebrochen", "Der Upload wurde abgebrochen.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upload fehlgeschlagen");

            foreach (var slot in slotsToUpload)
            {
                slot.ResetUpload();
            }

            OverallProgress = 0;
            StatusText = "Upload fehlgeschlagen.";
            _notificationService.ShowError("Upload fehlgeschlagen", ex.Message);
        }
        finally
        {
            IsUploading = false;

            foreach (var slot in Slots)
            {
                slot.IsLocked = false;
            }
        }
    }

    private bool CanReset() => !IsUploading && Slots.Any(s => s.HasFile);

    [RelayCommand(CanExecute = nameof(CanReset))]
    private void Reset()
    {
        foreach (var slot in Slots)
        {
            slot.Clear();
        }

        OverallProgress = 0;
        StatusText = "Alle Felder wurden zurückgesetzt.";
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        var theme = _themeService.ActualTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        _themeService.Apply(theme);
        IsDarkTheme = theme == AppTheme.Dark;

        _settings.Theme = theme;
        _settingsStore.Save(_settings);
    }

    private void Assign(UploadSlotViewModel slot, string path)
    {
        StatusText = slot.TryAssign(path)
            ? $"„{slot.FileName}“ wurde „{slot.Title}“ zugewiesen."
            : $"„{Path.GetFileName(path)}“ passt nicht zu „{slot.Title}“: {slot.ErrorMessage}";
    }

    private bool IsSupportedByAnySlot(string path) => Slots.Any(s => s.Accepts(path));

    private void OnSlotPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(UploadSlotViewModel.State) or nameof(UploadSlotViewModel.FilePath)))
        {
            return;
        }

        OnPropertyChanged(nameof(SatisfiedRequiredSlotCount));
        OnPropertyChanged(nameof(IsReadyForUpload));
        OnPropertyChanged(nameof(ReadinessText));
        UploadCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
    }

    private void OnExplorerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FileExplorerViewModel.SelectedNode):
                AssignSelectedFileCommand.NotifyCanExecuteChanged();
                break;

            case nameof(FileExplorerViewModel.RootPath) when Explorer.RootPath is not null:
                _settings.LastFolder = Explorer.RootPath;
                _settingsStore.Save(_settings);
                break;

            case nameof(FileExplorerViewModel.ShowOnlySupportedFiles):
                _settings.ShowOnlySupportedFiles = Explorer.ShowOnlySupportedFiles;
                _settingsStore.Save(_settings);
                break;
        }
    }
}
