using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundboardMixer.App.Models;
using SoundboardMixer.App.Services;
using SoundboardMixer.App.Services.Audio;
using SoundboardMixer.App.Services.Hotkeys;

namespace SoundboardMixer.App.ViewModels;

internal sealed class MainWindowViewModel : ObservableObject
{
    private const string StopAllHotkeyId = "__stop_all__";

    private readonly AppSettings _settings;
    private readonly ISettingsService _settingsService;
    private readonly IFileDialogService _fileDialogService;
    private readonly ClipLoaderService _clipLoaderService;
    private readonly IStartupRegistrationService _startupRegistrationService;
    private readonly IAudioEngineService _audioEngineService;
    private readonly IGlobalHotkeyService _globalHotkeyService;
    private readonly ILogService _logService;
    private readonly Dispatcher _uiDispatcher;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _playbackTimer;
    private readonly Dictionary<string, ClipPlaybackUiState> _playbackStates = [];

    private AudioDeviceInfo? _selectedMicrophone;
    private AudioDeviceInfo? _selectedOutput;
    private AudioDeviceInfo? _selectedSpeakerOutput;
    private ClipItemViewModel? _selectedClip;
    private string _engineStatus = "Initializing audio...";
    private string _clipBrowserSearchText = string.Empty;
    private double _microphoneVolumePercent;
    private double _soundboardVolumePercent;
    private bool _isMicrophoneMuted;
    private bool _isSpeakerMonitorEnabled;
    private bool _isClipBrowserOpen;
    private bool _isSettingsOpen;
    private bool _isAutoStartOnWindowsStart;
    private bool _isMinimizeToSystemTrayOnClose;
    private string? _stopAllHotkeyText;
    private string _stopAllHotkeyStatus = string.Empty;
    private bool _isInitializing;
    private bool _isRefreshingDevices;
    private Task? _shutdownTask;
    private readonly CollectionViewSource _clipBrowserViewSource;

