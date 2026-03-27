# Logging

Quarry uses [Logsmith](https://www.nuget.org/packages/Logsmith) in **Abstraction mode** for structured logging. In this mode, Logsmith source-generates all logging types -- `ILogsmithLogger`, `LogEntry`, `LogLevel`, `LogsmithOutput`, and per-category log classes -- directly into the `Quarry.Logging` namespace at compile time. No Logsmith DLL ships with Quarry and no Logsmith package reference is required in consumer projects.

This means logging is zero-dependency: Quarry internally calls generated static methods like `QueryLog.SqlGenerated(opId, sql)`, and Logsmith's source generator turns those into UTF-8 message formatting and a dispatch to `LogsmithOutput.Logger`. If no logger is assigned, the calls are effectively no-ops gated behind a null check.

## The ILogsmithLogger Interface

All Quarry log output flows through a single interface:

```csharp
namespace Quarry.Logging;

public interface ILogsmithLogger
{
    bool IsEnabled(LogLevel level, string category);
    void Write(in LogEntry entry, ReadOnlySpan<byte> utf8Message);
}
```

**`IsEnabled`** is called before any message formatting. If it returns `false`, the log entry is skipped entirely -- no string allocation, no UTF-8 encoding. This is the primary mechanism for filtering by level or category.

**`Write`** receives a `LogEntry` (a read-only struct) and the pre-formatted message as a `ReadOnlySpan<byte>` in UTF-8. The `LogEntry` struct contains:

| Property    | Type         | Description                                        |
|-------------|--------------|----------------------------------------------------|
| `Level`     | `LogLevel`   | Severity of the entry (Trace through Error)        |
| `Category`  | `string`     | Log category (e.g. `"Quarry.Query"`)               |
| `Exception` | `Exception?` | Attached exception, if any (e.g. on query failure) |

The message arrives as UTF-8 bytes rather than a `string` to avoid unnecessary allocations on the hot path. Convert with `Encoding.UTF8.GetString(utf8Message)` when needed.

## Setup

Implement `ILogsmithLogger` and assign it to `LogsmithOutput.Logger`:

```csharp
using Quarry.Logging;

LogsmithOutput.Logger = new ConsoleLogger();

sealed class ConsoleLogger : ILogsmithLogger
{
    public bool IsEnabled(LogLevel level, string category) => level >= LogLevel.Debug;

    public void Write(in LogEntry entry, ReadOnlySpan<byte> utf8Message)
    {
        Console.WriteLine($"[{entry.Level}] {entry.Category}: {Encoding.UTF8.GetString(utf8Message)}");
    }
}
```

To disable logging, set `LogsmithOutput.Logger = null` (the default). `LogsmithOutput.Logger` is a process-wide singleton, so set it once at startup before any Quarry operations.

## Log Levels

Quarry defines six log levels, matching the standard severity progression:

| Level         | Typical use in Quarry                                      |
|---------------|------------------------------------------------------------|
| `Trace`       | Parameter values bound to queries (`Quarry.Parameters`)    |
| `Debug`       | SQL generated, fetch/modification completion               |
| `Information` | Connection opened/closed, migration progress               |
| `Warning`     | Slow query detection, migration cautionary notices         |
| `Error`       | Query or modification failures (with attached `Exception`) |
| `None`        | Disables logging for the category                          |

### Filtering with IsEnabled

The `IsEnabled` method receives both the level and the category, giving you fine-grained control. Quarry calls `IsEnabled` before every log operation, so returning `false` skips all formatting work.

```csharp
sealed class FilteredLogger : ILogsmithLogger
{
    public bool IsEnabled(LogLevel level, string category)
    {
        // Suppress noisy parameter logging in production
        if (category == "Quarry.Parameters")
            return false;

        // Only show warnings and above for query logs
        if (category == "Quarry.Query")
            return level >= LogLevel.Warning;

        // Default: Information and above
        return level >= LogLevel.Information;
    }

    public void Write(in LogEntry entry, ReadOnlySpan<byte> utf8Message)
    {
        Console.WriteLine(Encoding.UTF8.GetString(utf8Message));
    }
}
```

## Log Categories

| Category             | Default Level | What it logs                                                            |
|----------------------|---------------|-------------------------------------------------------------------------|
| `Quarry.Connection`  | Information   | Connection opened/closed                                                |
| `Quarry.Query`       | Debug         | SQL generated, fetch completion (row count + elapsed time), scalar results |
| `Quarry.Modify`      | Debug         | SQL generated, modification completion (operation + row count + elapsed time) |
| `Quarry.RawSql`      | Debug         | SQL generated, fetch/non-query/scalar completion                        |
| `Quarry.Parameters`  | Trace         | Parameter values bound to queries (`@p0 = value`)                       |
| `Quarry.Execution`   | Warning       | Slow query detection (elapsed time + SQL)                               |
| `Quarry.Migration`   | Information   | Migration applying/applied/rolled back, dry run, SQL generated          |

## Integrating with Microsoft.Extensions.Logging

Bridge Quarry's logging into `ILoggerFactory` so that Quarry log entries flow through the same pipeline as the rest of your application (console, Serilog sinks, Application Insights, etc.):

```csharp
using System.Text;
using Quarry.Logging;
using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;

public sealed class LogsmithBridge(ILoggerFactory loggerFactory) : ILogsmithLogger
{
    public bool IsEnabled(Quarry.Logging.LogLevel level, string category)
    {
        var logger = loggerFactory.CreateLogger(category);
        return logger.IsEnabled(MapLevel(level));
    }

    public void Write(in LogEntry entry, ReadOnlySpan<byte> utf8Message)
    {
        var logger = loggerFactory.CreateLogger(entry.Category);
        var msLevel = MapLevel(entry.Level);

        if (!logger.IsEnabled(msLevel))
            return;

        var message = Encoding.UTF8.GetString(utf8Message);
        logger.Log(msLevel, entry.Exception, "{Message}", message);
    }

    private static MsLogLevel MapLevel(Quarry.Logging.LogLevel level) => level switch
    {
        Quarry.Logging.LogLevel.Trace => MsLogLevel.Trace,
        Quarry.Logging.LogLevel.Debug => MsLogLevel.Debug,
        Quarry.Logging.LogLevel.Information => MsLogLevel.Information,
        Quarry.Logging.LogLevel.Warning => MsLogLevel.Warning,
        Quarry.Logging.LogLevel.Error => MsLogLevel.Error,
        _ => MsLogLevel.None,
    };
}
```

Wire it up at startup:

```csharp
var app = builder.Build();
var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
LogsmithOutput.Logger = new LogsmithBridge(loggerFactory);
```

Once connected, Quarry categories appear as standard `Microsoft.Extensions.Logging` categories. You can configure their minimum levels through `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Quarry.Query": "Debug",
      "Quarry.Parameters": "Warning",
      "Quarry.Execution": "Warning"
    }
  }
}
```

### Serilog Bridge

If you use Serilog directly (without `Microsoft.Extensions.Logging`), the bridge is similar:

```csharp
using System.Text;
using Quarry.Logging;
using Serilog;
using Serilog.Events;

public sealed class SerilogBridge(Serilog.ILogger logger) : ILogsmithLogger
{
    public bool IsEnabled(Quarry.Logging.LogLevel level, string category)
        => logger.ForContext("Category", category).IsEnabled(MapLevel(level));

    public void Write(in LogEntry entry, ReadOnlySpan<byte> utf8Message)
    {
        var contextLogger = logger.ForContext("Category", entry.Category);
        var serilogLevel = MapLevel(entry.Level);

        if (!contextLogger.IsEnabled(serilogLevel))
            return;

        var message = Encoding.UTF8.GetString(utf8Message);

        if (entry.Exception is { } ex)
            contextLogger.Write(serilogLevel, ex, "{Message}", message);
        else
            contextLogger.Write(serilogLevel, "{Message}", message);
    }

    private static LogEventLevel MapLevel(Quarry.Logging.LogLevel level) => level switch
    {
        Quarry.Logging.LogLevel.Trace => LogEventLevel.Verbose,
        Quarry.Logging.LogLevel.Debug => LogEventLevel.Debug,
        Quarry.Logging.LogLevel.Information => LogEventLevel.Information,
        Quarry.Logging.LogLevel.Warning => LogEventLevel.Warning,
        Quarry.Logging.LogLevel.Error => LogEventLevel.Error,
        _ => LogEventLevel.Fatal,
    };
}
```

## Operation Correlation

Every query and modification is assigned a monotonically increasing operation ID (`opId`) via `OpId.Next()`. All log entries from the same operation -- SQL generation, parameter binding, completion, and slow query warnings -- share the same `opId`, which appears as a `[N]` prefix in the log message.

This enables you to correlate related log entries even when multiple queries execute concurrently. For example, a single `ExecuteFetchAllAsync` call with a parameterized WHERE clause produces:

```
[Quarry.Query]      [42] SQL: SELECT "UserId", "UserName" FROM "users" WHERE "UserId" = @p0
[Quarry.Parameters] [42] @p0 = 1
[Quarry.Query]      [42] Fetched 1 rows in 0.3ms
```

All three entries share `opId` 42. A second query running concurrently would get a different opId (e.g. 43), so you can filter or group entries by their `[N]` prefix to isolate a single operation:

```
[Quarry.Query]      [42] SQL: SELECT "UserId", "UserName" FROM "users" WHERE "UserId" = @p0
[Quarry.Query]      [43] SQL: SELECT "OrderId", "Total" FROM "orders" WHERE "UserId" = @p0
[Quarry.Parameters] [42] @p0 = 1
[Quarry.Parameters] [43] @p0 = 5
[Quarry.Query]      [42] Fetched 1 rows in 0.3ms
[Quarry.Query]      [43] Fetched 3 rows in 0.5ms
```

Even with interleaved output, the opId prefix makes it straightforward to reconstruct the full timeline for operation 42 vs 43.

## Slow Query Detection

```csharp
db.SlowQueryThreshold = TimeSpan.FromSeconds(1); // default: 500ms
db.SlowQueryThreshold = null;                    // disable
```

When a query's elapsed time exceeds the threshold, a `Warning`-level entry is emitted on the `Quarry.Execution` category with the elapsed time and the SQL text:

```
[Quarry.Execution] [42] Slow query (1205ms): SELECT "UserId", "UserName" FROM "users" WHERE ...
```

## Sensitive Parameter Redaction

Mark columns with the `Sensitive()` modifier in the schema to redact their parameter values in all log output. This prevents secrets, passwords, tokens, and other sensitive data from appearing in logs regardless of the configured log level.

```csharp
public class WidgetSchema : Schema
{
    public static string Table => "widgets";

    public Key<Guid> WidgetId => ClientGenerated();
    public Col<string> WidgetName => Length(100);
    public Col<string> Secret => Length(200).Sensitive();  // redacted in logs
}
```

When a sensitive column is bound as a parameter, the generator emits a call to `ParameterLog.BoundSensitive` instead of `ParameterLog.Bound`. The actual value is never passed to the logging infrastructure:

```
[Quarry.Parameters] [42] @p0 = Gizmo
[Quarry.Parameters] [42] @p1 = [SENSITIVE]
```

The redaction applies to all operations -- queries, inserts, updates -- anywhere the sensitive column appears as a parameter.
