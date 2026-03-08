using System.Text.Json;
using System.IO;
using SoundboardMixer.App.Infrastructure;
using SoundboardMixer.App.Models;

namespace SoundboardMixer.App.Services;

public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly ILogService _logService;

    public SettingsService(ILogService logService)
    {
        _logService = logService;
    }

    public string SettingsPath => AppPaths.SettingsFilePath;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.AppDataDirectory);

            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            await using var stream = File.OpenRead(SettingsPath);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            return Normalize(settings ?? new AppSettings());
        }
        catch (Exception exception)
        {
            _logService.Warning($"Failed to load settings. Defaults will be used. {exception.Message}");
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        settings = Normalize(settings);
        Directory.CreateDirectory(AppPaths.AppDataDirectory);
        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var tempPath = $"{SettingsPath}.tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, SettingsPath, true);
        }
        catch (Exception exception)
        {
            _logService.Error("Failed to save settings.", exception);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        settings.Clips ??= [];
        settings.Window ??= new WindowSettings();
        settings.MicrophoneVolume = Math.Clamp(settings.MicrophoneVolume, 0.0f, 1.0f);
        settings.SoundboardVolume = Math.Clamp(settings.SoundboardVolume, 0.0f, 1.0f);
        settings.Window.Width = settings.Window.Width > 0 ? settings.Window.Width : 1180;
        settings.Window.Height = settings.Window.Height > 0 ? settings.Window.Height : 760;

        foreach (var clip in settings.Clips)
        {
            clip.Id = string.IsNullOrWhiteSpace(clip.Id) ? Guid.NewGuid().ToString("N") : clip.Id;
            clip.DisplayName = clip.DisplayName?.Trim() ?? string.Empty;
            clip.SourcePath = clip.SourcePath?.Trim() ?? string.Empty;
            clip.Volume = Math.Clamp(clip.Volume, 0.0f, 1.0f);
            clip.HotkeyText = string.IsNullOrWhiteSpace(clip.HotkeyText) ? null : clip.HotkeyText.Trim();
        }

        return settings;
    }
}
