using SoundboardMixer.App.Models;

namespace SoundboardMixer.App.Services;

public sealed class LogService : ILogService
{
    public event EventHandler<LogEntry>? EntryLogged;

    public void Info(string message) => Publish("Info", message);

    public void Warning(string message) => Publish("Warning", message);

    public void Error(string message, Exception? exception = null)
    {
        var fullMessage = exception is null ? message : $"{message} {exception.Message}";
        Publish("Error", fullMessage);
    }

    private void Publish(string level, string message)
    {
        EntryLogged?.Invoke(this, new LogEntry(level, message));
    }
}
