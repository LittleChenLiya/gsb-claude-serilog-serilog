// Copyright © Serilog Contributors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Runtime.ExceptionServices;

namespace Serilog.Core.Sinks;

/// <summary>
/// A synchronous adapter that writes each log event to multiple sinks. Every sink is attempted
/// exactly once per event, in configuration order; the event is neither copied nor re-sent.
/// Failures are collected and, once all sinks have been attempted, reported to the caller:
/// a single failure is rethrown as-is, preserving the original cause, while multiple failures
/// are aggregated into an <see cref="AggregateException"/> in configuration order. Failures
/// encountered while disposing the wrapped sinks are aggregated and reported separately from
/// emission failures.
/// </summary>
public sealed class AggregatingSink : ILogEventSink, IDisposable
#if FEATURE_ASYNCDISPOSABLE
    , IAsyncDisposable
#endif
{
    readonly ILogEventSink[] _sinks;

    /// <summary>
    /// Create an <see cref="AggregatingSink"/> that writes to the specified sinks.
    /// </summary>
    /// <param name="sinks">The sinks to write each event to, in order.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="sinks"/> is <code>null</code>.</exception>
    public AggregatingSink(IEnumerable<ILogEventSink> sinks)
    {
        _sinks = Guard.AgainstNull(sinks).ToArray();
    }

    /// <summary>
    /// Emit the provided log event to every sink. All sinks are attempted once; failures are
    /// then rethrown, either directly when a single sink failed, or aggregated in configuration
    /// order when multiple sinks failed.
    /// </summary>
    /// <param name="logEvent">The log event to emit. The same instance is passed to every sink.</param>
    public void Emit(LogEvent logEvent)
    {
        List<Exception>? exceptions = null;
        foreach (var sink in _sinks)
        {
            try
            {
                sink.Emit(logEvent);
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine("Caught exception while emitting to sink {0}: {1}", sink, ex);
                exceptions ??= [];
                exceptions.Add(ex);
            }
        }

        RethrowIfAny(exceptions, "Failed to emit a log event to one or more sinks.");
    }

    /// <summary>
    /// Dispose all wrapped sinks that implement <see cref="IDisposable"/>. Failures are aggregated
    /// and reported separately from emission failures.
    /// </summary>
    public void Dispose()
    {
        List<Exception>? exceptions = null;
        foreach (var sink in _sinks)
        {
            if (sink is not IDisposable disposable) continue;

            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine("Caught exception while disposing sink {0}: {1}", sink, ex);
                exceptions ??= [];
                exceptions.Add(ex);
            }
        }

        RethrowIfAny(exceptions, "Failed to dispose one or more sinks.");
    }

#if FEATURE_ASYNCDISPOSABLE
    /// <summary>
    /// Dispose all wrapped sinks that implement <see cref="IAsyncDisposable"/> or <see cref="IDisposable"/>.
    /// Failures are aggregated and reported separately from emission failures.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        List<Exception>? exceptions = null;
        foreach (var sink in _sinks)
        {
            try
            {
                if (sink is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                else if (sink is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine("Caught exception while disposing sink {0}: {1}", sink, ex);
                exceptions ??= [];
                exceptions.Add(ex);
            }
        }

        RethrowIfAny(exceptions, "Failed to dispose one or more sinks.");
    }
#endif

    static void RethrowIfAny(List<Exception>? exceptions, string aggregateMessage)
    {
        if (exceptions == null)
            return;

        // A single failure keeps its original cause (and stack trace) intact.
        if (exceptions.Count == 1)
            ExceptionDispatchInfo.Capture(exceptions[0]).Throw();

        throw new AggregateException(aggregateMessage, exceptions);
    }
}
