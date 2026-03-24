using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;

/// <summary>
/// Tests for .Prepare() single-terminal collapse.
/// When .Prepare() is followed by a single terminal, the generator should produce
/// identical SQL to a direct terminal chain — zero overhead.
/// </summary>
[TestFixture]
internal class PrepareTests : CrossDialectTestBase
{
    #region Single-Terminal Collapse — Select

    [Test]
    public void Prepare_SingleTerminal_ToDiagnostics_ProducesSameSqlAsDirectChain()
    {
        // Direct chain (no Prepare)
        var directDiag = Lite.Users()
            .Where(u => u.IsActive)
            .Select(u => (u.UserName, u.Email))
            .ToDiagnostics();

        // Prepare + single terminal (should collapse to identical SQL)
        var prepared = Lite.Users()
            .Where(u => u.IsActive)
            .Select(u => (u.UserName, u.Email))
            .Prepare();

        var preparedDiag = prepared.ToDiagnostics();

        Assert.That(preparedDiag.Sql, Is.EqualTo(directDiag.Sql));
    }

    [Test]
    public void Prepare_SingleTerminal_CrossDialect_ProducesCorrectSql()
    {
        var liteDiag = Lite.Users().Select(u => (u.UserName, u.UserId)).Prepare().ToDiagnostics();
        var pgDiag = Pg.Users().Select(u => (u.UserName, u.UserId)).Prepare().ToDiagnostics();
        var myDiag = My.Users().Select(u => (u.UserName, u.UserId)).Prepare().ToDiagnostics();
        var ssDiag = Ss.Users().Select(u => (u.UserName, u.UserId)).Prepare().ToDiagnostics();

        AssertDialects(liteDiag, pgDiag, myDiag, ssDiag,
            sqlite: "SELECT \"UserName\", \"UserId\" FROM \"users\"",
            pg:     "SELECT \"UserName\", \"UserId\" FROM \"users\"",
            mysql:  "SELECT `UserName`, `UserId` FROM `users`",
            ss:     "SELECT [UserName], [UserId] FROM [users]");
    }

    [Test]
    public void Prepare_WithWhere_SingleTerminal_ProducesSameSqlAsDirectChain()
    {
        // Verify that .Prepare() + single terminal produces the same SQL as a direct chain
        var directLite = Lite.Users().Where(u => u.IsActive).Select(u => (u.UserName, u.UserId)).ToDiagnostics();
        var preparedLite = Lite.Users().Where(u => u.IsActive).Select(u => (u.UserName, u.UserId)).Prepare().ToDiagnostics();

        Assert.That(preparedLite.Sql, Is.EqualTo(directLite.Sql));
    }

    [Test]
    public void Prepare_WithLimitOffset_SingleTerminal_ProducesSameSqlAsDirectChain()
    {
        var directDiag = Lite.Users()
            .Select(u => (u.UserId, u.UserName))
            .Limit(10).Offset(5)
            .ToDiagnostics();

        var preparedDiag = Lite.Users()
            .Select(u => (u.UserId, u.UserName))
            .Limit(10).Offset(5)
            .Prepare()
            .ToDiagnostics();

        Assert.That(preparedDiag.Sql, Is.EqualTo(directDiag.Sql));
        Assert.That(preparedDiag.Sql, Does.Contain("LIMIT"));
        Assert.That(preparedDiag.Sql, Does.Contain("OFFSET"));
    }

    [Test]
    public void Prepare_WithLimitOffset_MultiTerminal_ProducesCorrectSql()
    {
        var directDiag = Lite.Users()
            .Select(u => (u.UserId, u.UserName))
            .Limit(10).Offset(5)
            .ToDiagnostics();

        var prepared = Lite.Users()
            .Select(u => (u.UserId, u.UserName))
            .Limit(10).Offset(5)
            .Prepare();

        var preparedDiag = prepared.ToDiagnostics();
        var preparedSql = prepared.ToSql();

        Assert.That(preparedDiag.Sql, Is.EqualTo(directDiag.Sql));
        Assert.That(preparedSql, Is.EqualTo(directDiag.Sql));
    }

    #endregion

    #region Single-Terminal Collapse — Delete

    [Test]
    public void Prepare_Delete_SingleTerminal_ProducesSameSqlAsDirectChain()
    {
        var directDiag = Lite.Users()
            .Delete().Where(u => u.IsActive)
            .ToDiagnostics();

        var preparedDiag = Lite.Users()
            .Delete().Where(u => u.IsActive)
            .Prepare()
            .ToDiagnostics();

        Assert.That(preparedDiag.Sql, Is.EqualTo(directDiag.Sql));
    }

