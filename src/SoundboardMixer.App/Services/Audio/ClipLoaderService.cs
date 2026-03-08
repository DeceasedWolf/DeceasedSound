using NAudio.Wave;
using System.IO;
using SoundboardMixer.App.Models;

namespace SoundboardMixer.App.Services.Audio;

internal sealed class ClipLoaderService
{
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
            var sampleProvider = AudioSampleProviderUtilities.ConvertToInternalMixFormat(reader);
            var samples = new List<float>();
            var readBuffer = new float[AudioEngineService.InternalSampleRate * AudioEngineService.InternalChannels];

            int samplesRead;
            while ((samplesRead = sampleProvider.Read(readBuffer, 0, readBuffer.Length)) > 0)
            {
                samples.AddRange(readBuffer.AsSpan(0, samplesRead).ToArray());
            }

            if (samples.Count == 0)
            {
                return new ClipLoadResult(null, false, "Clip contained no audio");
            }

            var duration = TimeSpan.FromSeconds(samples.Count / (double)(AudioEngineService.InternalSampleRate * AudioEngineService.InternalChannels));
            var loadedClip = new LoadedClip(path, samples.ToArray(), duration);
            return new ClipLoadResult(loadedClip, true, $"Ready ({duration:mm\\:ss})");
        }
        catch (Exception exception)
        {
            _logService.Warning($"Failed to load clip '{path}'. {exception.Message}");
            return new ClipLoadResult(null, false, "Load failed");
        }
    }

    internal sealed record ClipLoadResult(LoadedClip? Clip, bool IsAvailable, string AvailabilityText);
}
