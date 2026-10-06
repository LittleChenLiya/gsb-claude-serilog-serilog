// Copyright 2013-2015 Serilog Contributors
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

namespace Serilog.Context;

/// <summary>
/// Holds ambient properties that can be attached to log events. To
/// configure, use the <see cref="Serilog.Configuration.LoggerEnrichmentConfiguration.FromLogContext"/> method.
/// </summary>
/// <example>
/// Configuration:
/// <code lang="C#">
/// var log = new LoggerConfiguration()
///     .Enrich.FromLogContext()
///     ...
/// </code>
/// Usage:
/// <code lang="C#">
/// using (LogContext.PushProperty("MessageId", message.Id))
/// {
///     Log.Information("The MessageId property will be attached to this event");
/// }
/// </code>
/// The scope returned by <see cref="PushProperty"/> is a credential bound to the current async
/// logical call context, so it flows across <c>await</c> points. Disposing a credential that is not
/// the current top of the stack (out-of-order dispose), or disposing the same credential twice
/// (duplicate dispose), is rejected and does not affect any other scope:
/// <code lang="C#">
/// var outer = LogContext.PushProperty("Outer", 1);
/// var inner = LogContext.PushProperty("Inner", 2);
///
/// outer.Dispose(); // Throws InvalidOperationException: inner is still on top
///
/// inner.Dispose(); // OK: inner is popped first
/// outer.Dispose(); // OK: outer is popped next
///
/// outer.Dispose(); // Throws ObjectDisposedException: already disposed
/// </code>
/// </example>
/// <remarks>The scope of the context is the current logical thread, using AsyncLocal
/// (and so is preserved across async/await calls).</remarks>
public static class LogContext
{
    static readonly AsyncLocal<EnricherStack?> Data = new();

    /// <summary>
    /// Push a property onto the context, returning an <see cref="IDisposable"/>
    /// that must later be used to remove the property, along with any others that
    /// may have been pushed on top of it and not yet popped. The property must
    /// be popped from the same thread/logical call context.
    /// </summary>
    /// <param name="name">The name of the property.</param>
    /// <param name="value">The value of the property.</param>
    /// <returns>A handle to later remove the property from the context.</returns>
    /// <param name="destructureObjects">If <see langword="true"/>, and the value is a non-primitive, non-array type,
    /// then the value will be converted to a structure; otherwise, unknown types will
    /// be converted to scalars, which are generally stored as strings.</param>
    /// <returns>A token that must be disposed, in order, to pop properties back off the stack.</returns>
    public static IDisposable PushProperty(string name, object? value, bool destructureObjects = false)
    {
        return Push(new PropertyEnricher(name, value, destructureObjects));
    }

    /// <summary>
    /// Push an enricher onto the context, returning an <see cref="IDisposable"/>
    /// that must later be used to remove the property, along with any others that
    /// may have been pushed on top of it and not yet popped. The property must
    /// be popped from the same thread/logical call context.
    /// </summary>
    /// <param name="enricher">An enricher to push onto the log context</param>
    /// <returns>A token that must be disposed, in order, to pop properties back off the stack.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="enricher"/> is <code>null</code></exception>
    [OverloadResolutionPriority(3)]
    public static IDisposable Push(ILogEventEnricher enricher)
    {
        Guard.AgainstNull(enricher);

        var previous = GetOrCreateEnricherStack();
        var expected = previous.Push(enricher);
        var bookmark = new ContextStackBookmark(previous, expected);

        Enrichers = expected;

        return bookmark;
    }

    /// <summary>
    /// Push multiple enrichers onto the context, returning an <see cref="IDisposable"/>
    /// that must later be used to remove the property, along with any others that
    /// may have been pushed on top of it and not yet popped. The property must
    /// be popped from the same thread/logical call context.
    /// </summary>
    /// <seealso cref="PropertyEnricher"/>.
    /// <param name="enrichers">Enrichers to push onto the log context</param>
    /// <returns>A token that must be disposed, in order, to pop properties back off the stack.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="enrichers"/> is <code>null</code></exception>
    [OverloadResolutionPriority(0)]
    public static IDisposable Push(params ILogEventEnricher[] enrichers)
    {
        Guard.AgainstNull(enrichers);
        
        return Push(enrichers.AsSpan());
    }

    /// <summary>
    /// Push multiple enrichers onto the context, returning an <see cref="IDisposable"/>
    /// that must later be used to remove the property, along with any others that
    /// may have been pushed on top of it and not yet popped. The property must
    /// be popped from the same thread/logical call context.
    /// </summary>
    /// <seealso cref="PropertyEnricher"/>.
    /// <param name="enrichers">Enrichers to push onto the log context</param>
    /// <returns>A token that must be disposed, in order, to pop properties back off the stack.</returns>
    [OverloadResolutionPriority(2)]
    public static IDisposable Push(params ReadOnlySpan<ILogEventEnricher> enrichers)
    {
        var previous = GetOrCreateEnricherStack();
        var expected = previous;

        foreach (var enricher in enrichers)
        {
            expected = expected.Push(enricher);
        }

        var bookmark = new ContextStackBookmark(previous, expected);

        Enrichers = expected;

        return bookmark;
    }

