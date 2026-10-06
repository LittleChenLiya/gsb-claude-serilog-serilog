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
/// </example>
/// <remarks>The scope of the context is the current logical thread, using AsyncLocal
/// (and so is preserved across async/await calls). Tokens returned by <c>Push</c> and
/// <see cref="Suspend"/> are bound to the logical async context in which they were created
/// and must be disposed in reverse (LIFO) order; out-of-order or repeated disposal is
/// rejected with an <see cref="InvalidOperationException"/> and leaves all other scopes intact.</remarks>
public static class LogContext
{
    static readonly AsyncLocal<EnricherStack?> Data = new();
    static readonly AsyncLocal<ContextStackBookmark?> ScopeData = new();

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

        var stack = GetOrCreateEnricherStack();
        var bookmark = new ContextStackBookmark(stack, CurrentScope);

        Enrichers = stack.Push(enricher);
        CurrentScope = bookmark;

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
        var stack = GetOrCreateEnricherStack();
        var bookmark = new ContextStackBookmark(stack, CurrentScope);

        foreach (var enricher in enrichers)
        {
            stack = stack.Push(enricher);
        }

        Enrichers = stack;
        CurrentScope = bookmark;

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

        var stack = GetOrCreateEnricherStack();
        var bookmark = new ContextStackBookmark(stack, CurrentScope);

        foreach (var enricher in enrichers)
        {
            stack = stack.Push(enricher);
        }

        Enrichers = stack;
        CurrentScope = bookmark;

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
        var stack = GetOrCreateEnricherStack();
        var bookmark = new ContextStackBookmark(stack, CurrentScope);

        Enrichers = EnricherStack.Empty;
        CurrentScope = bookmark;

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

    sealed class ContextStackBookmark : IDisposable
    {
        readonly EnricherStack _bookmark;
        readonly ContextStackBookmark? _parent;
        bool _disposed;

        public ContextStackBookmark(EnricherStack bookmark, ContextStackBookmark? parent)
        {
            _bookmark = bookmark;
            _parent = parent;
        }

        public void Dispose()
        {
            if (_disposed)
                throw new InvalidOperationException("This LogContext scope has already been disposed.");

            if (!ReferenceEquals(CurrentScope, this))
                throw new InvalidOperationException(
                    "LogContext scopes must be disposed in reverse order of creation, within the logical async context that created them.");

            _disposed = true;
            Enrichers = _bookmark;
            CurrentScope = _parent;
        }
    }

    static EnricherStack? Enrichers
    {
        get => Data.Value;
        set => Data.Value = value;
    }

    static ContextStackBookmark? CurrentScope
    {
        get => ScopeData.Value;
        set => ScopeData.Value = value;
    }
}

