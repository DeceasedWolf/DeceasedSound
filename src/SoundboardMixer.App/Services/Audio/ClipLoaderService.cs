using NAudio.Wave;
using System.Globalization;
using System.IO;
using SoundboardMixer.App.Models;

namespace SoundboardMixer.App.Services.Audio;

internal sealed class ClipLoaderService
{
    private const int BytesPerFloatSample = 4;
    private const int MaxClipDurationMinutes = 5;
    private const int MaxDecodedClipMiB = 128;
    private const int MaxDecodedClipBytes = MaxDecodedClipMiB * 1024 * 1024;
    private const int InternalSampleCountPerSecond = AudioEngineService.InternalSampleRate * AudioEngineService.InternalChannels;
    private const int ReadBufferSampleCount = InternalSampleCountPerSecond;
    private const int MaxDecodedClipSamples = MaxDecodedClipBytes / BytesPerFloatSample;
    private const int MaxClipDurationSamples = MaxClipDurationMinutes * 60 * InternalSampleCountPerSecond;
    private const int MaxAllowedClipSamples = MaxClipDurationSamples < MaxDecodedClipSamples
        ? MaxClipDurationSamples
        : MaxDecodedClipSamples;

    private static readonly TimeSpan MaxClipDuration = TimeSpan.FromMinutes(MaxClipDurationMinutes);

    private readonly ILogService _logService;

    public ClipLoaderService(ILogService logService)
    {
        _logService = logService;
    }

    public ClipLoadResult LoadClip(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new ClipLoadResult(null, false, "Missing file");
        }

        try
        {
            using var reader = new AudioFileReader(path);

            if (TryCreateUnavailableResultForKnownDuration(path, reader.TotalTime, out var unavailableResult))
            {
                return unavailableResult;
            }

            var sampleProvider = AudioSampleProviderUtilities.ConvertToInternalMixFormat(reader);
            var readBuffer = new float[ReadBufferSampleCount];
            var sampleBuffer = CreateInitialSampleBuffer(reader.TotalTime);
            var samplesWritten = 0;

            int samplesRead;
            while ((samplesRead = sampleProvider.Read(readBuffer, 0, readBuffer.Length)) > 0)
            {
                var requiredSampleCount = samplesWritten + (long)samplesRead;

                if (TryCreateUnavailableResultForSampleCount(path, requiredSampleCount, out unavailableResult))
                {
                    return unavailableResult;
                }

                sampleBuffer = EnsureCapacity(sampleBuffer, (int)requiredSampleCount);
                readBuffer.AsSpan(0, samplesRead).CopyTo(sampleBuffer.AsSpan(samplesWritten, samplesRead));
                samplesWritten += samplesRead;
            }

            if (samplesWritten == 0)
            {
                return new ClipLoadResult(null, false, "Clip contained no audio");
            }

            if (sampleBuffer.Length != samplesWritten)
            {
                Array.Resize(ref sampleBuffer, samplesWritten);
            }

            var duration = TimeSpan.FromSeconds(samplesWritten / (double)InternalSampleCountPerSecond);
            var loadedClip = new LoadedClip(path, sampleBuffer, duration);
            return new ClipLoadResult(loadedClip, true, $"Ready ({duration:mm\\:ss})");
        }
        catch (Exception exception)
        {
            _logService.Warning($"Failed to load clip '{path}'. {exception.Message}");
            return new ClipLoadResult(null, false, "Load failed");
        }
    }

    internal sealed record ClipLoadResult(LoadedClip? Clip, bool IsAvailable, string AvailabilityText);

    private bool TryCreateUnavailableResultForKnownDuration(
        string path,
        TimeSpan sourceDuration,
        out ClipLoadResult unavailableResult)
    {
        unavailableResult = default!;

        if (!HasUsableDuration(sourceDuration))
        {
            return false;
        }

        if (sourceDuration > MaxClipDuration)
        {
            unavailableResult = RejectClip(
                $"Clip '{path}' is {FormatDuration(sourceDuration)} and exceeds the preload duration limit of {FormatDuration(MaxClipDuration)}.",
                $"Too long (max {FormatDuration(MaxClipDuration)})");
            return true;
        }

        if (!TryGetDecodedSampleCount(sourceDuration, out var expectedSamples) ||
            expectedSamples > MaxDecodedClipSamples)
        {
            unavailableResult = RejectClip(
                $"Clip '{path}' would exceed the preload decoded-size limit of {MaxDecodedClipMiB} MiB.",
                $"Too large (max {MaxDecodedClipMiB} MiB decoded)");
            return true;
        }

        return false;
    }

    private bool TryCreateUnavailableResultForSampleCount(
        string path,
        long sampleCount,
        out ClipLoadResult unavailableResult)
    {
        unavailableResult = default!;

        if (sampleCount > MaxClipDurationSamples)
        {
            var decodedDuration = TimeSpan.FromSeconds(sampleCount / (double)InternalSampleCountPerSecond);
            unavailableResult = RejectClip(
                $"Clip '{path}' exceeded the preload duration limit of {FormatDuration(MaxClipDuration)} while decoding; decoded at least {FormatDuration(decodedDuration)}.",
                $"Too long (max {FormatDuration(MaxClipDuration)})");
            return true;
        }

        if (sampleCount > MaxDecodedClipSamples)
        {
            unavailableResult = RejectClip(
                $"Clip '{path}' exceeded the preload decoded-size limit of {MaxDecodedClipMiB} MiB while decoding.",
                $"Too large (max {MaxDecodedClipMiB} MiB decoded)");
            return true;
        }

        return false;
    }

    private ClipLoadResult RejectClip(string logMessage, string availabilityText)
    {
        _logService.Warning(logMessage);
        return new ClipLoadResult(null, false, availabilityText);
    }

    private static float[] CreateInitialSampleBuffer(TimeSpan sourceDuration)
    {
        if (TryGetDecodedSampleCount(sourceDuration, out var expectedSampleCount) &&
            expectedSampleCount is > 0 and <= MaxAllowedClipSamples)
        {
            return new float[expectedSampleCount];
        }

        return new float[ReadBufferSampleCount];
    }

    private static float[] EnsureCapacity(float[] sampleBuffer, int requiredSampleCount)
    {
        if (requiredSampleCount <= sampleBuffer.Length)
        {
            return sampleBuffer;
        }

        var nextLength = Math.Max(sampleBuffer.Length, 1);
        while (nextLength < requiredSampleCount)
        {
            var doubledLength = (long)nextLength * 2;
            nextLength = (int)Math.Min(
                Math.Max(doubledLength, requiredSampleCount),
                MaxAllowedClipSamples);
        }

        Array.Resize(ref sampleBuffer, nextLength);
        return sampleBuffer;
    }

    private static bool TryGetDecodedSampleCount(TimeSpan duration, out int sampleCount)
    {
        sampleCount = 0;

        if (!HasUsableDuration(duration))
        {
            return false;
        }

        var decodedSampleCount = Math.Ceiling(duration.TotalSeconds * InternalSampleCountPerSecond);
        if (decodedSampleCount <= 0 || decodedSampleCount > int.MaxValue)
        {
            return false;
        }

        sampleCount = (int)decodedSampleCount;
        return true;
    }

    private static bool HasUsableDuration(TimeSpan duration)
    {
        return duration > TimeSpan.Zero && duration < TimeSpan.MaxValue;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : duration.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }
}
