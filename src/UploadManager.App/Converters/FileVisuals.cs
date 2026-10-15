using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace UploadManager.App.Converters;

/// <summary>
/// Icons and colors for the file tree. Folder colors are derived from the folder name with a
/// stable hash, so a folder always keeps its color (across sessions as well).
/// </summary>
public static class FileVisuals
{
    private static readonly Brush[] FolderPalette =
    [
        CreateBrush("#8B8B93"), // gray
        CreateBrush("#8A7CD8"), // purple
        CreateBrush("#B7ABF2"), // lavender
        CreateBrush("#F2B33D"), // amber
        CreateBrush("#8794F7"), // periwinkle
        CreateBrush("#3A73DB"), // blue
        CreateBrush("#E2573A"), // orange
        CreateBrush("#3BAF7E"), // green
        CreateBrush("#E06AA3"), // pink
        CreateBrush("#3BB0C4"), // teal
    ];

    private static readonly Brush PdfBrush = CreateBrush("#E5534B");
    private static readonly Brush WordBrush = CreateBrush("#4A86E8");
    private static readonly Brush SheetBrush = CreateBrush("#33A36B");
    private static readonly Brush CodeBrush = CreateBrush("#E8A93A");
    private static readonly Brush ImageBrush = CreateBrush("#A77BF3");
    private static readonly Brush ArchiveBrush = CreateBrush("#B08B5B");
    private static readonly Brush SlideBrush = CreateBrush("#E07A3F");
    private static readonly Brush MediaBrush = CreateBrush("#3BB0C4");
    private static readonly Brush DefaultFileBrush = CreateBrush("#8B8B93");

    public static Brush GetFolderBrush(string? name)
    {
        // FNV-1a: unlike string.GetHashCode() it is stable across processes.
        var hash = 2166136261u;
        foreach (var c in (name ?? string.Empty).ToUpperInvariant())
        {
            hash = (hash ^ c) * 16777619u;
        }

        return FolderPalette[hash % (uint)FolderPalette.Length];
    }

    public static SymbolRegular GetFileSymbol(string? extension) => extension switch
    {
        ".pdf" => SymbolRegular.DocumentPdf20,
        ".xlsx" or ".xls" or ".csv" or ".tsv" or ".ods" => SymbolRegular.DocumentTable20,
        ".json" or ".xml" or ".yaml" or ".yml" or ".toml" or ".ini" or ".config" => SymbolRegular.Braces20,
        ".cs" or ".xaml" or ".js" or ".ts" or ".py" or ".html" or ".css" or ".sql" => SymbolRegular.Code20,
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".svg" or ".webp" or ".tif" or ".tiff" => SymbolRegular.Image20,
        ".zip" or ".7z" or ".rar" or ".tar" or ".gz" => SymbolRegular.FolderZip20,
        ".ppt" or ".pptx" or ".odp" => SymbolRegular.SlideText20,
        ".mp4" or ".mov" or ".avi" or ".mkv" => SymbolRegular.Video20,
        ".mp3" or ".wav" or ".flac" or ".m4a" => SymbolRegular.MusicNote120,
        ".txt" or ".md" or ".log" or ".rtf" or ".doc" or ".docx" or ".odt" => SymbolRegular.DocumentText20,
        _ => SymbolRegular.Document20,
    };

    public static Brush GetFileBrush(string? extension) => extension switch
    {
        ".pdf" => PdfBrush,
        ".doc" or ".docx" or ".odt" or ".rtf" => WordBrush,
        ".xlsx" or ".xls" or ".csv" or ".tsv" or ".ods" => SheetBrush,
        ".json" or ".xml" or ".yaml" or ".yml" or ".toml" or ".ini" or ".config"
            or ".cs" or ".xaml" or ".js" or ".ts" or ".py" or ".html" or ".css" or ".sql" => CodeBrush,
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".svg" or ".webp" or ".tif" or ".tiff" => ImageBrush,
        ".zip" or ".7z" or ".rar" or ".tar" or ".gz" => ArchiveBrush,
        ".ppt" or ".pptx" or ".odp" => SlideBrush,
        ".mp4" or ".mov" or ".avi" or ".mkv" or ".mp3" or ".wav" or ".flac" or ".m4a" => MediaBrush,
        _ => DefaultFileBrush,
    };

    private static SolidColorBrush CreateBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

/// <summary>Folder name → folder color.</summary>
[ValueConversion(typeof(string), typeof(Brush))]
public sealed class FolderColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        FileVisuals.GetFolderBrush(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>File extension → icon.</summary>
[ValueConversion(typeof(string), typeof(SymbolRegular))]
public sealed class FileSymbolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        FileVisuals.GetFileSymbol(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>File extension → icon color.</summary>
[ValueConversion(typeof(string), typeof(Brush))]
public sealed class FileColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        FileVisuals.GetFileBrush(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