    public MainWindowViewModel(
        AppSettings settings,
        ISettingsService settingsService,
        IFileDialogService fileDialogService,
        ClipLoaderService clipLoaderService,
        IStartupRegistrationService startupRegistrationService,
        IAudioEngineService audioEngineService,
        IGlobalHotkeyService globalHotkeyService,
        ILogService logService)
    {
        _settings = settings;
        _settingsService = settingsService;
        _fileDialogService = fileDialogService;
        _clipLoaderService = clipLoaderService;
        _startupRegistrationService = startupRegistrationService;
        _audioEngineService = audioEngineService;
        _globalHotkeyService = globalHotkeyService;
        _logService = logService;
        _uiDispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        _microphoneVolumePercent = settings.MicrophoneVolume * 100.0;
        _soundboardVolumePercent = settings.SoundboardVolume * 100.0;
        _isMicrophoneMuted = settings.IsMicrophoneMuted;
        _isSpeakerMonitorEnabled = settings.IsSpeakerMonitorEnabled;
        _isAutoStartOnWindowsStart = settings.AutoStartOnWindowsStart;
        _isMinimizeToSystemTrayOnClose = settings.MinimizeToSystemTrayOnClose;
        _stopAllHotkeyText = settings.StopAllHotkeyText;
        _clipBrowserViewSource = new CollectionViewSource { Source = Clips };
        _clipBrowserViewSource.Filter += OnClipBrowserFilter;

        RefreshDevicesCommand = new RelayCommand(RefreshDevices);
        RestartAudioCommand = new RelayCommand(RestartAudio);
        AddClipsCommand = new AsyncRelayCommand(AddClipsAsync);
        StopAllCommand = new RelayCommand(StopAllClips);
        OpenClipBrowserCommand = new RelayCommand(OpenClipBrowser);
        CloseClipBrowserCommand = new RelayCommand(() => IsClipBrowserOpen = false);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        CloseSettingsCommand = new RelayCommand(() => IsSettingsOpen = false);
        RemoveSelectedClipCommand = new RelayCommand(RemoveSelectedClip, () => SelectedClip is not null);
        PlaySelectedClipCommand = new AsyncRelayCommand(PlaySelectedClipAsync, () => SelectedClip is not null);
        RemoveClipCommand = new RelayCommand<ClipItemViewModel?>(RemoveClip);
        PlayClipCommand = new AsyncRelayCommand<ClipItemViewModel?>(PlayClipAsync);
        ToggleClipPlaybackCommand = new AsyncRelayCommand<ClipItemViewModel?>(ToggleClipPlaybackAsync);

        _audioEngineService.StatusChanged += OnAudioEngineStatusChanged;
        _globalHotkeyService.HotkeyPressed += OnHotkeyPressed;
        _logService.EntryLogged += OnLogEntryLogged;

        _saveTimer = new DispatcherTimer(DispatcherPriority.Background, _uiDispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _saveTimer.Tick += OnSaveTimerTick;

        _playbackTimer = new DispatcherTimer(DispatcherPriority.Render, _uiDispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _playbackTimer.Tick += OnPlaybackTimerTick;
    }

    public ObservableCollection<AudioDeviceInfo> MicrophoneDevices { get; } = [];

    public ObservableCollection<AudioDeviceInfo> OutputDevices { get; } = [];

    public ObservableCollection<ClipItemViewModel> Clips { get; } = [];

    public ICollectionView ClipBrowserClips => _clipBrowserViewSource.View;

    public ObservableCollection<LogEntry> LogEntries { get; } = [];

    public RelayCommand RefreshDevicesCommand { get; }

    public RelayCommand RestartAudioCommand { get; }

    public AsyncRelayCommand AddClipsCommand { get; }

    public RelayCommand StopAllCommand { get; }

    public RelayCommand OpenClipBrowserCommand { get; }

    public RelayCommand CloseClipBrowserCommand { get; }

    public RelayCommand OpenSettingsCommand { get; }

    public RelayCommand CloseSettingsCommand { get; }

    public RelayCommand RemoveSelectedClipCommand { get; }

    public AsyncRelayCommand PlaySelectedClipCommand { get; }

    public RelayCommand<ClipItemViewModel?> RemoveClipCommand { get; }

    public AsyncRelayCommand<ClipItemViewModel?> PlayClipCommand { get; }

    public AsyncRelayCommand<ClipItemViewModel?> ToggleClipPlaybackCommand { get; }

    public AudioDeviceInfo? SelectedMicrophone
    {
        get => _selectedMicrophone;
        set
        {
            if (SetProperty(ref _selectedMicrophone, value))
            {
                OnSelectedDeviceChanged();
            }
        }
    }

    public AudioDeviceInfo? SelectedOutput
    {
        get => _selectedOutput;
        set
        {
            if (SetProperty(ref _selectedOutput, value))
            {
                OnSelectedDeviceChanged();
            }
        }
    }

    public AudioDeviceInfo? SelectedSpeakerOutput
    {
        get => _selectedSpeakerOutput;
        set
        {
            if (SetProperty(ref _selectedSpeakerOutput, value))
            {
                OnSelectedDeviceChanged();
            }
        }
    }

    public ClipItemViewModel? SelectedClip
    {
        get => _selectedClip;
        set
        {
            if (SetProperty(ref _selectedClip, value))
            {
                RemoveSelectedClipCommand.NotifyCanExecuteChanged();
                PlaySelectedClipCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string EngineStatus
    {
        get => _engineStatus;
        private set => SetProperty(ref _engineStatus, value);
    }

    public string ClipBrowserSearchText
    {
        get => _clipBrowserSearchText;
        set
        {
            if (SetProperty(ref _clipBrowserSearchText, value ?? string.Empty))
            {
                ClipBrowserClips.Refresh();
            }
        }
    }

    public bool IsClipBrowserOpen
    {
        get => _isClipBrowserOpen;
        set => SetProperty(ref _isClipBrowserOpen, value);
    }

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set => SetProperty(ref _isSettingsOpen, value);
    }

    public bool IsAutoStartOnWindowsStart
    {
        get => _isAutoStartOnWindowsStart;
        set
        {
            if (!SetProperty(ref _isAutoStartOnWindowsStart, value))
            {
                return;
            }

            if (!ApplyAutoStartSetting(value))
            {
                _isAutoStartOnWindowsStart = !value;
                OnPropertyChanged(nameof(IsAutoStartOnWindowsStart));
            }

            ScheduleSave();
        }
    }

    public bool IsMinimizeToSystemTrayOnClose
    {
        get => _isMinimizeToSystemTrayOnClose;
        set
        {
            if (SetProperty(ref _isMinimizeToSystemTrayOnClose, value))
            {
                ScheduleSave();
            }
        }
    }

    public string? StopAllHotkeyText
    {
        get => _stopAllHotkeyText;
        set
        {
            var nextValue = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (SetProperty(ref _stopAllHotkeyText, nextValue))
            {
                ApplyHotkeyBindings();
                ScheduleSave();
            }
        }
    }

    public string StopAllHotkeyStatus
    {
        get => _stopAllHotkeyStatus;
        private set => SetProperty(ref _stopAllHotkeyStatus, value);
    }

    public double MicrophoneVolumePercent
    {
        get => _microphoneVolumePercent;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 300.0);
            if (SetProperty(ref _microphoneVolumePercent, clamped))
            {
                ApplyMixSettings();
                ScheduleSave();
            }
        }
    }

    public double SoundboardVolumePercent
    {
        get => _soundboardVolumePercent;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 100.0);
            if (SetProperty(ref _soundboardVolumePercent, clamped))
            {
                ApplyMixSettings();
                ScheduleSave();
            }
        }
    }

