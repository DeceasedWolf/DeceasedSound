using System.IO;

namespace SoundboardMixer.App.Infrastructure;

internal static class AppPaths
{
    public static string AppDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SoundboardMixer");

    public static string SettingsFilePath => Path.Combine(AppDataDirectory, "settings.json");
}
