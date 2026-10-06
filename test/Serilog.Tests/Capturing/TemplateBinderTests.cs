namespace Serilog.Tests.Capturing;

public class TemplateBinderTests
{
    sealed class CountingValue
    {
        public int ToStringCount;

        public override string ToString()
        {
            ++ToStringCount;
            return "counted";
        }
    }

    static Dictionary<string, object?> Values(params (string Name, object? Value)[] values) =>
        values.ToDictionary(v => v.Name, v => v.Value);

    [Fact]
    public void RepeatedTemplateNamesShareOneBindingAndRenderTheSameValue()
    {
        var counting = new CountingValue();

        var receipt = TemplateBinder.Bind("Hello {name}, again {name}", Values(("name", counting)));

        Assert.Single(receipt.BoundProperties);
        Assert.True(receipt.IsComplete);
        Assert.Equal("Hello counted, again counted", receipt.Render());
        Assert.Equal(1, counting.ToStringCount);
    }

    [Fact]
    public void MissingValuesAreReportedAsIdentifiableTokens()
    {
        var receipt = TemplateBinder.Bind("{present} and {missing}", Values(("present", 1)));

        var token = Assert.Single(receipt.MissingTokens);
        Assert.Equal("missing", token.PropertyName);
        Assert.Equal("{missing}", token.ToString());
        Assert.False(receipt.IsComplete);

        // The missing token remains identifiable in the rendered output.
        Assert.Equal("1 and {missing}", receipt.Render());
    }

    [Fact]
    public void RepeatedMissingNamesAreReportedOnce()
    {
        var receipt = TemplateBinder.Bind("{m} then {m}", Values());

        var token = Assert.Single(receipt.MissingTokens);
        Assert.Equal("m", token.PropertyName);
        Assert.Equal("{m} then {m}", receipt.Render());
    }

    [Fact]
    public void ExcessValuesAreReported()
    {
        var receipt = TemplateBinder.Bind("{a}", Values(("a", 1), ("extra", 2)));

        Assert.Empty(receipt.MissingTokens);
        Assert.Equal(new[] { "extra" }, receipt.ExcessValues);
        Assert.False(receipt.IsComplete);
        Assert.Equal("1", receipt.Render());
    }

    [Fact]
    public void ToStringIsEvaluatedLazilyAndOnlyOnce()
    {
        var counting = new CountingValue();

        var receipt = TemplateBinder.Bind("{value} / {value}", Values(("value", counting)));

        // Binding alone must not touch the value.
        Assert.Equal(0, counting.ToStringCount);

        Assert.Equal("counted / counted", receipt.Render());
        Assert.Equal(1, counting.ToStringCount);

        // Re-rendering reuses the memoized rendering.
        Assert.Equal("counted / counted", receipt.Render());
        Assert.Equal(1, counting.ToStringCount);
    }

    [Fact]
    public void CompleteReceiptReportsNoMissingOrExcess()
    {
        var receipt = TemplateBinder.Bind("{a} {b}", Values(("a", 1), ("b", 2)));

        Assert.True(receipt.IsComplete);
        Assert.Empty(receipt.MissingTokens);
        Assert.Empty(receipt.ExcessValues);
        Assert.Equal("1 2", receipt.Render());
    }

    [Fact]
    public void MemorySinkCapturesEventsWrittenThroughTheStandardLoggerEntry()
    {
        var sink = new MemorySink();
        using var log = new LoggerConfiguration()
            .WriteTo.Sink(sink)
            .CreateLogger();

        log.Information("Hello {name}", "world");

        var evt = sink.SingleEvent;
        Assert.Equal("Hello \"world\"", sink.Messages.Single());

        // The captured event can be re-bound through the receipt API with the same result.
        var receipt = TemplateBinder.Bind(
            evt.MessageTemplate,
            evt.Properties.ToDictionary(p => p.Key, p => (object?)((ScalarValue)p.Value).Value));

        Assert.True(receipt.IsComplete);
        Assert.Equal(sink.Messages.Single(), receipt.Render());
    }
}