    public bool IsMicrophoneMuted
    {
        get => _isMicrophoneMuted;
        set
        {
            if (SetProperty(ref _isMicrophoneMuted, value))
            {
                ApplyMixSettings();
                ScheduleSave();
            }
        }
    }

    public bool IsSpeakerMonitorEnabled
    {
        get => _isSpeakerMonitorEnabled;
        set
        {
            if (SetProperty(ref _isSpeakerMonitorEnabled, value))
            {
                OnSelectedDeviceChanged();
                ScheduleSave();
            }
        }
    }

    public async Task InitializeAsync()
    {
        _isInitializing = true;

        try
        {
            RefreshDevices();
            await LoadInitialClipsAsync();
            ApplyHotkeyBindings();
            ApplyMixSettings();
            EnsureStartupRegistration();
            RestartAudio();
        }
        finally
        {
            _isInitializing = false;
        }

        await FlushSettingsAsync();
    }

    public void ApplyWindowSettings(Window window)
    {
        var windowSettings = _settings.Window;
        window.Width = windowSettings.Width > 0 ? windowSettings.Width : 1180;
        window.Height = windowSettings.Height > 0 ? windowSettings.Height : 760;

        if (windowSettings.Left.HasValue && windowSettings.Top.HasValue)
        {
            var savedBounds = new Rect(windowSettings.Left.Value, windowSettings.Top.Value, window.Width, window.Height);
            if (IsWindowPlacementVisible(savedBounds))
            {
                window.Left = windowSettings.Left.Value;
                window.Top = windowSettings.Top.Value;
            }
            else
            {
                _logService.Warning("Saved window position is outside the current desktop. Using the default startup position.");
            }
        }

        if (windowSettings.IsMaximized)
        {
            window.WindowState = WindowState.Maximized;
        }
    }

