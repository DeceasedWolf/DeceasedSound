using SoundboardMixer.App.Models;
using SoundboardMixer.App.Services;

namespace SoundboardMixer.App.Tests.Support;

internal sealed class TestLogService : ILogService
{
    public event EventHandler<LogEntry>? EntryLogged;

    public List<LogEntry> Entries { get; } = [];

    public void Info(string message) => Publish("Info", message);

    public void Warning(string message) => Publish("Warning", message);

    public void Error(string message, Exception? exception = null)
    {
        Publish("Error", exception is null ? message : $"{message} {exception.Message}");
    }

    private void Publish(string level, string message)
    {
        var entry = new LogEntry(level, message);
        Entries.Add(entry);
        EntryLogged?.Invoke(this, entry);
    }
}
