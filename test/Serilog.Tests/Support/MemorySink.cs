namespace Serilog.Tests.Support;

/// <summary>
/// An in-memory sink example that captures emitted log events along with their rendered
/// messages, for inspection in tests.
/// </summary>
public class MemorySink : ILogEventSink
{
    readonly List<LogEvent> _events = [];
    readonly List<string> _messages = [];

    public IReadOnlyList<LogEvent> Events => _events;

    public IReadOnlyList<string> Messages => _messages;

    public LogEvent SingleEvent => _events.Single();

    public void Emit(LogEvent logEvent)
    {
        _events.Add(logEvent);
        _messages.Add(logEvent.RenderMessage());
    }
}