    public void CaptureWindowSettings(Window window)
    {
        var bounds = window.WindowState == WindowState.Normal ? new Rect(window.Left, window.Top, window.Width, window.Height) : window.RestoreBounds;

        _settings.Window.Width = bounds.Width > 0 ? bounds.Width : _settings.Window.Width;
        _settings.Window.Height = bounds.Height > 0 ? bounds.Height : _settings.Window.Height;
        _settings.Window.Left = bounds.Left;
        _settings.Window.Top = bounds.Top;
        _settings.Window.IsMaximized = window.WindowState == WindowState.Maximized;
        ScheduleSave();
    }

    public void OnHotkeyWindowReady()
    {
        ApplyHotkeyBindings();
    }

    public async Task FlushSettingsAsync()
    {
        await SaveSettingsAsync().ConfigureAwait(false);
    }

    public Task ShutdownAsync()
    {
        VerifyUiAccess();

        if (_shutdownTask is not null)
        {
            return _shutdownTask;
        }

        _shutdownTask = ShutdownCoreAsync();
        return _shutdownTask;
    }

    private async Task ShutdownCoreAsync()
    {
        _saveTimer.Stop();
        _saveTimer.Tick -= OnSaveTimerTick;
        _playbackTimer.Stop();
        _playbackTimer.Tick -= OnPlaybackTimerTick;

        AppSettings? snapshot = null;
        try
        {
            snapshot = BuildSettingsSnapshot();
            ApplySettingsSnapshot(snapshot);
        }
        catch (Exception exception)
        {
            _logService.Error("Failed to capture settings during shutdown.", exception);
        }

        _globalHotkeyService.HotkeyPressed -= OnHotkeyPressed;
        _clipBrowserViewSource.Filter -= OnClipBrowserFilter;

        try
        {
            _audioEngineService.Stop();
        }
        catch (Exception exception)
        {
            _logService.Error("Failed to stop the audio engine during shutdown.", exception);
        }

        _audioEngineService.StatusChanged -= OnAudioEngineStatusChanged;
        _logService.EntryLogged -= OnLogEntryLogged;

        if (snapshot is not null)
        {
            await _settingsService.SaveAsync(snapshot).ConfigureAwait(false);
        }
    }

    private async Task LoadInitialClipsAsync()
    {
        Clips.Clear();

        foreach (var clipSettings in _settings.Clips)
        {
            var displayName = string.IsNullOrWhiteSpace(clipSettings.DisplayName)
                ? Path.GetFileNameWithoutExtension(clipSettings.SourcePath)
                : clipSettings.DisplayName;

            var clip = new ClipItemViewModel(
                clipSettings.Id,
                displayName,
                clipSettings.SourcePath,
                clipSettings.Volume,
                clipSettings.HotkeyText,
                OnClipChanged);

            await ReloadClipAsync(clip);
            Clips.Add(clip);
        }
    }

    private async Task AddClipsAsync()
    {
        var selectedFiles = _fileDialogService.PickClipFiles();
        if (selectedFiles.Count == 0)
        {
            return;
        }

        foreach (var path in selectedFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var clip = new ClipItemViewModel(
                Guid.NewGuid().ToString("N"),
                Path.GetFileNameWithoutExtension(path),
                path,
                1.0f,
                null,
                OnClipChanged);

            await ReloadClipAsync(clip);
            Clips.Add(clip);
        }

        ApplyHotkeyBindings();
        ScheduleSave();
    }

    private async Task ReloadClipAsync(ClipItemViewModel clip)
    {
        var loadResult = await Task.Run(() => _clipLoaderService.LoadClip(clip.SourcePath));
        clip.ApplyLoadResult(loadResult.Clip, loadResult.IsAvailable, loadResult.AvailabilityText);
        ClipBrowserClips.Refresh();
    }

    private async Task PlaySelectedClipAsync()
    {
        await PlayClipAsync(SelectedClip);
    }

