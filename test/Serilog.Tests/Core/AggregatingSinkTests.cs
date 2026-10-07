namespace Serilog.Tests.Core;

public class AggregatingSinkTests
{
    [Fact]
    public void WhenTwoOfThreeSinksFailAllSinksAreAttemptedInOrderAndFailuresAreAggregatedInConfigurationOrder()
    {
        var calls = new List<string>();
        var first = new InvalidOperationException("first sink failed");
        var third = new ApplicationException("third sink failed");

        var success = new CollectingSink();
        var sink = new AggregatingSink(new ILogEventSink[]
        {
            new DelegatingSink(_ => { calls.Add("first"); throw first; }),
            new DelegatingSink(le => { calls.Add("second"); success.Emit(le); }),
            new DelegatingSink(_ => { calls.Add("third"); throw third; }),
        });

        var evt = Some.InformationEvent();
        var thrown = Assert.Throws<AggregateException>(() => sink.Emit(evt));

        // Every sink was attempted exactly once, in configuration order.
        Assert.Equal(new[] { "first", "second", "third" }, calls);

        // Failures are aggregated in configuration order.
        Assert.Equal(2, thrown.InnerExceptions.Count);
        Assert.Same(first, thrown.InnerExceptions[0]);
        Assert.Same(third, thrown.InnerExceptions[1]);

        // The successful sink received the original event exactly once (no copying or re-sending).
        var received = Assert.Single(success.Events);
        Assert.Same(evt, received);
    }

    [Fact]
    public void WhenASingleSinkFailsTheOriginalCauseIsPreserved()
    {
        var original = new InvalidOperationException("the one and only failure");
        var sink = new AggregatingSink(new ILogEventSink[]
        {
            new DelegatingSink(_ => throw original),
            new CollectingSink(),
        });

        var thrown = Assert.Throws<InvalidOperationException>(() => sink.Emit(Some.InformationEvent()));

        Assert.Same(original, thrown);
    }

    [Fact]
    public void WhenNoSinkFailsNothingIsThrown()
    {
        var sink = new AggregatingSink(new ILogEventSink[]
        {
            new CollectingSink(),
            new CollectingSink(),
        });

        sink.Emit(Some.InformationEvent());
    }

    [Fact]
    public void DisposeFailuresAreAggregatedSeparatelyFromEmitFailures()
    {
        var emitFailure = new InvalidOperationException("emit failed");
        var firstDisposeFailure = new ApplicationException("first dispose failed");
        var secondDisposeFailure = new ApplicationException("second dispose failed");

        var sink = new AggregatingSink(new ILogEventSink[]
        {
            new DelegatingSink(_ => throw emitFailure),
            new ThrowingOnDisposeSink(firstDisposeFailure),
            new ThrowingOnDisposeSink(secondDisposeFailure),
        });

        // The emit failure is reported on its own...
        var emitThrown = Assert.Throws<InvalidOperationException>(() => sink.Emit(Some.InformationEvent()));
        Assert.Same(emitFailure, emitThrown);

        // ...while dispose failures are reported independently, in configuration order, and
        // do not include the earlier emit failure.
        var disposeThrown = Assert.Throws<AggregateException>(() => sink.Dispose());
        Assert.Equal(2, disposeThrown.InnerExceptions.Count);
        Assert.Same(firstDisposeFailure, disposeThrown.InnerExceptions[0]);
        Assert.Same(secondDisposeFailure, disposeThrown.InnerExceptions[1]);
        Assert.DoesNotContain(emitFailure, disposeThrown.InnerExceptions);
    }

    [Fact]
    public void ASingleDisposeFailurePreservesTheOriginalCause()
    {
        var original = new ApplicationException("dispose failed");
        var sink = new AggregatingSink(new ILogEventSink[]
        {
            new DisposeTrackingSink(),
            new ThrowingOnDisposeSink(original),
        });

        var thrown = Assert.Throws<ApplicationException>(() => sink.Dispose());
        Assert.Same(original, thrown);
    }

    [Fact]
    public void AllDisposableSinksAreDisposedEvenWhenOthersFail()
    {
        var failure = new ApplicationException("boom");
        var tracking = new DisposeTrackingSink();
        var sink = new AggregatingSink(new ILogEventSink[]
        {
            new ThrowingOnDisposeSink(failure),
            tracking,
        });

        // A single dispose failure keeps its original cause; the remaining sink is still disposed.
        var thrown = Assert.Throws<ApplicationException>(() => sink.Dispose());
        Assert.Same(failure, thrown);
        Assert.True(tracking.IsDisposed);
    }

    [Fact]
    public void NullSinkCollectionsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new AggregatingSink(null!));
    }

    [Fact]
    public void NullConfigurationCallbacksAreRejected()
    {
        var wt = new LoggerConfiguration().WriteTo;
        Assert.Throws<ArgumentNullException>(() => wt.Aggregate(null!));
    }

    [Fact]
    public void WriteToAggregateWiresTheAdapterIntoTheLoggerPipeline()
    {
        var selfLog = new List<string>();
        using var _ = TemporarySelfLog.SaveTo(selfLog);

        var first = new InvalidOperationException("first sink failed");
        var third = new ApplicationException("third sink failed");
        var success = new CollectingSink();
        var calls = new List<string>();

        var log = new LoggerConfiguration()
            .WriteTo.Aggregate(wt =>
            {
                wt.Sink(new DelegatingSink(_ => { calls.Add("first"); throw first; }));
                wt.Sink(new DelegatingSink(le => { calls.Add("second"); success.Emit(le); }));
                wt.Sink(new DelegatingSink(_ => { calls.Add("third"); throw third; }));
            })
            .CreateLogger();

        // The default logger still swallows sink failures; the aggregate failure is reported through SelfLog.
        log.Information("Hello, aggregation!");

        Assert.Equal(new[] { "first", "second", "third" }, calls);
        Assert.Single(success.Events);
        var report = Assert.Single(selfLog, m => m.Contains("Failed to emit a log event to one or more sinks."));
        Assert.Contains("first sink failed", report);
        Assert.Contains("third sink failed", report);
    }

    [Fact]
    public void WriteToAggregateDisposesConfiguredSinksWithTheLogger()
    {
        var tracking = new DisposeTrackingSink();

        var log = new LoggerConfiguration()
            .WriteTo.Aggregate(wt => wt.Sink(tracking))
            .CreateLogger();

        ((IDisposable)log).Dispose();

        Assert.True(tracking.IsDisposed);
    }

    sealed class ThrowingOnDisposeSink : ILogEventSink, IDisposable
    {
        readonly Exception _exception;

        public ThrowingOnDisposeSink(Exception exception)
        {
            _exception = exception;
        }

        public void Emit(LogEvent logEvent)
        {
        }

        public void Dispose()
        {
            throw _exception;
        }
    }
}
