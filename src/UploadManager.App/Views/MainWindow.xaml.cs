using System.ComponentModel;
using System.Windows;
using UploadManager.Core.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Controls;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace UploadManager.App.Views;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainViewModel viewModel, ISnackbarService snackbarService)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(snackbarService);

        ViewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();
        FitToWorkArea();

        snackbarService.SetSnackbarPresenter(SnackbarPresenter);
    }

    public MainViewModel ViewModel { get; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (ViewModel.IsUploading)
        {
            var result = MessageBox.Show(
                this,
                "Es läuft noch ein Upload. Möchtest du ihn abbrechen und die Anwendung beenden?",
                "Upload läuft",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            ViewModel.UploadCancelCommand.Execute(null);
        }

        base.OnClosing(e);
    }

    /// <summary>
    /// Keeps the default window size within the screen on small displays.
    /// </summary>
    private void FitToWorkArea()
    {
        var workArea = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, Math.Min(Width, workArea.Width * 0.95));
        Height = Math.Max(MinHeight, Math.Min(Height, workArea.Height * 0.95));
    }
}
