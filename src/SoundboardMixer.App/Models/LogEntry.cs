namespace SoundboardMixer.App.Models;

/// <summary>
/// Represents a single status, warning, or error line shown in the in-app log.
/// </summary>
public sealed class LogEntry
{
    public LogEntry(string level, string message)
    {
        Timestamp = DateTimeOffset.Now;
        Level = level;
        Message = message;
    }

    public DateTimeOffset Timestamp { get; }

    public string Level { get; }

    public string Message { get; }

    public string DisplayText => $"[{Timestamp:HH:mm:ss}] {Level}: {Message}";
}
