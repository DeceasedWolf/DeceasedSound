using NAudio.Wave;

namespace SoundboardMixer.App.Tests.Support;

internal sealed class ArraySampleProvider : ISampleProvider
{
    private readonly float[] _samples;
    private int _position;

    public ArraySampleProvider(float[] samples, int sampleRate = 48_000, int channels = 1)
    {
        _samples = samples;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var samplesAvailable = Math.Max(0, _samples.Length - _position);
        var samplesToCopy = Math.Min(count, samplesAvailable);

        Array.Copy(_samples, _position, buffer, offset, samplesToCopy);
        _position += samplesToCopy;

        return samplesToCopy;
    }
}
