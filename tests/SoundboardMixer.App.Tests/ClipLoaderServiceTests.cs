using Microsoft.VisualStudio.TestTools.UnitTesting;
using NAudio.Wave;
using SoundboardMixer.App.Services.Audio;
using SoundboardMixer.App.Tests.Support;

namespace SoundboardMixer.App.Tests;

[TestClass]
public sealed class ClipLoaderServiceTests
{
    [TestMethod]
    public void LoadClip_ReturnsMissingFile_WhenPathIsBlankOrAbsent()
    {
        using var directory = new TemporaryDirectory();
        var service = new ClipLoaderService(new TestLogService());

        var blankResult = service.LoadClip(" ");
        var missingResult = service.LoadClip(directory.FilePath("missing.wav"));

        AssertMissingFile(blankResult);
        AssertMissingFile(missingResult);
    }

    [TestMethod]
    public void LoadClip_ReturnsLoadFailedAndLogsWarning_WhenFileIsNotAudio()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.FilePath("not-audio.txt");
        File.WriteAllText(path, "this is not audio data");
        var logService = new TestLogService();
        var service = new ClipLoaderService(logService);

        var result = service.LoadClip(path);

        Assert.IsFalse(result.IsAvailable);
        Assert.IsNull(result.Clip);
        Assert.AreEqual("Load failed", result.AvailabilityText);
        Assert.IsTrue(logService.Entries.Any(entry =>
            entry.Level == "Warning" &&
            entry.Message.StartsWith("Failed to load clip", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void LoadClip_RejectsClip_WhenKnownDurationExceedsPreloadLimit()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.FilePath("too-long.wav");
        WritePcm8Wave(path, TimeSpan.FromSeconds(301));
        var logService = new TestLogService();
        var service = new ClipLoaderService(logService);

        var result = service.LoadClip(path);

        Assert.IsFalse(result.IsAvailable);
        Assert.IsNull(result.Clip);
        Assert.AreEqual("Too long (max 05:00)", result.AvailabilityText);
        Assert.IsTrue(logService.Entries.Any(entry =>
            entry.Level == "Warning" &&
            entry.Message.Contains("exceeds the preload duration limit", StringComparison.Ordinal)));
    }

    private static void AssertMissingFile(ClipLoaderService.ClipLoadResult result)
    {
        Assert.IsFalse(result.IsAvailable);
        Assert.IsNull(result.Clip);
        Assert.AreEqual("Missing file", result.AvailabilityText);
    }

    private static void WritePcm8Wave(string path, TimeSpan duration)
    {
        var waveFormat = new WaveFormat(rate: 8_000, bits: 8, channels: 1);
        var sampleCount = checked((int)Math.Ceiling(duration.TotalSeconds * waveFormat.SampleRate));
        var buffer = Enumerable.Repeat((byte)128, waveFormat.SampleRate).ToArray();

        using var writer = new WaveFileWriter(path, waveFormat);
        while (sampleCount > 0)
        {
            var samplesToWrite = Math.Min(sampleCount, buffer.Length);
            writer.Write(buffer, 0, samplesToWrite);
            sampleCount -= samplesToWrite;
        }
    }
}
