namespace SoundboardMixer.App.Models;

/// <summary>
/// Represents an audio endpoint that can be selected by the user.
/// </summary>
public sealed class AudioDeviceInfo
{
    public AudioDeviceInfo(string id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public override string ToString() => DisplayName;
}
