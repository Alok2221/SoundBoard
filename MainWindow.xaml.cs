using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using SoundboardApp.Services;
using SoundboardApp.ViewModels;

namespace SoundboardApp;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        TrySetWindowIcon();

        var playback = new AudioPlaybackService();
        var devices = new AudioDeviceService();
        var settings = new SettingsService();
        var hotkeys = new HotkeyService();

        _viewModel = new MainViewModel(playback, devices, settings, hotkeys);
        DataContext = _viewModel;

        Loaded += (_, _) => _viewModel.Initialize(this);
        Closed += (_, _) => _viewModel.Dispose();
    }

    private void TrySetWindowIcon()
    {
        try
        {
            Icon = BitmapFrame.Create(
                new Uri("pack://application:,,,/resources/app.ico", UriKind.Absolute));
        }
        catch
        {
            // exe icon from ApplicationIcon still applies in Explorer
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel.TryCaptureHotkey(e))
            e.Handled = true;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
                return;

            _viewModel.AddDroppedFiles(paths);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Soundboard - file drop",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
