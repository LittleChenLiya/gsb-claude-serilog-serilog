namespace Serilog.Tests.Support;

// In-memory sink example: captures log events and their rendered messages
// entirely in memory, so tests can inspect both the structured properties and
// the final textual output without touching the console or the file system.
class InMemorySink : ILogEventSink
{
    readonly object _sync = new();
    readonly List<LogEvent> _events = [];
    readonly List<string> _messages = [];

    public IReadOnlyList<LogEvent> Events => _events;

    public IReadOnlyList<string> Messages => _messages;

    public LogEvent SingleEvent => _events.Single();

    public string SingleMessage => _messages.Single();

    public void Emit(LogEvent logEvent)
    {
        lock (_sync)
        {
            _events.Add(logEvent);
            _messages.Add(logEvent.RenderMessage(CultureInfo.InvariantCulture));
        }
    }
}
