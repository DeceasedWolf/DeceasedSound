using SoundboardMixer.App.Models;

namespace SoundboardMixer.App.Services.Audio;

/// <summary>
/// Owns microphone capture, clip mixing, and playback routing for the application.
/// </summary>
public interface IAudioEngineService : IDisposable
{
    event EventHandler<string>? StatusChanged;

    IReadOnlyList<AudioDeviceInfo> GetCaptureDevices();

    IReadOnlyList<AudioDeviceInfo> GetRenderDevices();

    void Start(string? microphoneDeviceId, string? outputDeviceId);

    void Stop();

    void PlayClip(LoadedClip clip);

    void StopAllClips();

    void UpdateMixSettings(float microphoneVolume, float soundboardVolume, bool microphoneMuted);
}