    #endregion

    #region Single-Terminal Collapse — Update

    [Test]
    public void Prepare_Update_SingleTerminal_ProducesSameSqlAsDirectChain()
    {
        var directDiag = Lite.Users()
            .Update().Set(u => u.IsActive = false).Where(u => u.UserId == 1)
            .ToDiagnostics();

        var preparedDiag = Lite.Users()
            .Update().Set(u => u.IsActive = false).Where(u => u.UserId == 1)
            .Prepare()
            .ToDiagnostics();

        Assert.That(preparedDiag.Sql, Is.EqualTo(directDiag.Sql));
    }

    #endregion

    #region Single-Terminal Collapse — Conditional Chain

    [Test]
    public void Prepare_ConditionalWhere_Active_ProducesSameSqlAsDirectChain()
    {
        IQueryBuilder<User> directQuery = Lite.Users().Where(u => true);
        if (true)
        {
            directQuery = directQuery.Where(u => u.IsActive);
        }
        var directDiag = directQuery.ToDiagnostics();

        IQueryBuilder<User> prepQuery = Lite.Users().Where(u => true);
        if (true)
        {
            prepQuery = prepQuery.Where(u => u.IsActive);
        }
        var preparedDiag = prepQuery.Prepare().ToDiagnostics();

        Assert.That(preparedDiag.Sql, Is.EqualTo(directDiag.Sql));
        Assert.That(preparedDiag.Sql, Does.Contain("\"IsActive\""));
    }

    [Test]
    public void Prepare_ConditionalWhere_Inactive_ProducesSameSqlAsDirectChain()
    {
        IQueryBuilder<User> directQuery = Lite.Users().Where(u => true);
        if (false)
        {
            directQuery = directQuery.Where(u => u.IsActive);
        }
        var directDiag = directQuery.ToDiagnostics();

        IQueryBuilder<User> prepQuery = Lite.Users().Where(u => true);
        if (false)
        {
            prepQuery = prepQuery.Where(u => u.IsActive);
        }
        var preparedDiag = prepQuery.Prepare().ToDiagnostics();

        Assert.That(preparedDiag.Sql, Is.EqualTo(directDiag.Sql));
        Assert.That(preparedDiag.Sql, Does.Not.Contain("\"IsActive\""));
    }

    #endregion

    #region Multi-Terminal — Select

    [Test]
    public void Prepare_MultiTerminal_ToDiagnosticsAndToSql_ProduceSameSql()
    {
        var directDiag = Lite.Users()
            .Where(u => u.IsActive)
            .Select(u => (u.UserName, u.Email))
            .ToDiagnostics();

        var prepared = Lite.Users()
            .Where(u => u.IsActive)
            .Select(u => (u.UserName, u.Email))
            .Prepare();

        var preparedDiag = prepared.ToDiagnostics();
        var preparedSql = prepared.ToSql();

        Assert.That(preparedDiag.Sql, Is.EqualTo(directDiag.Sql),
            "Multi-terminal ToDiagnostics should produce same SQL as direct chain");
        Assert.That(preparedSql, Is.EqualTo(directDiag.Sql),
            "Multi-terminal ToSql should produce same SQL as direct chain");
    }

    [Test]
    public void Prepare_MultiTerminal_CrossDialect_ProducesCorrectSql()
    {
        var litePrepared = Lite.Users().Select(u => (u.UserName, u.UserId)).Prepare();
        var liteDiag = litePrepared.ToDiagnostics();
        var liteSql = litePrepared.ToSql();

        Assert.That(liteDiag.Sql, Is.EqualTo("SELECT \"UserName\", \"UserId\" FROM \"users\""));
        Assert.That(liteSql, Is.EqualTo("SELECT \"UserName\", \"UserId\" FROM \"users\""));
    }

    #endregion

    #region Multi-Terminal — Delete

    [Test]
    public void Prepare_Delete_MultiTerminal_ProducesCorrectSql()
    {
        var directDiag = Lite.Users()
            .Delete().Where(u => u.IsActive)
            .ToDiagnostics();

        var prepared = Lite.Users()
            .Delete().Where(u => u.IsActive)
            .Prepare();

        var preparedDiag = prepared.ToDiagnostics();
        var preparedSql = prepared.ToSql();

        Assert.That(preparedDiag.Sql, Is.EqualTo(directDiag.Sql));
        Assert.That(preparedSql, Is.EqualTo(directDiag.Sql));
    }

    #endregion

