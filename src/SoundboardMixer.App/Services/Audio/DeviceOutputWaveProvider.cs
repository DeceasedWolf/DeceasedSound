using System.Buffers;
using NAudio.Dmo;
using NAudio.Wave;

namespace SoundboardMixer.App.Services.Audio;

internal sealed class DeviceOutputWaveProvider : IWaveProvider
{
    private readonly ISampleProvider _source;
    private readonly OutputSampleEncoding _encoding;

    public DeviceOutputWaveProvider(ISampleProvider source, WaveFormat waveFormat)
    {
        _source = source;
        WaveFormat = waveFormat;
        _encoding = ResolveEncoding(waveFormat);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(byte[] buffer, int offset, int count)
    {
        var bytesToWrite = count - (count % WaveFormat.BlockAlign);
        if (bytesToWrite <= 0)
        {
            return 0;
        }

        Array.Clear(buffer, offset, bytesToWrite);

        var samplesRequested = bytesToWrite / (WaveFormat.BitsPerSample / 8);
        var sampleBuffer = ArrayPool<float>.Shared.Rent(samplesRequested);

        try
        {
            Array.Clear(sampleBuffer, 0, samplesRequested);
            var samplesRead = _source.Read(sampleBuffer, 0, samplesRequested);

            switch (_encoding)
            {
                case OutputSampleEncoding.Float32:
                    Buffer.BlockCopy(sampleBuffer, 0, buffer, offset, samplesRead * sizeof(float));
                    break;

                case OutputSampleEncoding.Pcm16:
                    WritePcm16(sampleBuffer, samplesRead, buffer, offset);
                    break;

                case OutputSampleEncoding.Pcm24:
                    WritePcm24(sampleBuffer, samplesRead, buffer, offset);
                    break;

                case OutputSampleEncoding.Pcm32:
                    WritePcm32(sampleBuffer, samplesRead, buffer, offset);
                    break;
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(sampleBuffer);
        }

        return bytesToWrite;
    }

    private static OutputSampleEncoding ResolveEncoding(WaveFormat waveFormat)
    {
        if (waveFormat.Encoding == WaveFormatEncoding.IeeeFloat && waveFormat.BitsPerSample == 32)
        {
            return OutputSampleEncoding.Float32;
        }

        if (waveFormat.Encoding == WaveFormatEncoding.Pcm)
        {
            return waveFormat.BitsPerSample switch
            {
                16 => OutputSampleEncoding.Pcm16,
                24 => OutputSampleEncoding.Pcm24,
                32 => OutputSampleEncoding.Pcm32,
                _ => throw new NotSupportedException($"Unsupported PCM output depth: {waveFormat.BitsPerSample} bit.")
            };
        }

        if (waveFormat is WaveFormatExtensible extensible)
        {
            if (extensible.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT && waveFormat.BitsPerSample == 32)
            {
                return OutputSampleEncoding.Float32;
            }

            if (extensible.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_PCM)
            {
                return waveFormat.BitsPerSample switch
                {
                    16 => OutputSampleEncoding.Pcm16,
                    24 => OutputSampleEncoding.Pcm24,
                    32 => OutputSampleEncoding.Pcm32,
                    _ => throw new NotSupportedException($"Unsupported extensible PCM depth: {waveFormat.BitsPerSample} bit.")
                };
            }
        }

        throw new NotSupportedException(
            $"Unsupported output format: {waveFormat.Encoding}, {waveFormat.BitsPerSample} bit, {waveFormat.Channels} channel.");
    }

    private static void WritePcm16(float[] sampleBuffer, int samplesRead, byte[] buffer, int offset)
    {
        var bufferIndex = offset;
        for (var index = 0; index < samplesRead; index++)
        {
            var sample = ClampSample(sampleBuffer[index]);
            var value = (short)Math.Round(sample * short.MaxValue);
            buffer[bufferIndex++] = (byte)(value & 0xFF);
            buffer[bufferIndex++] = (byte)((value >> 8) & 0xFF);
        }
    }

    private static void WritePcm24(float[] sampleBuffer, int samplesRead, byte[] buffer, int offset)
    {
        var bufferIndex = offset;
        for (var index = 0; index < samplesRead; index++)
        {
            var sample = ClampSample(sampleBuffer[index]);
            var value = (int)Math.Round(sample * 8_388_607.0f);
            buffer[bufferIndex++] = (byte)(value & 0xFF);
            buffer[bufferIndex++] = (byte)((value >> 8) & 0xFF);
            buffer[bufferIndex++] = (byte)((value >> 16) & 0xFF);
        }
    }

    private static void WritePcm32(float[] sampleBuffer, int samplesRead, byte[] buffer, int offset)
    {
        var bufferIndex = offset;
        for (var index = 0; index < samplesRead; index++)
        {
            var sample = ClampSample(sampleBuffer[index]);
            var value = (int)Math.Round(sample * int.MaxValue);
            buffer[bufferIndex++] = (byte)(value & 0xFF);
            buffer[bufferIndex++] = (byte)((value >> 8) & 0xFF);
            buffer[bufferIndex++] = (byte)((value >> 16) & 0xFF);
            buffer[bufferIndex++] = (byte)((value >> 24) & 0xFF);
        }
    }

    private static float ClampSample(float sample) => Math.Clamp(sample, -1.0f, 1.0f);

    private enum OutputSampleEncoding
    {
        Float32,
        Pcm16,
        Pcm24,
        Pcm32
    }
}
