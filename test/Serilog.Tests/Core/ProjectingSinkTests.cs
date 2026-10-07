using System.Diagnostics;

namespace Serilog.Tests.Core;

public class ProjectingSinkTests
{
    static LogEvent WriteThroughProjection(
        CollectingSink projected,
        CollectingSink unprojected,
        string[] selectedProperties,
        LogEvent evt)
    {
        var logger = new LoggerConfiguration()
            .WriteTo.SelectProperties(selectedProperties, wt => wt.Sink(projected))
            .WriteTo.Sink(unprojected)
            .CreateLogger();

        logger.Write(evt);
        return evt;
    }

    static LogEvent SomeEventWithTwoProperties(Exception? exception = null, DateTimeOffset? timestamp = null)
    {
        return Some.LogEvent(
            timestamp ?? Some.OffsetInstant(),
            Warning,
            exception,
            "Got {@A} and {B}",
            [new { X = 1 }, 2],
            ActivityTraceId.CreateRandom(),
            ActivitySpanId.CreateRandom());
    }

    [Fact]
    public void ProjectedSinkReceivesOnlySelectedPropertiesWithOriginalMetadata()
    {
        var projected = new CollectingSink();
        var unprojected = new CollectingSink();
        var exception = new DivideByZeroException();
        var timestamp = Some.OffsetInstant();
        var evt = SomeEventWithTwoProperties(exception, timestamp);

        WriteThroughProjection(projected, unprojected, ["A"], evt);

        var p = projected.SingleEvent;
        Assert.NotSame(evt, p);

        var property = Assert.Single(p.Properties);
        Assert.Equal("A", property.Key);

        Assert.Equal(timestamp, p.Timestamp);
        Assert.Equal(Warning, p.Level);
        Assert.Same(exception, p.Exception);
        Assert.Same(evt.MessageTemplate, p.MessageTemplate);
        Assert.Equal(evt.TraceId, p.TraceId);
        Assert.Equal(evt.SpanId, p.SpanId);
    }

    [Fact]
    public void SelectedStructuredValuesArePreservedNotStringified()
    {
        var projected = new CollectingSink();
        var unprojected = new CollectingSink();
        var evt = SomeEventWithTwoProperties();

        WriteThroughProjection(projected, unprojected, ["A"], evt);

        var structure = Assert.IsType<StructureValue>(projected.SingleEvent.Properties["A"]);
        var x = Assert.Single(structure.Properties);
        Assert.Equal("X", x.Name);
        Assert.Equal(new ScalarValue(1), x.Value);

        // The projected value is the very same (immutable) value instance.
        Assert.Same(unprojected.SingleEvent.Properties["A"], projected.SingleEvent.Properties["A"]);
    }

    [Fact]
    public void MessageTemplateRendersSelectedParametersAndLeavesOthersUnbound()
    {
        var projected = new CollectingSink();
        var unprojected = new CollectingSink();
        var evt = SomeEventWithTwoProperties();

        WriteThroughProjection(projected, unprojected, ["A"], evt);

        Assert.Equal("Got { X: 1 } and {B}", projected.SingleEvent.RenderMessage());
        Assert.Equal("Got { X: 1 } and 2", unprojected.SingleEvent.RenderMessage());
    }

    [Fact]
    public void OriginalEventAndOtherSinksAreNotModified()
    {
        var projected = new CollectingSink();
        var unprojected = new CollectingSink();
        var evt = SomeEventWithTwoProperties();

        WriteThroughProjection(projected, unprojected, ["A"], evt);

        Assert.Same(evt, unprojected.SingleEvent);
        Assert.Equal(["A", "B"], unprojected.SingleEvent.Properties.Keys.OrderBy(k => k).ToArray());
        Assert.Equal(["A", "B"], evt.Properties.Keys.OrderBy(k => k).ToArray());
    }

    [Fact]
    public void NullArgumentsAreRejected()
    {
        var writeTo = new LoggerConfiguration().WriteTo;

        Assert.Throws<ArgumentNullException>(() => writeTo.SelectProperties(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => writeTo.SelectProperties(["A"], null!));
    }
}
