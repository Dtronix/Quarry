using System;
using Quarry;

namespace Quarry.Tests.Samples;

/// <summary>
/// Schema for testing DateTimeOffset, TimeSpan, DateOnly, and TimeOnly
/// reader method generation (GetFieldValue&lt;T&gt; types).
/// </summary>
public class EventSchema : Schema
{
    public static string Table => "events";

    public Key<int> EventId => Identity();
    public Col<string> EventName => Length(200);
    public Col<DateTimeOffset> ScheduledAt { get; }
    public Col<DateTimeOffset?> CancelledAt { get; }
}
