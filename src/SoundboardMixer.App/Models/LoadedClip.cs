namespace SoundboardMixer.App.Models;

/// <summary>
/// Represents a sound clip that has already been decoded into the app's in-memory mix format.
/// </summary>
public sealed class LoadedClip
{
    internal LoadedClip(string sourcePath, float[] sampleBuffer, TimeSpan duration)
    {
        SourcePath = sourcePath;
        SampleBuffer = sampleBuffer;
        Duration = duration;
    }

    public string SourcePath { get; }

    public TimeSpan Duration { get; }

    internal float[] SampleBuffer { get; }
}
