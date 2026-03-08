using SoundboardMixer.App.Models;

namespace SoundboardMixer.App.Services;

/// <summary>
/// Provides a lightweight application log stream for UI status and troubleshooting.
/// </summary>
public interface ILogService
{
    event EventHandler<LogEntry>? EntryLogged;

    void Info(string message);

    void Warning(string message);

    void Error(string message, Exception? exception = null);
}
