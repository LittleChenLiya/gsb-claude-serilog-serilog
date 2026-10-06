using Serilog.Tests.Support;

namespace Serilog.Tests.Capturing;

public class TemplateBinderTests
{
    [Fact]
    public void RepeatedNamedTokensShareASingleBinding()
    {
        var receipt = TemplateBinder.Bind("{Name} {Name}", "world");

        Assert.True(receipt.IsComplete);
        var property = Assert.Single(receipt.Properties);
        Assert.Equal("Name", property.Name);
        Assert.Empty(receipt.MissingProperties);
        Assert.Empty(receipt.UnmatchedValues);

        // Both occurrences of {Name} render the same captured value.
        Assert.Equal("\"world\" \"world\"", receipt.Render());
    }

    [Fact]
    public void MissingValuesAreReportedWithIdentifiableTokens()
    {
        var receipt = TemplateBinder.Bind("{First} {Second}", 1);

        Assert.False(receipt.IsComplete);
        var missing = Assert.Single(receipt.MissingProperties);
        Assert.Equal("Second", missing.PropertyName);
        Assert.Equal("{Second}", missing.RawText);
        Assert.Empty(receipt.UnmatchedValues);

        // The token with the missing value remains identifiable in the output.
        Assert.Equal("1 {Second}", receipt.Render());
    }

    [Fact]
    public void ExtraValuesAreReportedAsUnmatched()
    {
        var receipt = TemplateBinder.Bind("{First}", 1, 2, 3);

        Assert.False(receipt.IsComplete);
        Assert.Empty(receipt.MissingProperties);
        Assert.Equal(new object?[] { 2, 3 }, receipt.UnmatchedValues);
        Assert.Equal("1", receipt.Render());
    }

    [Fact]
    public void ValuesBeyondRepeatedNamesAreReportedAsUnmatched()
    {
        var receipt = TemplateBinder.Bind("{Name} {Name}", "a", "b");

        Assert.False(receipt.IsComplete);
        Assert.Single(receipt.Properties);
        Assert.Empty(receipt.MissingProperties);
        Assert.Equal(new object?[] { "b" }, receipt.UnmatchedValues);
        Assert.Equal("\"a\" \"a\"", receipt.Render());
    }

    [Fact]
    public void ToStringIsOnlyEvaluatedWhenOutputIsRendered()
    {
        var value = new CountsToStringEvaluations();

        var receipt = TemplateBinder.Bind("{Value}", value);

        Assert.Equal(0, value.ToStringEvaluationCount);
        Assert.Equal("counted", receipt.Render());
        Assert.Equal(1, value.ToStringEvaluationCount);
    }

    [Fact]
    public void RepeatedTokensEvaluateToStringOnlyOnce()
    {
        var value = new CountsToStringEvaluations();

        var receipt = TemplateBinder.Bind("{Value} and {Value}", value);

        Assert.Equal(0, value.ToStringEvaluationCount);
        Assert.Equal("counted and counted", receipt.Render());
        Assert.Equal(1, value.ToStringEvaluationCount);
    }

    [Fact]
    public void PositionalTemplatesAreOutOfScope()
    {
        Assert.Throws<ArgumentException>(() => TemplateBinder.Bind("{0}", 1));
    }

    [Fact]
    public void BoundReceiptsFlowThroughTheOriginalLoggerEntryPoints()
    {
        var sink = new InMemorySink();
        using var log = new LoggerConfiguration()
            .WriteTo.Sink(sink)
            .CreateLogger();

        var value = new CountsToStringEvaluations();
        var receipt = TemplateBinder.Bind("{Value} {Value}", value);

        log.Write(receipt.ToLogEvent(Warning));

        var evt = sink.SingleEvent;
        Assert.Equal(Warning, evt.Level);
        Assert.Equal("counted counted", sink.SingleMessage);
        Assert.True(evt.Properties.ContainsKey("Value"));
    }

    [Fact]
    public void OriginalLoggerEntryPointsRenderRepeatedNamesConsistently()
    {
        var sink = new InMemorySink();
        using var log = new LoggerConfiguration()
            .WriteTo.Sink(sink)
            .CreateLogger();

        log.Information("{Name} {Name}", "world");

        Assert.Equal("\"world\" \"world\"", sink.SingleMessage);
        Assert.True(sink.SingleEvent.Properties.ContainsKey("Name"));
    }

    sealed class CountsToStringEvaluations
    {
        public int ToStringEvaluationCount;

        public override string ToString()
        {
            ++ToStringEvaluationCount;
            return "counted";
        }
    }
}
