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
}
