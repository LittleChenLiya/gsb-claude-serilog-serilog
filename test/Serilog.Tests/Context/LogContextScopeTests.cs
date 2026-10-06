namespace Serilog.Tests.Context;

public class LogContextScopeTests
{
    static (Serilog.Core.Logger, Func<LogEvent?>) CreateCapturingLogger()
    {
        LogEvent? lastEvent = null;
        var log = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(new DelegatingSink(e => lastEvent = e))
            .CreateLogger();
        return (log, () => lastEvent);
    }

    [Fact]
    public async Task ParallelAsyncScopesWithSameKeyDoNotInterfere()
    {
        var bothReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyCount = 0;

        async Task Worker(int value)
        {
            LogEvent? lastEvent = null;
            var log = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .WriteTo.Sink(new DelegatingSink(e => lastEvent = e))
                .CreateLogger();

            using (LogContext.PushProperty("Worker", value))
            {
                if (Interlocked.Increment(ref readyCount) == 2)
                    bothReady.TrySetResult();

                // Ensure both workers have pushed the same key before either logs.
                await bothReady.Task;
                await Task.Yield();

                release.TrySetResult();
                await Task.Delay(1);

                log.Information("hello");
                Assert.NotNull(lastEvent);
                Assert.Equal(value, lastEvent!.Properties["Worker"].LiteralValue());
            }
        }

        await Task.WhenAll(Worker(1), Worker(2));
    }

    [Fact]
    public async Task NestedScopesRestoreOuterValuesAcrossAwaits()
    {
        var (log, lastEvent) = CreateCapturingLogger();

        using (LogContext.PushProperty("A", 1))
        {
            await Task.Yield();

            using (LogContext.PushProperty("A", 2))
            {
                await Task.Yield();
                log.Write(Some.InformationEvent());
                Assert.Equal(2, lastEvent()!.Properties["A"].LiteralValue());
            }

            await Task.Yield();
            log.Write(Some.InformationEvent());
            Assert.Equal(1, lastEvent()!.Properties["A"].LiteralValue());
        }

        log.Write(Some.InformationEvent());
        Assert.False(lastEvent()!.Properties.ContainsKey("A"));
    }

    [Fact]
    public async Task ScopeTokenRemainsValidAcrossAwaitPoints()
    {
        var (log, lastEvent) = CreateCapturingLogger();

        var scope = LogContext.PushProperty("A", 1);
        await Task.Yield();
        await Task.Run(() => { });

        log.Write(Some.InformationEvent());
        Assert.Equal(1, lastEvent()!.Properties["A"].LiteralValue());

        scope.Dispose();

        log.Write(Some.InformationEvent());
        Assert.False(lastEvent()!.Properties.ContainsKey("A"));
    }

    [Fact]
    public void OutOfOrderDisposeIsRejectedAndLeavesAllScopesIntact()
    {
        var (log, lastEvent) = CreateCapturingLogger();

        var outer = LogContext.PushProperty("A", 1);
        var inner = LogContext.PushProperty("B", 2);

        // Disposing the outer scope before the inner one is rejected...
        Assert.Throws<InvalidOperationException>(() => outer.Dispose());

        // ...and corrupts nothing: both scopes are still fully applied.
        log.Write(Some.InformationEvent());
        Assert.Equal(1, lastEvent()!.Properties["A"].LiteralValue());
        Assert.Equal(2, lastEvent()!.Properties["B"].LiteralValue());

        // The rejected token is not poisoned: once order is restored, both
        // scopes unwind normally.
        inner.Dispose();
        log.Write(Some.InformationEvent());
        Assert.Equal(1, lastEvent()!.Properties["A"].LiteralValue());
        Assert.False(lastEvent()!.Properties.ContainsKey("B"));

        outer.Dispose();
        log.Write(Some.InformationEvent());
        Assert.False(lastEvent()!.Properties.ContainsKey("A"));
    }

    [Fact]
    public void DoubleDisposeIsRejectedAndLeavesOuterScopeIntact()
    {
        var (log, lastEvent) = CreateCapturingLogger();

        var outer = LogContext.PushProperty("A", 1);
        var inner = LogContext.PushProperty("B", 2);

        inner.Dispose();
        Assert.Throws<InvalidOperationException>(() => inner.Dispose());

        // The outer scope is unaffected by the rejected second dispose.
        log.Write(Some.InformationEvent());
        Assert.Equal(1, lastEvent()!.Properties["A"].LiteralValue());
        Assert.False(lastEvent()!.Properties.ContainsKey("B"));

        outer.Dispose();
        log.Write(Some.InformationEvent());
        Assert.False(lastEvent()!.Properties.ContainsKey("A"));
    }

    [Fact]
    public void SuspendParticipatesInScopeOrdering()
    {
        var (log, lastEvent) = CreateCapturingLogger();

        var outer = LogContext.PushProperty("A", 1);
        var suspend = LogContext.Suspend();

        log.Write(Some.InformationEvent());
        Assert.Empty(lastEvent()!.Properties);

        // The suspend token is top-of-stack; disposing the outer push first is rejected.
        Assert.Throws<InvalidOperationException>(() => outer.Dispose());

        suspend.Dispose();
        log.Write(Some.InformationEvent());
        Assert.Equal(1, lastEvent()!.Properties["A"].LiteralValue());

        outer.Dispose();
        log.Write(Some.InformationEvent());
        Assert.False(lastEvent()!.Properties.ContainsKey("A"));
    }
}
