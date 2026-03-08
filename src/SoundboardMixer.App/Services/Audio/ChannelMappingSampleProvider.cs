using NAudio.Wave;

namespace SoundboardMixer.App.Services.Audio;

internal sealed class ChannelMappingSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _sourceChannels;
    private readonly int _targetChannels;
    private float[] _sourceBuffer = [];

    public ChannelMappingSampleProvider(ISampleProvider source, int targetChannels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetChannels);

        _source = source;
        _sourceChannels = source.WaveFormat.Channels;
        _targetChannels = targetChannels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, targetChannels);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var framesRequested = count / _targetChannels;
        var sourceSamplesNeeded = framesRequested * _sourceChannels;

        if (_sourceBuffer.Length < sourceSamplesNeeded)
        {
            _sourceBuffer = new float[sourceSamplesNeeded];
        }

        Array.Clear(_sourceBuffer, 0, sourceSamplesNeeded);
        Array.Clear(buffer, offset, framesRequested * _targetChannels);

        var samplesRead = _source.Read(_sourceBuffer, 0, sourceSamplesNeeded);
        var framesRead = samplesRead / _sourceChannels;

        for (var frame = 0; frame < framesRead; frame++)
        {
            var sourceIndex = frame * _sourceChannels;
            var targetIndex = offset + (frame * _targetChannels);

            if (_targetChannels == 1)
            {
                var sum = 0.0f;
                for (var sourceChannel = 0; sourceChannel < _sourceChannels; sourceChannel++)
                {
                    sum += _sourceBuffer[sourceIndex + sourceChannel];
                }

                buffer[targetIndex] = sum / _sourceChannels;
                continue;
            }

            if (_sourceChannels == 1)
            {
                var sample = _sourceBuffer[sourceIndex];
                for (var targetChannel = 0; targetChannel < _targetChannels; targetChannel++)
                {
                    buffer[targetIndex + targetChannel] = sample;
                }

                continue;
            }

            var channelsToCopy = Math.Min(_sourceChannels, _targetChannels);
            for (var channel = 0; channel < channelsToCopy; channel++)
            {
                buffer[targetIndex + channel] = _sourceBuffer[sourceIndex + channel];
            }
        }

        return framesRead * _targetChannels;
    }
}
