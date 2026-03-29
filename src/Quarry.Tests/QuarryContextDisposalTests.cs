using System.Data;
using Microsoft.Data.Sqlite;

namespace Quarry.Tests;

/// <summary>
/// Tests for QuarryContext disposal semantics (item 5.1).
/// Verifies connection state restoration and idempotent disposal.
/// </summary>
[TestFixture]
public class QuarryContextDisposalTests
{
    private sealed class MinimalContext : QuarryContext
    {
        public MinimalContext(IDbConnection connection) : base(connection) { }
        public MinimalContext(IDbConnection connection, bool ownsConnection) : base(connection, ownsConnection) { }
    }

    [Test]
    public void Dispose_ConnectionWasClosed_ClosesConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        // Connection starts closed
        Assert.That(connection.State, Is.EqualTo(ConnectionState.Closed));

        var ctx = new MinimalContext(connection);
        connection.Open(); // simulate Quarry opening it

        ctx.Dispose();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Closed));
    }

    [Test]
    public void Dispose_ConnectionWasOpen_LeavesConnectionOpen()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open(); // open BEFORE creating context

        var ctx = new MinimalContext(connection);

        ctx.Dispose();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
        connection.Close();
    }

    [Test]
    public void Dispose_DoubleDispose_DoesNotThrow()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        var ctx = new MinimalContext(connection);
        connection.Open();

        ctx.Dispose();
        Assert.DoesNotThrow(() => ctx.Dispose());
    }

    [Test]
    public async Task DisposeAsync_ConnectionWasClosed_ClosesConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        var ctx = new MinimalContext(connection);
        connection.Open();

        await ctx.DisposeAsync();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Closed));
    }

    [Test]
    public async Task DisposeAsync_ConnectionWasOpen_LeavesConnectionOpen()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var ctx = new MinimalContext(connection);

        await ctx.DisposeAsync();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
        connection.Close();
    }

    [Test]
    public async Task DisposeAsync_DoubleDispose_DoesNotThrow()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        var ctx = new MinimalContext(connection);
        connection.Open();

        await ctx.DisposeAsync();
        Assert.DoesNotThrowAsync(async () => await ctx.DisposeAsync());
    }

    [Test]
    public void Dispose_OwnsConnection_DisposesConnection()
    {
        var connection = new TrackingConnection("Data Source=:memory:");
        var ctx = new MinimalContext(connection, ownsConnection: true);
        connection.Open();

        ctx.Dispose();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Closed));
        Assert.That(connection.DisposeCallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task DisposeAsync_OwnsConnection_DisposesConnection()
    {
        var connection = new TrackingConnection("Data Source=:memory:");
        var ctx = new MinimalContext(connection, ownsConnection: true);
        connection.Open();

        await ctx.DisposeAsync();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Closed));
        Assert.That(connection.DisposeAsyncCallCount, Is.EqualTo(1));
    }

    [Test]
    public void Dispose_OwnsConnectionButNotOpen_StillDisposesConnection()
    {
        var connection = new TrackingConnection("Data Source=:memory:");
        var ctx = new MinimalContext(connection, ownsConnection: true);

        ctx.Dispose();

        Assert.That(connection.DisposeCallCount, Is.EqualTo(1));
    }

    [Test]
    public void Dispose_DoesNotOwnConnection_DoesNotDisposeConnection()
    {
        var connection = new TrackingConnection("Data Source=:memory:");
        var ctx = new MinimalContext(connection, ownsConnection: false);
        connection.Open();

        ctx.Dispose();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Closed));
        Assert.That(connection.DisposeCallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task DisposeAsync_DoesNotOwnConnection_DoesNotDisposeConnection()
    {
        var connection = new TrackingConnection("Data Source=:memory:");
        var ctx = new MinimalContext(connection, ownsConnection: false);
        connection.Open();

        await ctx.DisposeAsync();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Closed));
        Assert.That(connection.DisposeAsyncCallCount, Is.EqualTo(0));
    }

    [Test]
    public void Dispose_OwnsConnection_DoubleDispose_DoesNotThrow()
    {
        var connection = new TrackingConnection("Data Source=:memory:");
        var ctx = new MinimalContext(connection, ownsConnection: true);
        connection.Open();

        ctx.Dispose();
        Assert.DoesNotThrow(() => ctx.Dispose());
        Assert.That(connection.DisposeCallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task DisposeAsync_OwnsConnection_DoubleDispose_DoesNotThrow()
    {
        var connection = new TrackingConnection("Data Source=:memory:");
        var ctx = new MinimalContext(connection, ownsConnection: true);
        connection.Open();

        await ctx.DisposeAsync();
        Assert.DoesNotThrowAsync(async () => await ctx.DisposeAsync());
        Assert.That(connection.DisposeAsyncCallCount, Is.EqualTo(1));
    }

    /// <summary>
    /// SqliteConnection wrapper that tracks Dispose/DisposeAsync calls.
    /// </summary>
    private sealed class TrackingConnection : SqliteConnection
    {
        public int DisposeCallCount { get; private set; }
        public int DisposeAsyncCallCount { get; private set; }

        public TrackingConnection(string connectionString) : base(connectionString) { }

        protected override void Dispose(bool disposing)
        {
            if (disposing) DisposeCallCount++;
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            DisposeAsyncCallCount++;
            await base.DisposeAsync();
        }
    }
}