    private async Task PlayClipAsync(ClipItemViewModel? clip)
    {
        if (clip is null)
        {
            return;
        }

        if (!clip.IsAvailable || clip.LoadedClip is null)
        {
            await ReloadClipAsync(clip);
        }

        if (clip.LoadedClip is null)
        {
            _logService.Warning($"Clip '{clip.DisplayName}' is unavailable.");
            return;
        }

        if (clip.IsPlaying)
        {
            StopClipPlayback(clip);
        }

        _audioEngineService.PlayClip(clip.LoadedClip, (float)(clip.VolumePercent / 100.0));
        StartClipPlayback(clip);
    }

    private async Task ToggleClipPlaybackAsync(ClipItemViewModel? clip)
    {
        if (clip is null)
        {
            return;
        }

        if (clip.IsPlaying)
        {
            StopClipPlayback(clip);
            return;
        }

        await PlayClipAsync(clip);
    }

    private void RemoveSelectedClip()
    {
        RemoveClip(SelectedClip);
    }

    private void RemoveClip(ClipItemViewModel? clip)
    {
        if (clip is null)
        {
            return;
        }

        StopClipPlayback(clip);

        if (ReferenceEquals(SelectedClip, clip))
        {
            SelectedClip = null;
        }

        Clips.Remove(clip);
        ClipBrowserClips.Refresh();
        ApplyHotkeyBindings();
        ScheduleSave();
    }

    private void OnClipBrowserFilter(object sender, FilterEventArgs eventArgs)
    {
        if (eventArgs.Item is not ClipItemViewModel clip)
        {
            eventArgs.Accepted = false;
            return;
        }

        var searchText = ClipBrowserSearchText.Trim();
        if (searchText.Length == 0)
        {
            eventArgs.Accepted = true;
            return;
        }

        eventArgs.Accepted =
            clip.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            clip.SourcePath.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            clip.AvailabilityText.Contains(searchText, StringComparison.OrdinalIgnoreCase);
    }

    private void StopAllClips()
    {
        _audioEngineService.StopAllClips();
        ClearAllClipPlayback();
    }

    private void RefreshDevices()
    {
        try
        {
            var preferredMicrophoneId = SelectedMicrophone?.Id ?? _settings.SelectedMicrophoneId;
            var preferredOutputId = SelectedOutput?.Id ?? _settings.SelectedOutputDeviceId;
            var preferredSpeakerId = SelectedSpeakerOutput?.Id ?? _settings.SelectedSpeakerDeviceId;

            var microphones = _audioEngineService.GetCaptureDevices();
            var outputs = _audioEngineService.GetRenderDevices();

            _isRefreshingDevices = true;

            try
            {
                ReplaceCollection(MicrophoneDevices, microphones);
                ReplaceCollection(OutputDevices, outputs);

                SelectedMicrophone = ResolveDeviceSelection(
                    MicrophoneDevices,
                    preferredMicrophoneId,
                    "microphone");

                SelectedOutput = ResolveDeviceSelection(
                    OutputDevices,
                    preferredOutputId,
                    "mixed output");

                SelectedSpeakerOutput = ResolveDeviceSelection(
                    OutputDevices,
                    preferredSpeakerId,
                    "speaker monitor",
                    SelectedOutput?.Id);
            }
            finally
            {
                _isRefreshingDevices = false;
            }

            if (!_isInitializing)
            {
                RestartAudio();
                ScheduleSave();
            }
        }
        catch (Exception exception)
        {
            EngineStatus = "Audio device refresh failed. See the log for details.";
            _logService.Error("Failed to refresh audio devices.", exception);
        }
    }

