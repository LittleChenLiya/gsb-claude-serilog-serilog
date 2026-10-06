# LogContext scoped tokens

`LogContext.PushProperty`/`Push`/`Suspend` return a scope token (`IDisposable`) that is
bound to the logical async context in which it was created. The binding flows across
`await` points, and disposing the token restores the enclosing (outer) scope.

```csharp
using Serilog.Context;

async Task HandleRequestAsync(string tenantId)
{
    using (LogContext.PushProperty("TenantId", tenantId)) // outer scope
    {
        await LoadDataAsync(); // TenantId is still attached after the await

        using (LogContext.PushProperty("TenantId", "override")) // inner scope
        {
            Log.Information("TenantId is 'override' here");
        } // exiting the inner scope restores the outer value

        Log.Information("TenantId is {TenantId} again", tenantId);
    }
}
```

## Ordering rules

Tokens must be disposed in reverse order of creation (LIFO) within the same logical
async context:

- **Out-of-order disposal** throws `InvalidOperationException` and changes nothing —
  all scopes, including the one the token belongs to, remain intact. The token can
  still be disposed later, once it is top-of-stack again.
- **Repeated disposal** of the same token throws `InvalidOperationException` and
  leaves the remaining scopes untouched.

```csharp
var outer = LogContext.PushProperty("A", 1);
var inner = LogContext.PushProperty("B", 2);

outer.Dispose(); // throws InvalidOperationException; nothing is popped
inner.Dispose(); // OK: pops B
outer.Dispose(); // OK: pops A
outer.Dispose(); // throws InvalidOperationException: already disposed
```

Parallel logical flows (e.g. two tasks) each get their own independent scope stack,
so scopes pushed with the same property key never leak between tasks.
