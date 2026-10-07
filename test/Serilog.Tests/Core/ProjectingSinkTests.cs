namespace Serilog.Tests.Core;

public class ProjectingSinkTests
{
    [Fact]
    public void ProjectedSinkSeesOnlySelectedPropertiesWhileOtherSinksSeeTheOriginalEvent()
    {
        var projected = new CollectingSink();
        var unprojected = new CollectingSink();

        var logger = new LoggerConfiguration()
            .WriteTo.Project(["A"], wt => wt.Sink(projected))
            .WriteTo.Sink(unprojected)
            .CreateLogger();

        logger.Information("Values {A} and {B}", 1, 2);

        Assert.Equal(new[] { "A" }, projected.SingleEvent.Properties.Keys);
        Assert.Equal(new ScalarValue(1), projected.SingleEvent.Properties["A"]);

        Assert.Equal(new[] { "A", "B" }, unprojected.SingleEvent.Properties.Keys.OrderBy(k => k));
        Assert.Equal(new ScalarValue(2), unprojected.SingleEvent.Properties["B"]);
    }

    [Fact]
    public void TimestampLevelAndExceptionArePreservedOnTheProjectedEvent()
    {
        var projected = new CollectingSink();

        var logger = new LoggerConfiguration()
            .WriteTo.Project(["A"], wt => wt.Sink(projected))
            .CreateLogger();

        var exception = Some.Exception();
        var timestamp = Some.OffsetInstant();

        logger.Write(new LogEvent(
            timestamp,
            Warning,
            exception,
            Some.MessageTemplate(),
            [new LogEventProperty("A", new ScalarValue(1))]));

        var projectedEvent = projected.SingleEvent;
        Assert.Equal(timestamp, projectedEvent.Timestamp);
        Assert.Equal(Warning, projectedEvent.Level);
        Assert.Same(exception, projectedEvent.Exception);
    }

    [Fact]
    public void MessageTemplateRendersSelectedParametersAndShowsUnselectedParametersUnbound()
    {
        var projected = new CollectingSink();

        var logger = new LoggerConfiguration()
            .WriteTo.Project(["Selected"], wt => wt.Sink(projected))
            .CreateLogger();

        logger.Information("Got {Selected} and {Unselected}", 42, "hidden");

        Assert.Equal("Got 42 and {Unselected}", projected.SingleEvent.RenderMessage());
    }

    [Fact]
    public void StructuredValuesArePreservedByReferenceAndNotConvertedToStrings()
    {
        var projected = new CollectingSink();

        var logger = new LoggerConfiguration()
            .WriteTo.Project(["Structured"], wt => wt.Sink(projected))
            .CreateLogger();

        logger.Information("Value is {@Structured}", new { X = 1, Y = 2 });

        var value = Assert.IsType<StructureValue>(projected.SingleEvent.Properties["Structured"]);
        Assert.Equal(new[] { "X", "Y" }, value.Properties.Select(p => p.Name));
    }

    [Fact]
    public void TheOriginalEventIsNotModifiedByProjection()
    {
        var projected = new CollectingSink();

        var logger = new LoggerConfiguration()
            .WriteTo.Project(["A"], wt => wt.Sink(projected))
            .CreateLogger();

        var original = Some.LogEvent(messageTemplate: "Values {A} and {B}", propertyValues: [1, 2]);
        var originalPropertyNames = original.Properties.Keys.ToList();

        logger.Write(original);

        Assert.NotSame(original, projected.SingleEvent);
        Assert.Equal(originalPropertyNames, original.Properties.Keys);
        Assert.Equal("Values 1 and 2", original.RenderMessage());
    }

    [Fact]
    public void UnselectedAndMissingPropertiesAreAbsentFromTheProjectedEvent()
    {
        var projected = new CollectingSink();

        var logger = new LoggerConfiguration()
            .WriteTo.Project(["A", "Missing"], wt => wt.Sink(projected))
            .CreateLogger();

        logger.Information("Values {A} and {B}", 1, 2);

        Assert.Equal(new[] { "A" }, projected.SingleEvent.Properties.Keys);
    }

    [Fact]
    public void NullArgumentsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new LoggerConfiguration()
            .WriteTo.Project(null!, wt => { }));

        Assert.Throws<ArgumentNullException>(() => new LoggerConfiguration()
            .WriteTo.Project(["A"], null!));

        Assert.Throws<ArgumentNullException>(() => new ProjectingSink(null!, ["A"]));

        Assert.Throws<ArgumentNullException>(() => new ProjectingSink(new CollectingSink(), null!));

        Assert.Throws<ArgumentNullException>(() => new ProjectingSink(new CollectingSink(), [null!]));
    }
}
