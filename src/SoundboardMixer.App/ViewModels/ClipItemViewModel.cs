using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;
using SoundboardMixer.App.Models;

namespace SoundboardMixer.App.ViewModels;

internal sealed class ClipItemViewModel : ObservableObject
{
    private readonly Action<ClipItemViewModel, string> _changeCallback;
    private string _displayName;
    private string? _hotkeyText;
    private string _hotkeyStatus = string.Empty;
    private string _availabilityText = "Not loaded";
    private bool _isAvailable;
    private bool _isPlaying;
    private double _playbackProgress;
    private double _volumePercent;

    public ClipItemViewModel(
        string id,
        string displayName,
        string sourcePath,
        float volume,
        string? hotkeyText,
        Action<ClipItemViewModel, string> changeCallback)
    {
        Id = id;
        _displayName = displayName;
        SourcePath = sourcePath;
        _volumePercent = Math.Clamp(volume, 0.0f, 1.0f) * 100.0;
        _hotkeyText = hotkeyText;
        _changeCallback = changeCallback;
    }

    public string Id { get; }

    public string SourcePath { get; }

    public string DisplayName
    {
        get => _displayName;
        set
        {
            var nextValue = string.IsNullOrWhiteSpace(value)
                ? Path.GetFileNameWithoutExtension(SourcePath)
                : value.Trim();

            if (SetProperty(ref _displayName, nextValue))
            {
                _changeCallback(this, nameof(DisplayName));
            }
        }
    }

    public string? HotkeyText
    {
        get => _hotkeyText;
        set
        {
            var nextValue = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (SetProperty(ref _hotkeyText, nextValue))
            {
                _changeCallback(this, nameof(HotkeyText));
            }
        }
    }

    public double VolumePercent
    {
        get => _volumePercent;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 100.0);
            if (SetProperty(ref _volumePercent, clamped))
            {
                _changeCallback(this, nameof(VolumePercent));
            }
        }
    }

    public string HotkeyStatus
    {
        get => _hotkeyStatus;
        private set => SetProperty(ref _hotkeyStatus, value);
    }

    public string AvailabilityText
    {
        get => _availabilityText;
        private set => SetProperty(ref _availabilityText, value);
    }

    public bool IsAvailable
    {
        get => _isAvailable;
        private set => SetProperty(ref _isAvailable, value);
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set => SetProperty(ref _isPlaying, value);
    }

    public double PlaybackProgress
    {
        get => _playbackProgress;
        private set => SetProperty(ref _playbackProgress, value);
    }

    internal LoadedClip? LoadedClip { get; private set; }

    public void ApplyLoadResult(LoadedClip? loadedClip, bool isAvailable, string availabilityText)
    {
        LoadedClip = loadedClip;
        IsAvailable = isAvailable;
        AvailabilityText = availabilityText;
    }

    public void SetHotkeyStatus(string status)
    {
        HotkeyStatus = status;
    }

    public void StartPlayback()
    {
        PlaybackProgress = 0.0;
        IsPlaying = true;
    }

    public void UpdatePlaybackProgress(double progress)
    {
        PlaybackProgress = Math.Clamp(progress, 0.0, 1.0);
    }

    public void StopPlayback()
    {
        IsPlaying = false;
        PlaybackProgress = 0.0;
    }

    public ClipSettings ToSettings()
    {
        return new ClipSettings
        {
            Id = Id,
            DisplayName = string.IsNullOrWhiteSpace(DisplayName)
                ? Path.GetFileNameWithoutExtension(SourcePath)
                : DisplayName.Trim(),
            SourcePath = SourcePath,
            Volume = (float)(VolumePercent / 100.0),
            HotkeyText = string.IsNullOrWhiteSpace(HotkeyText) ? null : HotkeyText.Trim()
        };
    }
}
