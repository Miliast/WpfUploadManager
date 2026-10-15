using UploadManager.Core.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace UploadManager.App.Services;

/// <summary>
/// Switches between the WPF-UI light and dark theme.
/// </summary>
public sealed class WpfThemeService : IThemeService
{
    public AppTheme ActualTheme =>
        ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark ? AppTheme.Dark : AppTheme.Light;

    public void Apply(AppTheme theme)
    {
        var target = theme switch
        {
            AppTheme.Light => ApplicationTheme.Light,
            AppTheme.Dark => ApplicationTheme.Dark,
            _ => IsSystemDark() ? ApplicationTheme.Dark : ApplicationTheme.Light,
        };

        ApplicationThemeManager.Apply(target, WindowBackdropType.Mica, updateAccent: true);
    }

    private static bool IsSystemDark() =>
        ApplicationThemeManager.GetSystemTheme() is SystemTheme.Dark or SystemTheme.HCBlack or SystemTheme.Glow or SystemTheme.CapturedMotion;
}
