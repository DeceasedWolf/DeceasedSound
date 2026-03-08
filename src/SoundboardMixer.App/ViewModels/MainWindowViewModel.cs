using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
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
    private readonly AppSettings _settings;
    private readonly ISettingsService _settingsService;
    private readonly IFileDialogService _fileDialogService;
    private readonly ClipLoaderService _clipLoaderService;
    private readonly IAudioEngineService _audioEngineService;
    private readonly IGlobalHotkeyService _globalHotkeyService;
    private readonly ILogService _logService;
    private readonly DispatcherTimer _saveTimer;

    private AudioDeviceInfo? _selectedMicrophone;
    private AudioDeviceInfo? _selectedOutput;
    private AudioDeviceInfo? _selectedSpeakerOutput;
    private ClipItemViewModel? _selectedClip;
    private string _engineStatus = "Initializing audio...";
    private double _microphoneVolumePercent;
    private double _soundboardVolumePercent;
    private bool _isMicrophoneMuted;
    private bool _isSpeakerMonitorEnabled;
    private bool _isInitializing;
    private bool _isRefreshingDevices;

    public MainWindowViewModel(
        AppSettings settings,
        ISettingsService settingsService,
        IFileDialogService fileDialogService,
        ClipLoaderService clipLoaderService,
        IAudioEngineService audioEngineService,
        IGlobalHotkeyService globalHotkeyService,
        ILogService logService)
    {
        _settings = settings;
        _settingsService = settingsService;
        _fileDialogService = fileDialogService;
        _clipLoaderService = clipLoaderService;
        _audioEngineService = audioEngineService;
        _globalHotkeyService = globalHotkeyService;
        _logService = logService;

        _microphoneVolumePercent = settings.MicrophoneVolume * 100.0;
        _soundboardVolumePercent = settings.SoundboardVolume * 100.0;
        _isMicrophoneMuted = settings.IsMicrophoneMuted;
        _isSpeakerMonitorEnabled = settings.IsSpeakerMonitorEnabled;

        RefreshDevicesCommand = new RelayCommand(RefreshDevices);
        RestartAudioCommand = new RelayCommand(RestartAudio);
        AddClipsCommand = new AsyncRelayCommand(AddClipsAsync);
        StopAllCommand = new RelayCommand(StopAllClips);
        RemoveSelectedClipCommand = new RelayCommand(RemoveSelectedClip, () => SelectedClip is not null);
        PlaySelectedClipCommand = new AsyncRelayCommand(PlaySelectedClipAsync, () => SelectedClip is not null);
        RemoveClipCommand = new RelayCommand<ClipItemViewModel?>(RemoveClip);
        PlayClipCommand = new AsyncRelayCommand<ClipItemViewModel?>(PlayClipAsync);

        _audioEngineService.StatusChanged += OnAudioEngineStatusChanged;
        _globalHotkeyService.HotkeyPressed += OnHotkeyPressed;
        _logService.EntryLogged += OnLogEntryLogged;

        _saveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _saveTimer.Tick += OnSaveTimerTick;
    }

    public ObservableCollection<AudioDeviceInfo> MicrophoneDevices { get; } = [];

    public ObservableCollection<AudioDeviceInfo> OutputDevices { get; } = [];

    public ObservableCollection<ClipItemViewModel> Clips { get; } = [];

    public ObservableCollection<LogEntry> LogEntries { get; } = [];

    public RelayCommand RefreshDevicesCommand { get; }

    public RelayCommand RestartAudioCommand { get; }

    public AsyncRelayCommand AddClipsCommand { get; }

    public RelayCommand StopAllCommand { get; }

    public RelayCommand RemoveSelectedClipCommand { get; }

    public AsyncRelayCommand PlaySelectedClipCommand { get; }

    public RelayCommand<ClipItemViewModel?> RemoveClipCommand { get; }

    public AsyncRelayCommand<ClipItemViewModel?> PlayClipCommand { get; }

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

    public double MicrophoneVolumePercent
    {
        get => _microphoneVolumePercent;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 100.0);
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

        if (windowSettings.Left.HasValue)
        {
            window.Left = windowSettings.Left.Value;
        }

        if (windowSettings.Top.HasValue)
        {
            window.Top = windowSettings.Top.Value;
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

    public async Task ShutdownAsync()
    {
        _saveTimer.Stop();
        await SaveSettingsAsync().ConfigureAwait(false);

        _audioEngineService.StatusChanged -= OnAudioEngineStatusChanged;
        _globalHotkeyService.HotkeyPressed -= OnHotkeyPressed;
        _logService.EntryLogged -= OnLogEntryLogged;

        _audioEngineService.Stop();
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

        _audioEngineService.PlayClip(clip.LoadedClip, (float)(clip.VolumePercent / 100.0));
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

        if (ReferenceEquals(SelectedClip, clip))
        {
            SelectedClip = null;
        }

        Clips.Remove(clip);
        ApplyHotkeyBindings();
        ScheduleSave();
    }

    private void StopAllClips()
    {
        _audioEngineService.StopAllClips();
    }

    private void RefreshDevices()
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
        ApplyMixSettings();
        _audioEngineService.Start(
            SelectedMicrophone?.Id,
            SelectedOutput?.Id,
            SelectedSpeakerOutput?.Id,
            IsSpeakerMonitorEnabled);
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
        var statuses = _globalHotkeyService.UpdateBindings(Clips.Select(clip =>
            new HotkeyBindingDefinition(clip.Id, clip.HotkeyText)));

        foreach (var clip in Clips)
        {
            clip.SetHotkeyStatus(statuses.TryGetValue(clip.Id, out var status) ? status : string.Empty);
        }
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
        await SaveSettingsAsync().ConfigureAwait(false);
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
        _settings.Clips = snapshot.Clips;
    }

    private void OnAudioEngineStatusChanged(object? sender, string status)
    {
        DispatchToUi(() => EngineStatus = status);
    }

    private void OnHotkeyPressed(object? sender, string clipId)
    {
        DispatchToUi(async () =>
        {
            var clip = Clips.FirstOrDefault(candidate => candidate.Id == clipId);
            if (clip is not null)
            {
                await PlayClipAsync(clip);
            }
        });
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
        });
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();

        foreach (var item in items)
        {
            collection.Add(item);
        }
    }

    private static void DispatchToUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }
}