    private AudioDeviceInfo? ResolveDeviceSelection(
        ObservableCollection<AudioDeviceInfo> devices,
        string? preferredDeviceId,
        string deviceLabel,
        string? excludedDeviceId = null)
    {
        if (devices.Count == 0)
        {
            _logService.Warning($"No {deviceLabel} devices are currently available.");
            return null;
        }

        if (!string.IsNullOrWhiteSpace(preferredDeviceId))
        {
            var savedDevice = devices.FirstOrDefault(device =>
                string.Equals(device.Id, preferredDeviceId, StringComparison.OrdinalIgnoreCase));

            if (savedDevice is not null)
            {
                return savedDevice;
            }

            _logService.Warning($"Saved {deviceLabel} device was not found. Falling back to '{devices[0].DisplayName}'.");
        }

        return devices.FirstOrDefault(device =>
                   !string.Equals(device.Id, excludedDeviceId, StringComparison.OrdinalIgnoreCase))
               ?? devices[0];
    }

    private void RestartAudio()
    {
        try
        {
            ClearAllClipPlayback();
            ApplyMixSettings();
            _audioEngineService.Start(
                SelectedMicrophone?.Id,
                SelectedOutput?.Id,
                SelectedSpeakerOutput?.Id,
                IsSpeakerMonitorEnabled);
        }
        catch (Exception exception)
        {
            EngineStatus = "Audio restart failed. See the log for details.";
            _logService.Error("Failed to restart audio.", exception);
        }
    }

    private void ApplyMixSettings()
    {
        _audioEngineService.UpdateMixSettings(
            (float)(MicrophoneVolumePercent / 100.0),
            (float)(SoundboardVolumePercent / 100.0),
            IsMicrophoneMuted);
    }

    private void OnSelectedDeviceChanged()
    {
        if (_isInitializing || _isRefreshingDevices)
        {
            return;
        }

        RestartAudio();
        ScheduleSave();
    }

    private void ApplyHotkeyBindings()
    {
        var bindings = Clips
            .Select(clip => new HotkeyBindingDefinition(clip.Id, clip.HotkeyText))
            .Append(new HotkeyBindingDefinition(StopAllHotkeyId, StopAllHotkeyText));

        var statuses = _globalHotkeyService.UpdateBindings(bindings);

        foreach (var clip in Clips)
        {
            clip.SetHotkeyStatus(statuses.TryGetValue(clip.Id, out var status) ? status : string.Empty);
        }

        StopAllHotkeyStatus = statuses.TryGetValue(StopAllHotkeyId, out var stopAllStatus) ? stopAllStatus : string.Empty;
    }

    private void OnClipChanged(ClipItemViewModel clip, string propertyName)
    {
        if (_isInitializing)
        {
            return;
        }

        if (propertyName == nameof(ClipItemViewModel.HotkeyText))
        {
            ApplyHotkeyBindings();
        }

        if (propertyName == nameof(ClipItemViewModel.DisplayName))
        {
            ClipBrowserClips.Refresh();
        }

        ScheduleSave();
    }

