using System.Diagnostics;
using NAudio.Dmo;
using NAudio.Wave;

namespace SoundboardMixer.App.Services.Audio;

internal sealed class DeviceOutputWaveProvider : IWaveProvider
{
    private static readonly TimeSpan WarningInterval = TimeSpan.FromSeconds(5);

    private readonly object _readLock = new();
    private readonly ISampleProvider _source;
    private readonly OutputSampleEncoding _encoding;
    private readonly int _bytesPerSample;
    private readonly string _outputName;
    private readonly ThrottledAudioLogger? _timingLogger;
    private readonly ThrottledAudioLogger? _underrunLogger;
    private float[] _sampleBuffer = [];

    public DeviceOutputWaveProvider(
        ISampleProvider source,
        WaveFormat waveFormat,
        ILogService? logService = null,
        string outputName = "output")
    {
        _source = source;
        WaveFormat = waveFormat;
        _encoding = ResolveEncoding(waveFormat);
        _bytesPerSample = waveFormat.BitsPerSample / 8;
        _outputName = outputName;

        if (_bytesPerSample <= 0)
        {
            throw new NotSupportedException($"Unsupported output sample size: {waveFormat.BitsPerSample} bit.");
        }

        if (logService is not null)
        {
            _timingLogger = new ThrottledAudioLogger(logService, WarningInterval);
            _underrunLogger = new ThrottledAudioLogger(logService, WarningInterval);
        }
    }

    public WaveFormat WaveFormat { get; }

    public string EncodingDescription => _encoding switch
    {
        OutputSampleEncoding.Float32 => "32-bit float",
        OutputSampleEncoding.Pcm16 => "16-bit PCM",
        OutputSampleEncoding.Pcm24 => "24-bit PCM",
        OutputSampleEncoding.Pcm32 => "32-bit PCM",
        _ => _encoding.ToString()
    };

    public int Read(byte[] buffer, int offset, int count)
    {
        var bytesToWrite = count - (count % WaveFormat.BlockAlign);
        if (bytesToWrite <= 0)
        {
            return 0;
        }

        var startedAt = Stopwatch.GetTimestamp();
        var samplesRequested = bytesToWrite / _bytesPerSample;
        int samplesRead;

        lock (_readLock)
        {
            EnsureSampleBuffer(samplesRequested);
            samplesRead = Math.Clamp(_source.Read(_sampleBuffer, 0, samplesRequested), 0, samplesRequested);

            switch (_encoding)
            {
                case OutputSampleEncoding.Float32:
                    Buffer.BlockCopy(_sampleBuffer, 0, buffer, offset, samplesRead * sizeof(float));
                    break;

                case OutputSampleEncoding.Pcm16:
                    WritePcm16(_sampleBuffer, samplesRead, buffer, offset);
                    break;

                case OutputSampleEncoding.Pcm24:
                    WritePcm24(_sampleBuffer, samplesRead, buffer, offset);
                    break;

                case OutputSampleEncoding.Pcm32:
                    WritePcm32(_sampleBuffer, samplesRead, buffer, offset);
                    break;
            }

            var bytesWritten = samplesRead * _bytesPerSample;
            if (bytesWritten < bytesToWrite)
            {
                Array.Clear(buffer, offset + bytesWritten, bytesToWrite - bytesWritten);
            }
        }

        if (samplesRead < samplesRequested)
        {
            LogUnderrun(samplesRead, samplesRequested, bytesToWrite);
        }

        LogSlowCallback(startedAt, bytesToWrite);
        return bytesToWrite;
    }

    private void EnsureSampleBuffer(int samplesRequested)
    {
        if (_sampleBuffer.Length < samplesRequested)
        {
            _sampleBuffer = new float[samplesRequested];
        }
    }

    private void LogUnderrun(int samplesRead, int samplesRequested, int bytesToWrite)
    {
        _underrunLogger?.Warning(
            $"{_outputName} render underrun: source produced {samplesRead}/{samplesRequested} samples for a {FormatBufferDuration(bytesToWrite)} device buffer.");
    }

    private void LogSlowCallback(long startedAt, int bytesToWrite)
    {
        if (_timingLogger is null)
        {
            return;
        }

        var elapsedMilliseconds = (Stopwatch.GetTimestamp() - startedAt) * 1000.0 / Stopwatch.Frequency;
        var bufferMilliseconds = GetBufferMilliseconds(bytesToWrite);
        var warningThresholdMilliseconds = Math.Max(2.0, bufferMilliseconds * 0.75);

        if (elapsedMilliseconds > warningThresholdMilliseconds)
        {
            _timingLogger.Warning(
                $"{_outputName} render callback took {elapsedMilliseconds:F1} ms for a {bufferMilliseconds:F1} ms device buffer.");
        }
    }

    private string FormatBufferDuration(int bytesToWrite) => $"{GetBufferMilliseconds(bytesToWrite):F1} ms";

    private double GetBufferMilliseconds(int bytesToWrite)
    {
        return WaveFormat.AverageBytesPerSecond > 0
            ? bytesToWrite * 1000.0 / WaveFormat.AverageBytesPerSecond
            : 0.0;
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
