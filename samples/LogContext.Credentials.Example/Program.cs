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

using Serilog;
using Serilog.Context;
using Serilog.Core;
using Serilog.Events;

// This example demonstrates the scope-credential behavior of LogContext. Each Push returns a
// credential (an IDisposable) that is bound to the current async logical call context. Disposing a
// credential that is not the current top of the stack (out-of-order), or disposing the same
// credential twice (duplicate), is rejected and does not affect any other scope.

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Sink(new ConsoleSink())
    .CreateLogger();

try
{
    // 1. Normal push/dispose: the outer scope is restored after the inner scope is disposed.
    using (LogContext.PushProperty("Outer", "out"))
    {
        Log.Information("Outer scope active");

        using (LogContext.PushProperty("Inner", "in"))
        {
            Log.Information("Inner scope active");
        }

        Log.Information("Inner scope disposed, outer restored");
    }

    Log.Information("Both scopes disposed");

    // 2. Out-of-order dispose is rejected and does not damage the other scopes.
    var outer = LogContext.PushProperty("Outer", "out");
    var inner = LogContext.PushProperty("Inner", "in");

    try
    {
        outer.Dispose(); // out of order: inner is still on top
    }
    catch (InvalidOperationException ex)
    {
        Log.Warning("Out-of-order dispose rejected: {Message}", ex.Message);
    }

    // The inner scope is still intact.
    Log.Information("After rejected out-of-order dispose: Outer={Outer}, Inner={Inner}", "out", "in");

    // Dispose in order to clean up.
    inner.Dispose();
    outer.Dispose();

    // 3. Duplicate dispose is rejected.
    var scope = LogContext.PushProperty("Scope", "one");
    scope.Dispose();

    try
    {
        scope.Dispose(); // duplicate
    }
    catch (ObjectDisposedException ex)
    {
        Log.Warning("Duplicate dispose rejected: {Message}", ex.Message);
    }

    // 4. Scopes flow across await and are restored after the continuation.
    using (LogContext.PushProperty("Flow", "yes"))
    {
        await Task.Yield();
        Log.Information("Scope flowed across await: Flow={Flow}", "yes");
    }

    Log.Information("Scope restored after await");
}
finally
{
    Log.CloseAndFlush();
}

sealed class ConsoleSink : ILogEventSink
{
    public void Emit(LogEvent logEvent)
    {
        var properties = string.Join(" ", logEvent.Properties.Select(p => $"{p.Key}={p.Value}"));
        Console.WriteLine($"[{logEvent.Timestamp:HH:mm:ss} {logEvent.Level}] {logEvent.RenderMessage()} {properties}");
    }
}
