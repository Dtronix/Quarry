using System.Threading.Tasks;
using MySqlConnector;

namespace Quarry.Tests.Integration;

/// <summary>
/// End-to-end execution tests on a real MySqlConnector + MySQL 8.4 container
/// covering the same generator + runtime code paths PR #266 verified for
/// PostgreSQL. The MySQL parallel of <see cref="PostgresIntegrationTests"/>.
/// </summary>
/// <remarks>
/// Phase 1 (this commit) is the container-bootstrap regression probe — if
/// Docker is up and the image pulls, this passes. Phase 3 will extend the
/// fixture with the four focused execution tests (entity insert, batch
/// insert, where-in-collection, plus the migration runner, which lives in
/// its own fixture).
/// </remarks>
[TestFixture]
[Category("MySqlIntegration")]
public class MySqlIntegrationTests
{
    [Test]
    public async Task ContainerBootstraps_OnMySQL()
    {
        // Bootstrap probe: prove the Testcontainers.MySql + MySqlConnector
        // wiring is reachable in this test process and on CI. If Docker is
        // unavailable the harness routes to Assert.Ignore with a clear
        // message; otherwise we expect a "8.4.x" version string.
        var cs = await MySqlTestContainer.GetConnectionStringAsync();
        await using var conn = new MySqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT VERSION()";
        var v = (string?)await cmd.ExecuteScalarAsync();
        Assert.That(v, Does.StartWith("8.4"));
    }
}
