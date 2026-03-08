using SoundboardMixer.App.Models;

namespace SoundboardMixer.App.Services;

/// <summary>
/// Loads and saves the application's persisted JSON settings.
/// </summary>
public interface ISettingsService
{
    string SettingsPath { get; }

    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
