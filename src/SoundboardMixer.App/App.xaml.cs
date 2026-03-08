using System.Windows;
using SoundboardMixer.App.Services;
using SoundboardMixer.App.Services.Audio;
using SoundboardMixer.App.Services.Hotkeys;
using SoundboardMixer.App.ViewModels;

namespace SoundboardMixer.App;

public partial class App : Application
{
    private MainWindowViewModel? _mainWindowViewModel;
    private IAudioEngineService? _audioEngineService;
    private IGlobalHotkeyService? _hotkeyService;

    protected override async void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);

        try
        {
            var logService = new LogService();
            var settingsService = new SettingsService(logService);
            var fileDialogService = new FileDialogService();
            var clipLoaderService = new ClipLoaderService(logService);

            _audioEngineService = new AudioEngineService(logService);
            _hotkeyService = new GlobalHotkeyService(logService);

            var settings = await settingsService.LoadAsync();

            _mainWindowViewModel = new MainWindowViewModel(
                settings,
                settingsService,
                fileDialogService,
                clipLoaderService,
                _audioEngineService,
                _hotkeyService,
                logService);

            var window = new MainWindow(_mainWindowViewModel, _hotkeyService);
            _mainWindowViewModel.ApplyWindowSettings(window);

            MainWindow = window;
            window.Show();

            await _mainWindowViewModel.InitializeAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"The application failed to start.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Soundboard Mixer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        try
        {
            _mainWindowViewModel?.ShutdownAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // The app is exiting anyway.
        }

        _hotkeyService?.Dispose();
        _audioEngineService?.Dispose();

        base.OnExit(eventArgs);
    }
}
