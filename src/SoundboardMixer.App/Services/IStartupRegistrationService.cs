namespace SoundboardMixer.App.Services;

internal interface IStartupRegistrationService
{
    bool SetAutoStartEnabled(bool enabled, out string? errorMessage);
}
