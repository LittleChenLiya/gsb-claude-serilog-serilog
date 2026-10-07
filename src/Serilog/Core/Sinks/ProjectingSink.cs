// Copyright 2013-2026 Serilog Contributors
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

namespace Serilog.Core.Sinks;

/// <summary>
/// Forwards log events to another sink, projecting each event down to an explicit
/// selection of top-level properties. The original event is left unmodified, so
/// other sinks in the pipeline still observe all of its properties.
/// </summary>
/// <remarks>
/// The projected event retains the original timestamp, level, exception, message
/// template, and trace/span ids. Property values are shared by reference with the
/// original event, so structured values are preserved as-is and never converted to
/// strings. Message template tokens bound to selected properties render normally;
/// tokens whose properties were not selected render in their original, unbound
/// (raw text) form.
/// </remarks>
sealed class ProjectingSink : ILogEventSink, IDisposable
#if FEATURE_ASYNCDISPOSABLE
    , IAsyncDisposable
#endif
{
    readonly ILogEventSink _wrapped;
    readonly string[] _propertyNames;

    public ProjectingSink(ILogEventSink wrapped, IEnumerable<string> propertyNames)
    {
        _wrapped = Guard.AgainstNull(wrapped);
        _propertyNames = Guard.AgainstNull(propertyNames).ToArray();

        foreach (var propertyName in _propertyNames)
            Guard.AgainstNull(propertyName, nameof(propertyNames));
    }

    public void Emit(LogEvent logEvent)
    {
        Guard.AgainstNull(logEvent);

        var projected = new Dictionary<string, LogEventPropertyValue>(_propertyNames.Length);
        foreach (var propertyName in _propertyNames)
        {
            if (logEvent.Properties.TryGetValue(propertyName, out var value))
                projected[propertyName] = value;
        }

        var projectedEvent = LogEvent.UnstableAssembleFromParts(
            logEvent.Timestamp,
            logEvent.Level,
            logEvent.Exception,
            logEvent.MessageTemplate,
            projected,
            logEvent.TraceId ?? default,
            logEvent.SpanId ?? default);

        _wrapped.Emit(projectedEvent);
    }

    public void Dispose()
    {
        (_wrapped as IDisposable)?.Dispose();
    }

#if FEATURE_ASYNCDISPOSABLE
    public ValueTask DisposeAsync()
    {
        if (_wrapped is IAsyncDisposable asyncDisposable)
            return asyncDisposable.DisposeAsync();

        Dispose();
        return default;
    }
#endif
}
