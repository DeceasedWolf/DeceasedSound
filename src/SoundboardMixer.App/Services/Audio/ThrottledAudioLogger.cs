using System.Diagnostics;

namespace SoundboardMixer.App.Services.Audio;

internal sealed class ThrottledAudioLogger
{
    private readonly ILogService _logService;
    private readonly long _intervalTimestampDelta;
    private int _suppressedCount;
    private long _nextLogTimestamp;

    public ThrottledAudioLogger(ILogService logService, TimeSpan interval)
    {
        _logService = logService;
        _intervalTimestampDelta = Math.Max(1, (long)(interval.TotalSeconds * Stopwatch.Frequency));
    }

    public void Warning(string message)
    {
        var now = Stopwatch.GetTimestamp();
        var nextLogTimestamp = Volatile.Read(ref _nextLogTimestamp);

        if (now < nextLogTimestamp)
        {
            Interlocked.Increment(ref _suppressedCount);
            return;
        }

        if (Interlocked.CompareExchange(ref _nextLogTimestamp, now + _intervalTimestampDelta, nextLogTimestamp) != nextLogTimestamp)
        {
            Interlocked.Increment(ref _suppressedCount);
            return;
        }

        var suppressedCount = Interlocked.Exchange(ref _suppressedCount, 0);
        if (suppressedCount > 0)
        {
            _logService.Warning($"{message} Suppressed {suppressedCount} similar warning(s).");
            return;
        }

        _logService.Warning(message);
    }
}
