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

namespace Serilog.Core.Sinks;

/// <summary>
/// Forwards log events to another sink, exposing only a selected set of top-level
/// properties. The original events are not modified, so other sinks still observe
/// all properties. Property values are shared with the original event and are
/// expected to be immutable, so structured values are preserved as-is.
/// </summary>
sealed class ProjectingSink : ILogEventSink, IDisposable
#if FEATURE_ASYNCDISPOSABLE
    , IAsyncDisposable
#endif
{
    readonly ILogEventSink _wrapped;
    readonly HashSet<string> _selectedProperties;

    public ProjectingSink(ILogEventSink wrapped, IEnumerable<string> selectedProperties)
    {
        _wrapped = Guard.AgainstNull(wrapped);
        _selectedProperties = new HashSet<string>(Guard.AgainstNull(selectedProperties));
    }

    public void Emit(LogEvent logEvent)
    {
        Guard.AgainstNull(logEvent);

        var properties = new Dictionary<string, LogEventPropertyValue>();
        foreach (var property in logEvent.Properties)
        {
            if (_selectedProperties.Contains(property.Key))
                properties.Add(property.Key, property.Value);
        }

        var projected = LogEvent.UnstableAssembleFromParts(
            logEvent.Timestamp,
            logEvent.Level,
            logEvent.Exception,
            logEvent.MessageTemplate,
            properties,
            logEvent.TraceId ?? default,
            logEvent.SpanId ?? default);

        _wrapped.Emit(projected);
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
