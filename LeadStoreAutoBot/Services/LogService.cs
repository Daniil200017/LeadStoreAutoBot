using System.Collections.Concurrent;

namespace LeadStoreAutoBot.Services;

public enum LogLevel { Info, Dim, Warn, Ok, Err, Bold }

public record LogEntry(DateTime Timestamp, string Message, LogLevel Level);

/// <summary>Очередь логов с подпиской UI. Аналог tk-консоли через очередь.</summary>
public class LogService
{
    private readonly ConcurrentQueue<LogEntry> _queue = new();
    public event Action<LogEntry>? EntryAdded;

    public void Log(string message, LogLevel level = LogLevel.Info)
    {
        var entry = new LogEntry(DateTime.Now, message, level);
        _queue.Enqueue(entry);
        EntryAdded?.Invoke(entry);
    }

    public IReadOnlyCollection<LogEntry> Drain()
    {
        var list = new List<LogEntry>();
        while (_queue.TryDequeue(out var e)) list.Add(e);
        return list;
    }
}