    /// <summary>
    /// Push multiple enrichers onto the context, returning an <see cref="IDisposable"/>
    /// that must later be used to remove the property, along with any others that
    /// may have been pushed on top of it and not yet popped. The property must
    /// be popped from the same thread/logical call context.
    /// </summary>
    /// <seealso cref="PropertyEnricher"/>.
    /// <param name="enrichers">Enrichers to push onto the log context</param>
    /// <returns>A token that must be disposed, in order, to pop properties back off the stack.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="enrichers"/> is <code>null</code></exception>
    [OverloadResolutionPriority(1)]
    public static IDisposable Push(params IEnumerable<ILogEventEnricher> enrichers)
    {
        Guard.AgainstNull(enrichers);

        var previous = GetOrCreateEnricherStack();
        var expected = previous;

        foreach (var enricher in enrichers)
        {
            expected = expected.Push(enricher);
        }

        var bookmark = new ContextStackBookmark(previous, expected);

        Enrichers = expected;

        return bookmark;
    }

    /// <summary>
    /// Obtain an enricher that represents the current contents of the <see cref="LogContext"/>. This
    /// can be pushed back onto the context in a different location/thread when required.
    /// </summary>
    /// <returns>An enricher that represents the current contents of the <see cref="LogContext"/>.</returns>
    public static ILogEventEnricher Clone()
    {
        var stack = GetOrCreateEnricherStack();
        return new SafeAggregateEnricher(stack);
    }

    /// <summary>
    /// Remove all enrichers from the <see cref="LogContext"/>, returning an <see cref="IDisposable"/>
    /// that must later be used to restore enrichers that were on the stack before <see cref="Suspend"/> was called.
    /// </summary>
    /// <returns>A token that must be disposed, in order, to restore properties back to the stack.</returns>
    public static IDisposable Suspend()
    {
        var previous = GetOrCreateEnricherStack();
        var bookmark = new ContextStackBookmark(previous, EnricherStack.Empty);

        Enrichers = EnricherStack.Empty;

        return bookmark;
    }

    /// <summary>
    /// Remove all enrichers from <see cref="LogContext"/> for the current async scope.
    /// </summary>
    public static void Reset()
    {
        if (Enrichers != null && Enrichers != EnricherStack.Empty)
        {
            Enrichers = EnricherStack.Empty;
        }
    }

    static EnricherStack GetOrCreateEnricherStack()
    {
        var enrichers = Enrichers;
        if (enrichers == null)
        {
            enrichers = EnricherStack.Empty;
            Enrichers = enrichers;
        }
        return enrichers;
    }

    internal static void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var enrichers = Enrichers;
        if (enrichers == null || enrichers == EnricherStack.Empty)
            return;

        foreach (var enricher in enrichers)
        {
            enricher.Enrich(logEvent, propertyFactory);
        }
    }

    /// <summary>
    /// An <see cref="IDisposable"/> that acts as a credential for a pushed scope. Disposing the
    /// bookmark removes the scope it represents, along with any scopes pushed on top of it.
    /// </summary>
    /// <remarks>
    /// The bookmark is bound to the current async logical call context via <see cref="AsyncLocal{T}"/>,
    /// so the scope it represents flows across <c>await</c> points. Disposing a bookmark that is not the
    /// current top of the stack (out-of-order dispose), or disposing the same bookmark twice (duplicate
    /// dispose), is rejected and does not affect any other scope.
    /// </remarks>
    sealed class ContextStackBookmark : IDisposable
    {
        readonly EnricherStack _previous;
        readonly EnricherStack _expected;
        bool _disposed;

        public ContextStackBookmark(EnricherStack previous, EnricherStack expected)
        {
            _previous = previous;
            _expected = expected;
        }

        public void Dispose()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ContextStackBookmark), "The scope has already been disposed.");

            // The bookmark is only valid if the stack it represents is still the current top. If any
            // other scope was pushed on top and not yet popped, the stack will differ from the one we
            // expect, and disposing now would remove that scope too. Reject instead.
            if (Enrichers != _expected)
                throw new InvalidOperationException("The scope cannot be disposed because other scopes have been pushed on top of it. Dispose scopes in the reverse order of their creation.");

            _disposed = true;
            Enrichers = _previous;
        }
    }

    static EnricherStack? Enrichers
    {
        get => Data.Value;
        set => Data.Value = value;
    }
}

