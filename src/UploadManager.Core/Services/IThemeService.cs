namespace UploadManager.Core.Services;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

public interface IThemeService
{
    /// <summary>
    /// The theme that is currently visible (never <see cref="AppTheme.System"/>).
    /// </summary>
    AppTheme ActualTheme { get; }

    void Apply(AppTheme theme);
}
