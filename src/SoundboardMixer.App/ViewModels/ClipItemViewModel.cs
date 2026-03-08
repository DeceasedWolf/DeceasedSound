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

    public ClipItemViewModel(
        string id,
        string displayName,
        string sourcePath,
        string? hotkeyText,
        Action<ClipItemViewModel, string> changeCallback)
    {
        Id = id;
        _displayName = displayName;
        SourcePath = sourcePath;
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

    public ClipSettings ToSettings()
    {
        return new ClipSettings
        {
            Id = Id,
            DisplayName = string.IsNullOrWhiteSpace(DisplayName)
                ? Path.GetFileNameWithoutExtension(SourcePath)
                : DisplayName.Trim(),
            SourcePath = SourcePath,
            HotkeyText = string.IsNullOrWhiteSpace(HotkeyText) ? null : HotkeyText.Trim()
        };
    }
}
