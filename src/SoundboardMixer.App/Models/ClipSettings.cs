namespace SoundboardMixer.App.Models;

/// <summary>
/// Stores the persisted metadata for an imported soundboard clip.
/// </summary>
public sealed class ClipSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string DisplayName { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;

    public float Volume { get; set; } = 1.0f;

    public string? HotkeyText { get; set; }
}
