using Microsoft.VisualStudio.TestTools.UnitTesting;
using SoundboardMixer.App.Services;
using SoundboardMixer.App.Tests.Support;

namespace SoundboardMixer.App.Tests;

[TestClass]
public sealed class SettingsServiceTests
{
    [TestMethod]
    public async Task LoadAsync_NormalizesPersistedSettings()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = directory.FilePath("settings.json");
        await File.WriteAllTextAsync(settingsPath, """
            {
              "MicrophoneVolume": 3.5,
              "SoundboardVolume": -1.0,
              "AutoStartOnWindowsStart": true,
              "MinimizeToSystemTrayOnClose": true,
              "StopAllHotkeyText": "  Ctrl+Shift+Esc  ",
              "Window": {
                "Width": 0,
                "Height": -25
              },
              "Clips": [
                {
                  "Id": " ",
                  "DisplayName": "  First clip  ",
                  "SourcePath": "  C:/clips/first.wav  ",
                  "Volume": 2.0,
                  "HotkeyText": "   "
                },
                {
                  "Id": "existing",
                  "DisplayName": null,
                  "SourcePath": null,
                  "Volume": -1.0,
                  "HotkeyText": "  Ctrl+A  "
                }
              ]
            }
            """);

        var service = new SettingsService(new TestLogService(), settingsPath);

        var settings = await service.LoadAsync();

        Assert.AreEqual(3.0f, settings.MicrophoneVolume, 0.0001f);
        Assert.AreEqual(0.0f, settings.SoundboardVolume, 0.0001f);
        Assert.IsTrue(settings.AutoStartOnWindowsStart);
        Assert.IsTrue(settings.MinimizeToSystemTrayOnClose);
        Assert.AreEqual("Ctrl+Shift+Esc", settings.StopAllHotkeyText);
        Assert.AreEqual(1180, settings.Window.Width);
        Assert.AreEqual(760, settings.Window.Height);

        Assert.AreEqual(2, settings.Clips.Count);

        var firstClip = settings.Clips[0];
        Assert.IsTrue(Guid.TryParseExact(firstClip.Id, "N", out _));
        Assert.AreEqual("First clip", firstClip.DisplayName);
        Assert.AreEqual("C:/clips/first.wav", firstClip.SourcePath);
        Assert.AreEqual(1.0f, firstClip.Volume, 0.0001f);
        Assert.IsNull(firstClip.HotkeyText);

        var secondClip = settings.Clips[1];
        Assert.AreEqual("existing", secondClip.Id);
        Assert.AreEqual(string.Empty, secondClip.DisplayName);
        Assert.AreEqual(string.Empty, secondClip.SourcePath);
        Assert.AreEqual(0.0f, secondClip.Volume, 0.0001f);
        Assert.AreEqual("Ctrl+A", secondClip.HotkeyText);
    }

    [TestMethod]
    public async Task LoadAsync_NormalizesNullCollectionsAndWindow()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = directory.FilePath("settings.json");
        await File.WriteAllTextAsync(settingsPath, """
            {
              "Clips": null,
              "Window": null
            }
            """);

        var service = new SettingsService(new TestLogService(), settingsPath);

        var settings = await service.LoadAsync();

        Assert.IsNotNull(settings.Clips);
        Assert.AreEqual(0, settings.Clips.Count);
        Assert.IsNotNull(settings.Window);
        Assert.AreEqual(1180, settings.Window.Width);
        Assert.AreEqual(760, settings.Window.Height);
    }

    [TestMethod]
    public async Task LoadAsync_ReturnsDefaultsAndLogsWarning_WhenJsonIsCorrupt()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = directory.FilePath("settings.json");
        await File.WriteAllTextAsync(settingsPath, "{ this is not json");

        var logService = new TestLogService();
        var service = new SettingsService(logService, settingsPath);

        var settings = await service.LoadAsync();

        Assert.AreEqual(1.0f, settings.MicrophoneVolume, 0.0001f);
        Assert.AreEqual(1.0f, settings.SoundboardVolume, 0.0001f);
        Assert.AreEqual(0, settings.Clips.Count);
        Assert.AreEqual(1180, settings.Window.Width);
        Assert.IsTrue(logService.Entries.Any(entry =>
            entry.Level == "Warning" &&
            entry.Message.StartsWith("Failed to load settings.", StringComparison.Ordinal)));
    }
}
