namespace SoundboardMixer.App.Models;

/// <summary>
/// Stores the persisted user configuration for the application.
/// </summary>
public sealed class AppSettings
{
    public string? SelectedMicrophoneId { get; set; }

    public string? SelectedOutputDeviceId { get; set; }

    public string? SelectedSpeakerDeviceId { get; set; }

    public bool IsSpeakerMonitorEnabled { get; set; } = true;

    public float MicrophoneVolume { get; set; } = 1.0f;

    public float SoundboardVolume { get; set; } = 1.0f;

    public bool IsMicrophoneMuted { get; set; }

    public bool AutoStartOnWindowsStart { get; set; }

    public bool MinimizeToSystemTrayOnClose { get; set; }

    public string? StopAllHotkeyText { get; set; }

    public List<ClipSettings> Clips { get; set; } = [];

    public WindowSettings Window { get; set; } = new();
}