    private void ScheduleSave()
    {
        if (_isInitializing)
        {
            return;
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private async void OnSaveTimerTick(object? sender, EventArgs eventArgs)
    {
        _saveTimer.Stop();

        try
        {
            await SaveSettingsAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logService.Error("Failed to save delayed settings update.", exception);
        }
    }

    private void OnPlaybackTimerTick(object? sender, EventArgs eventArgs)
    {
        if (_playbackStates.Count == 0)
        {
            _playbackTimer.Stop();
            return;
        }

        var now = Stopwatch.GetTimestamp();
        foreach (var state in _playbackStates.Values.ToList())
        {
            var elapsedSeconds = (now - state.StartTimestamp) / (double)Stopwatch.Frequency;
            var progress = state.Duration.TotalSeconds <= 0.0
                ? 1.0
                : elapsedSeconds / state.Duration.TotalSeconds;

            if (progress >= 1.0)
            {
                ClearClipPlayback(state.Clip);
                continue;
            }

            state.Clip.UpdatePlaybackProgress(progress);
        }
    }

    private void StartClipPlayback(ClipItemViewModel clip)
    {
        var duration = clip.LoadedClip?.Duration ?? TimeSpan.Zero;
        _playbackStates[clip.Id] = new ClipPlaybackUiState(clip, Stopwatch.GetTimestamp(), duration);
        clip.StartPlayback();

        if (!_playbackTimer.IsEnabled)
        {
            _playbackTimer.Start();
        }
    }

    private void StopClipPlayback(ClipItemViewModel clip)
    {
        if (clip.LoadedClip is not null)
        {
            _audioEngineService.StopClip(clip.LoadedClip);
        }

        ClearClipPlayback(clip);
    }

    private void ClearClipPlayback(ClipItemViewModel clip)
    {
        _playbackStates.Remove(clip.Id);
        clip.StopPlayback();

        if (_playbackStates.Count == 0)
        {
            _playbackTimer.Stop();
        }
    }

    private void ClearAllClipPlayback()
    {
        foreach (var state in _playbackStates.Values.ToList())
        {
            state.Clip.StopPlayback();
        }

        _playbackStates.Clear();
        _playbackTimer.Stop();
    }

    private async Task SaveSettingsAsync()
    {
        var snapshot = BuildSettingsSnapshot();
        ApplySettingsSnapshot(snapshot);
        await _settingsService.SaveAsync(snapshot).ConfigureAwait(false);
    }

    private AppSettings BuildSettingsSnapshot()
    {
        return new AppSettings
        {
            SelectedMicrophoneId = SelectedMicrophone?.Id,
            SelectedOutputDeviceId = SelectedOutput?.Id,
            SelectedSpeakerDeviceId = SelectedSpeakerOutput?.Id,
            IsSpeakerMonitorEnabled = IsSpeakerMonitorEnabled,
            MicrophoneVolume = (float)(MicrophoneVolumePercent / 100.0),
            SoundboardVolume = (float)(SoundboardVolumePercent / 100.0),
            IsMicrophoneMuted = IsMicrophoneMuted,
            AutoStartOnWindowsStart = IsAutoStartOnWindowsStart,
            MinimizeToSystemTrayOnClose = IsMinimizeToSystemTrayOnClose,
            StopAllHotkeyText = string.IsNullOrWhiteSpace(StopAllHotkeyText) ? null : StopAllHotkeyText.Trim(),
            Clips = Clips.Select(clip => clip.ToSettings()).ToList(),
            Window = new WindowSettings
            {
                Width = _settings.Window.Width,
                Height = _settings.Window.Height,
                Left = _settings.Window.Left,
                Top = _settings.Window.Top,
                IsMaximized = _settings.Window.IsMaximized
            }
        };
    }

    private void ApplySettingsSnapshot(AppSettings snapshot)
    {
        _settings.SelectedMicrophoneId = snapshot.SelectedMicrophoneId;
        _settings.SelectedOutputDeviceId = snapshot.SelectedOutputDeviceId;
        _settings.SelectedSpeakerDeviceId = snapshot.SelectedSpeakerDeviceId;
        _settings.IsSpeakerMonitorEnabled = snapshot.IsSpeakerMonitorEnabled;
        _settings.MicrophoneVolume = snapshot.MicrophoneVolume;
        _settings.SoundboardVolume = snapshot.SoundboardVolume;
        _settings.IsMicrophoneMuted = snapshot.IsMicrophoneMuted;
        _settings.AutoStartOnWindowsStart = snapshot.AutoStartOnWindowsStart;
        _settings.MinimizeToSystemTrayOnClose = snapshot.MinimizeToSystemTrayOnClose;
        _settings.StopAllHotkeyText = snapshot.StopAllHotkeyText;
        _settings.Clips = snapshot.Clips;
    }

    private void OpenClipBrowser()
    {
        IsSettingsOpen = false;
        IsClipBrowserOpen = true;
    }

    private void OpenSettings()
    {
        IsClipBrowserOpen = false;
        IsSettingsOpen = true;
    }

    private void EnsureStartupRegistration()
    {
        if (IsAutoStartOnWindowsStart && !ApplyAutoStartSetting(enabled: true))
        {
            _isAutoStartOnWindowsStart = false;
            OnPropertyChanged(nameof(IsAutoStartOnWindowsStart));
        }
    }

    private bool ApplyAutoStartSetting(bool enabled)
    {
        if (_startupRegistrationService.SetAutoStartEnabled(enabled, out var errorMessage))
        {
            return true;
        }

        _logService.Warning(
            enabled
                ? $"Failed to enable Windows startup. {errorMessage}"
                : $"Failed to disable Windows startup. {errorMessage}");
        return false;
    }

    private void OnAudioEngineStatusChanged(object? sender, string status)
    {
        DispatchToUi(() => EngineStatus = status, "Failed to update audio status.");
    }

    private void OnHotkeyPressed(object? sender, string bindingId)
    {
        DispatchToUiAsync(async () =>
        {
            if (string.Equals(bindingId, StopAllHotkeyId, StringComparison.Ordinal))
            {
                StopAllClips();
                return;
            }

            var clip = Clips.FirstOrDefault(candidate => candidate.Id == bindingId);
            if (clip is not null)
            {
                await PlayClipAsync(clip);
            }
        }, $"Failed to handle hotkey '{bindingId}'.");
    }

    private void OnLogEntryLogged(object? sender, LogEntry entry)
    {
        DispatchToUi(() =>
        {
            LogEntries.Insert(0, entry);

            while (LogEntries.Count > 200)
            {
                LogEntries.RemoveAt(LogEntries.Count - 1);
            }
        }, failureMessage: null);
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();

        foreach (var item in items)
        {
            collection.Add(item);
        }
    }

    private void VerifyUiAccess()
    {
        if (!_uiDispatcher.CheckAccess())
        {
            throw new InvalidOperationException("MainWindowViewModel shutdown must run on the UI dispatcher.");
        }
    }

    private void DispatchToUi(Action action, string? failureMessage)
    {
        DispatchToUiAsync(
            () =>
            {
                action();
                return Task.CompletedTask;
            },
            failureMessage);
    }

    private void DispatchToUiAsync(Func<Task> action, string? failureMessage)
    {
        if (_uiDispatcher.HasShutdownStarted || _uiDispatcher.HasShutdownFinished)
        {
            return;
        }

        if (_uiDispatcher.CheckAccess())
        {
            _ = ExecuteUiActionAsync(action, failureMessage);
            return;
        }

        try
        {
            _uiDispatcher.BeginInvoke(
                new Action(() => _ = ExecuteUiActionAsync(action, failureMessage)),
                DispatcherPriority.Background);
        }
        catch (InvalidOperationException)
        {
            // The dispatcher is already shutting down.
        }
        catch (TaskCanceledException)
        {
            // The dispatcher is already shutting down.
        }
    }

    private static bool IsWindowPlacementVisible(Rect bounds)
    {
        if (!IsFinite(bounds.Left) ||
            !IsFinite(bounds.Top) ||
            !IsFinite(bounds.Width) ||
            !IsFinite(bounds.Height) ||
            bounds.Width <= 0 ||
            bounds.Height <= 0)
        {
            return false;
        }

        var desktopBounds = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

        var visibleBounds = Rect.Intersect(bounds, desktopBounds);
        if (visibleBounds.IsEmpty)
        {
            return false;
        }

        return visibleBounds.Width >= Math.Min(80.0, bounds.Width) &&
               visibleBounds.Height >= Math.Min(80.0, bounds.Height);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private async Task ExecuteUiActionAsync(Func<Task> action, string? failureMessage)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            if (!string.IsNullOrWhiteSpace(failureMessage))
            {
                _logService.Error(failureMessage, exception);
            }
        }
    }

    private sealed record ClipPlaybackUiState(
        ClipItemViewModel Clip,
        long StartTimestamp,
        TimeSpan Duration);
}
