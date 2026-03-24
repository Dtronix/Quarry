using Quarry.Tests.Samples;

namespace Quarry.Tests.Integration;

/// <summary>
/// Integration tests for .Prepare() — verifies that single-terminal collapse
/// and multi-terminal paths produce correct results against a real SQLite database.
/// </summary>
[TestFixture]
internal class PrepareIntegrationTests : SqliteIntegrationTestBase
{
    #region Single-Terminal Collapse — Select Execution

    [Test]
    public async Task Prepare_SingleTerminal_ExecuteFetchAll_ReturnsCorrectData()
    {
        var prepared = Db.Users()
            .Where(u => u.IsActive)
            .Select(u => (u.UserId, u.UserName))
            .Prepare();

        var results = await prepared.ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));
    }

    [Test]
    public async Task Prepare_SingleTerminal_ExecuteFetchFirst_ReturnsCorrectData()
    {
        var prepared = Db.Users()
            .Select(u => (u.UserId, u.UserName))
            .Prepare();

        var result = await prepared.ExecuteFetchFirstAsync();

        Assert.That(result, Is.EqualTo((1, "Alice")));
    }

    [Test]
    public void Prepare_SingleTerminal_ToDiagnostics_ProducesValidSql()
    {
        var prepared = Db.Users()
            .Where(u => u.IsActive)
            .Select(u => (u.UserId, u.UserName))
            .Prepare();

        var diag = prepared.ToDiagnostics();

        Assert.That(diag.Sql, Is.EqualTo("SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = 1"));
    }

    #endregion

    #region Multi-Terminal — Select

    [Test]
    public async Task Prepare_MultiTerminal_DiagnosticsAndFetchAll()
    {
        var prepared = Db.Users()
            .Where(u => u.IsActive)
            .Select(u => (u.UserId, u.UserName))
            .Prepare();

        var diag = prepared.ToDiagnostics();
        var results = await prepared.ExecuteFetchAllAsync();

        Assert.That(diag.Sql, Is.EqualTo("SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = 1"));
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
    }

    [Test]
    public async Task Prepare_MultiTerminal_ToSqlAndFetchAll()
    {
        var prepared = Db.Users()
            .Select(u => (u.UserId, u.UserName))
            .Prepare();

        var sql = prepared.ToDiagnostics().Sql;
        var results = await prepared.ExecuteFetchAllAsync();

        Assert.That(sql, Is.EqualTo("SELECT \"UserId\", \"UserName\" FROM \"users\""));
        Assert.That(results, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task Prepare_MultiTerminal_DiagnosticsAndToSql_SameSql()
    {
        var prepared = Db.Users()
            .Where(u => u.IsActive)
            .Select(u => (u.UserId, u.UserName))
            .Prepare();

        var diag = prepared.ToDiagnostics();
        var sql = prepared.ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo(diag.Sql));
    }

    #endregion

    #region Multi-Terminal — Delete

    [Test]
    public async Task Prepare_Delete_MultiTerminal_DiagnosticsAndExecute()
    {
        // Use a condition that matches no rows to avoid modifying test data
        var prepared = Db.Users()
            .Delete().Where(u => u.UserId == 999)
            .Prepare();

        var diag = prepared.ToDiagnostics();
        var affected = await prepared.ExecuteNonQueryAsync();

        Assert.That(diag.Sql, Is.EqualTo("DELETE FROM \"users\" WHERE \"UserId\" = 999"));
        Assert.That(affected, Is.EqualTo(0));
    }

    #endregion

    #region Multi-Terminal — Update

    [Test]
    public async Task Prepare_Update_MultiTerminal_DiagnosticsAndExecute()
    {
        // Use a condition that matches no rows to avoid modifying test data
        var prepared = Db.Users()
            .Update().Set(u => u.UserName = "Updated").Where(u => u.UserId == 999)
            .Prepare();

        var diag = prepared.ToDiagnostics();
        var affected = await prepared.ExecuteNonQueryAsync();

        Assert.That(diag.Sql, Is.EqualTo("UPDATE \"users\" SET \"UserName\" = 'Updated' WHERE \"UserId\" = 999"));
        Assert.That(affected, Is.EqualTo(0));
    }

    #endregion

    #region Multi-Terminal — Batch Insert

    [Test]
    public void Prepare_BatchInsert_MultiTerminal_DiagnosticsAndToSql()
    {
        var users = new List<User>
        {
            new() { UserName = "PrepBatch1", IsActive = true, CreatedAt = DateTime.UtcNow },
            new() { UserName = "PrepBatch2", IsActive = false, CreatedAt = DateTime.UtcNow }
        };

        var prepared = Db.Users()
            .InsertBatch(u => (u.UserName, u.IsActive, u.CreatedAt))
            .Values(users)
            .Prepare();

        var diag = prepared.ToDiagnostics();
        var sql = prepared.ToDiagnostics().Sql;

        Assert.That(diag.Sql, Is.EqualTo(
            "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES (@p0, @p1, @p2) RETURNING \"UserId\""));
        // Both calls return the same full SQL via ToDiagnostics()
        Assert.That(sql, Is.EqualTo(diag.Sql));
    }

    #endregion
}