    #region Single-Terminal Collapse — Join

    [Test]
    public void Prepare_Join_SingleTerminal_ProducesSameSqlAsDirectChain()
    {
        var directDiag = Lite.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total))
            .ToDiagnostics();

        var preparedDiag = Lite.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total))
            .Prepare()
            .ToDiagnostics();

        Assert.That(preparedDiag.Sql, Is.EqualTo(directDiag.Sql));
    }

    #endregion

    #region Multi-Terminal — Join

    [Test]
    public void Prepare_Join_MultiTerminal_ProducesCorrectSql()
    {
        var directDiag = Lite.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total))
            .ToDiagnostics();

        var prepared = Lite.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total))
            .Prepare();

        var preparedDiag = prepared.ToDiagnostics();
        var preparedSql = prepared.ToSql();

        Assert.That(preparedDiag.Sql, Is.EqualTo(directDiag.Sql));
        Assert.That(preparedSql, Is.EqualTo(directDiag.Sql));
    }

    [Test]
    public void Prepare_Join_WithWhere_MultiTerminal_ProducesCorrectSql()
    {
        var directDiag = Lite.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Where((u, o) => u.IsActive)
            .Select((u, o) => (u.UserName, o.Total))
            .ToDiagnostics();

        var prepared = Lite.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Where((u, o) => u.IsActive)
            .Select((u, o) => (u.UserName, o.Total))
            .Prepare();

        var preparedDiag = prepared.ToDiagnostics();
        var preparedSql = prepared.ToSql();

        Assert.That(preparedDiag.Sql, Is.EqualTo(directDiag.Sql));
        Assert.That(preparedSql, Is.EqualTo(directDiag.Sql));
    }

    #endregion

    #region Single-Terminal Collapse — Insert

    [Test]
    public void Prepare_Insert_SingleTerminal_ProducesSameSqlAsDirectChain()
    {
        var entity = new User { UserName = "x", IsActive = true, CreatedAt = default };

        var directDiag = Lite.Users()
            .Insert(entity)
            .ToDiagnostics();

        var preparedDiag = Lite.Users()
            .Insert(entity)
            .Prepare()
            .ToDiagnostics();

        Assert.That(preparedDiag.Sql, Is.EqualTo(directDiag.Sql));
    }

    #endregion

    #region Multi-Terminal — Insert

    [Test]
    public void Prepare_Insert_MultiTerminal_ProducesCorrectSql()
    {
        var entity = new User { UserName = "x", IsActive = true, CreatedAt = default };

        var directDiag = Lite.Users()
            .Insert(entity)
            .ToDiagnostics();

        var prepared = Lite.Users()
            .Insert(entity)
            .Prepare();

        var preparedDiag = prepared.ToDiagnostics();
        var preparedSql = prepared.ToSql();

        Assert.That(preparedDiag.Sql, Is.EqualTo(directDiag.Sql));
        Assert.That(preparedSql, Is.EqualTo(directDiag.Sql));
    }

    #endregion

    #region Batch Insert

    [Test]
    public void Prepare_BatchInsert_SingleTerminal_ProducesSameSqlAsDirectChain()
    {
        var users = new List<User>
        {
            new() { UserName = "x", IsActive = true, CreatedAt = default },
            new() { UserName = "y", IsActive = false, CreatedAt = default }
        };

        var directDiag = Lite.Users()
            .InsertBatch(u => (u.UserName, u.IsActive, u.CreatedAt))
            .Values(users)
            .ToDiagnostics();

        var preparedDiag = Lite.Users()
            .InsertBatch(u => (u.UserName, u.IsActive, u.CreatedAt))
            .Values(users)
            .Prepare()
            .ToDiagnostics();

        Assert.That(preparedDiag.Sql, Is.EqualTo(directDiag.Sql));
    }

    #endregion

    #region Multi-Terminal — Update

    [Test]
    public void Prepare_Update_MultiTerminal_ProducesCorrectSql()
    {
        var directDiag = Lite.Users()
            .Update().Set(u => u.IsActive = false).Where(u => u.UserId == 1)
            .ToDiagnostics();

        var prepared = Lite.Users()
            .Update().Set(u => u.IsActive = false).Where(u => u.UserId == 1)
            .Prepare();

        var preparedDiag = prepared.ToDiagnostics();
        var preparedSql = prepared.ToSql();

        Assert.That(preparedDiag.Sql, Is.EqualTo(directDiag.Sql));
        Assert.That(preparedSql, Is.EqualTo(directDiag.Sql));
    }

    #endregion
}
