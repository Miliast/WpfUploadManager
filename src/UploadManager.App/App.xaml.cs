using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UploadManager.App.Services;
using UploadManager.App.Views;
using UploadManager.Core.Configuration;
using UploadManager.Core.Services;
using UploadManager.Core.ViewModels;
using Wpf.Ui;
using IThemeService = UploadManager.Core.Services.IThemeService;

namespace UploadManager.App;

/// <summary>
/// Composition root: configures the generic host (configuration, logging, DI) and shows the main window.
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RegisterGlobalExceptionHandlers();

        try
        {
            _host = CreateHostBuilder(e.Args).Build();

            // Validates UploadOptions (ValidateOnStart).
            await _host.StartAsync();

            var viewModel = _host.Services.GetRequiredService<MainViewModel>();
            viewModel.Initialize();

            var window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();

            await viewModel.RestoreSessionAsync();
        }
        catch (OptionsValidationException ex)
        {
            ShowError("Die Konfiguration (appsettings.json) ist ungültig:" + Environment.NewLine + Environment.NewLine
                + string.Join(Environment.NewLine, ex.Failures.Select(f => "• " + f)));
            Shutdown(1);
        }
        catch (Exception ex)
        {
            GetLogger()?.LogCritical(ex, "Anwendung konnte nicht gestartet werden");
            ShowError("Die Anwendung konnte nicht gestartet werden:" + Environment.NewLine + Environment.NewLine + ex.Message);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            _host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            _host.Dispose();
        }

        base.OnExit(e);
    }

    private static HostApplicationBuilder CreateHostBuilder(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Logging.ClearProviders();
        builder.Logging.AddDebug();

        var services = builder.Services;

        services.AddOptions<UploadOptions>()
            .Bind(builder.Configuration.GetSection(UploadOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<UploadOptions>, UploadOptionsValidator>();

        // Upload: simulated by default, real HTTP upload when "Upload:UseSimulation" is false.
        services.AddHttpClient(HttpUploadService.HttpClientName, (sp, client) =>
            client.Timeout = sp.GetRequiredService<IOptions<UploadOptions>>().Value.Timeout);
        services.AddSingleton<IUploadService>(sp =>
            sp.GetRequiredService<IOptions<UploadOptions>>().Value.UseSimulation
                ? ActivatorUtilities.CreateInstance<SimulatedUploadService>(sp)
                : ActivatorUtilities.CreateInstance<HttpUploadService>(sp));

        // UI services
        services.AddSingleton<ISnackbarService, SnackbarService>();
        services.AddSingleton<INotificationService, SnackbarNotificationService>();
        services.AddSingleton<IFileDialogService, WpfFileDialogService>();
        services.AddSingleton<IThemeService, WpfThemeService>();
        services.AddSingleton<IUserSettingsStore>(sp => new JsonUserSettingsStore(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WpfUploadManager", "settings.json"),
            sp.GetRequiredService<ILogger<JsonUserSettingsStore>>()));

        // View models & views
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return builder;
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            GetLogger()?.LogCritical(args.ExceptionObject as Exception, "Unbehandelte Ausnahme (AppDomain)");

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            GetLogger()?.LogError(args.Exception, "Unbeobachtete Ausnahme in einem Task");
            args.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        GetLogger()?.LogError(e.Exception, "Unbehandelte Ausnahme im UI-Thread");

        ShowError("Ein unerwarteter Fehler ist aufgetreten:" + Environment.NewLine + Environment.NewLine + e.Exception.Message);

        // Keep the application alive; the error has been logged.
        e.Handled = true;
    }

    private ILogger<App>? GetLogger() => _host?.Services.GetService<ILogger<App>>();

    private void ShowError(string message)
    {
        const string caption = "Upload Manager";

        if (MainWindow is { IsLoaded: true } owner)
        {
            MessageBox.Show(owner, message, caption, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        else
        {
            MessageBox.Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
