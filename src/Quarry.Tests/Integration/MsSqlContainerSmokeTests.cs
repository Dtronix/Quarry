using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Quarry.Tests.Integration;

/// <summary>
/// Phase 1 smoke test for the SQL Server container helper. Verifies that
/// <see cref="MsSqlTestContainer"/> can boot a real MS SQL 2022 container and
/// accept a basic <c>sa</c> connection. Schema DDL, mapped login, and seed
/// data are deferred to Phase 2 and exercised by later tests.
/// </summary>
[TestFixture]
[Category("SqlServerIntegration")]
public class MsSqlContainerSmokeTests
{
    [Test]
    public async Task SqlServerContainer_BootsAndAcceptsConnection()
    {
        var cs = await MsSqlTestContainer.GetSaConnectionStringAsync();

        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT @@VERSION";
        var version = (string?)await cmd.ExecuteScalarAsync();

        Assert.That(version, Is.Not.Null);
        // 2022-latest reports a "Microsoft SQL Server 2022" banner; we keep the
        // assertion broad so future image-tag bumps that change capitalisation
        // don't trip the smoke test.
        Assert.That(version!, Does.Contain("SQL Server"));
    }
}
