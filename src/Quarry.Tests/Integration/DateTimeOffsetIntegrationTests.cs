using Quarry.Tests.Samples;

namespace Quarry.Tests.Integration;

/// <summary>
/// Integration tests verifying that DateTimeOffset columns round-trip correctly
/// via the generated GetFieldValue&lt;DateTimeOffset&gt; reader method.
/// </summary>
[TestFixture]
internal class DateTimeOffsetIntegrationTests
{
    [Test]
    public async Task SelectEntity_DateTimeOffset_RoundTrips()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var events = await Lite.Events()
            .Select(e => e)
            .ExecuteFetchAllAsync();

        Assert.That(events, Has.Count.EqualTo(2));
        Assert.That(events[0].EventName, Is.EqualTo("Launch"));
        Assert.That(events[0].ScheduledAt, Is.EqualTo(new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero)));
        Assert.That(events[0].CancelledAt, Is.Null);

        Assert.That(events[1].EventName, Is.EqualTo("Review"));
        Assert.That(events[1].ScheduledAt, Is.EqualTo(new DateTimeOffset(2024, 7, 1, 14, 0, 0, TimeSpan.FromHours(2))));
        Assert.That(events[1].CancelledAt, Is.EqualTo(new DateTimeOffset(2024, 6, 28, 9, 0, 0, TimeSpan.FromHours(2))));
    }

    [Test]
    public async Task InsertThenSelect_DateTimeOffset_RoundTrips()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var scheduled = new DateTimeOffset(2025, 3, 15, 9, 0, 0, TimeSpan.FromHours(-5));
        await Lite.Events().Insert(new Event
        {
            EventName = "Deployment",
            ScheduledAt = scheduled,
            CancelledAt = null
        }).ExecuteNonQueryAsync();

        var result = await Lite.Events()
            .Where(e => e.EventName == "Deployment")
            .Select(e => e)
            .ExecuteFetchFirstAsync();

        Assert.That(result.ScheduledAt, Is.EqualTo(scheduled));
        Assert.That(result.CancelledAt, Is.Null);
    }

    [Test]
    public async Task InsertThenSelect_NullableDateTimeOffset_RoundTrips()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var scheduled = new DateTimeOffset(2025, 4, 1, 12, 0, 0, TimeSpan.Zero);
        var cancelled = new DateTimeOffset(2025, 3, 28, 8, 30, 0, TimeSpan.Zero);
        await Lite.Events().Insert(new Event
        {
            EventName = "Cancelled Meeting",
            ScheduledAt = scheduled,
            CancelledAt = cancelled
        }).ExecuteNonQueryAsync();

        var result = await Lite.Events()
            .Where(e => e.EventName == "Cancelled Meeting")
            .Select(e => e)
            .ExecuteFetchFirstAsync();

        Assert.That(result.ScheduledAt, Is.EqualTo(scheduled));
        Assert.That(result.CancelledAt, Is.EqualTo(cancelled));
    }
}
