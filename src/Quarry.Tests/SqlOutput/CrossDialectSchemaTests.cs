using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


[TestFixture]
internal class CrossDialectSchemaTests
{
    #region Schema-Qualified Table Names

    [Test]
    public async Task Select_SchemaQualified_PostgreSQL()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        using var db = new Quarry.Tests.Samples.SchemaPg.SchemaPgDb(t.MockConnection);
        Assert.That(db.Users().ToDiagnostics().Sql,
            Is.EqualTo("SELECT \"UserId\", \"UserName\", \"Email\", \"IsActive\", \"CreatedAt\", \"LastLogin\" FROM \"public\".\"users\""));
    }

    [Test]
    public async Task Select_SchemaQualified_MySQL()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        using var db = new Quarry.Tests.Samples.SchemaMy.SchemaMyDb(t.MockConnection);
        Assert.That(db.Users().ToDiagnostics().Sql,
            Is.EqualTo("SELECT `UserId`, `UserName`, `Email`, `IsActive`, `CreatedAt`, `LastLogin` FROM `myapp`.`users`"));
    }

    [Test]
    public async Task Select_SchemaQualified_SqlServer()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        using var db = new Quarry.Tests.Samples.SchemaSs.SchemaSsDb(t.MockConnection);
        Assert.That(db.Users().ToDiagnostics().Sql,
            Is.EqualTo("SELECT [UserId], [UserName], [Email], [IsActive], [CreatedAt], [LastLogin] FROM [dbo].[users]"));
    }

    #endregion

    #region MapTo Custom Column Name

    [Test]
    public async Task Select_MapToColumn_CreditLimit()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt= Lite.Accounts().Select(a => (a.AccountId, a.CreditLimit)).Prepare();
        var pg = Pg.Accounts().Select(a => (a.AccountId, a.CreditLimit)).Prepare();
        var my = My.Accounts().Select(a => (a.AccountId, a.CreditLimit)).Prepare();
        var ss = Ss.Accounts().Select(a => (a.AccountId, a.CreditLimit)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"AccountId\", \"credit_limit\" FROM \"accounts\"",
            pg:     "SELECT \"AccountId\", \"credit_limit\" FROM \"accounts\"",
            mysql:  "SELECT `AccountId`, `credit_limit` FROM `accounts`",
            ss:     "SELECT [AccountId], [credit_limit] FROM [accounts]");
    }

    #endregion

    #region Single-Column Select

    [Test]
    public async Task Select_SingleColumn()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt= Lite.Users().Select(u => u.UserName).Prepare();
        var pg = Pg.Users().Select(u => u.UserName).Prepare();
        var my = My.Users().Select(u => u.UserName).Prepare();
        var ss = Ss.Users().Select(u => u.UserName).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserName\" FROM \"users\"",
            pg:     "SELECT \"UserName\" FROM \"users\"",
            mysql:  "SELECT `UserName` FROM `users`",
            ss:     "SELECT [UserName] FROM [users]");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo("Alice"));

        var pgResults = await pg.ExecuteFetchAllAsync().SortedByAsync(s => s);
        Assert.That(pgResults, Has.Count.EqualTo(3));
        Assert.That(pgResults[0], Is.EqualTo("Alice"));

        var myResults = await my.ExecuteFetchAllAsync().SortedByAsync(s => s);
        Assert.That(myResults, Has.Count.EqualTo(3));
        Assert.That(myResults[0], Is.EqualTo("Alice"));

        var ssResults = await ss.ExecuteFetchAllAsync().SortedByAsync(s => s);
        Assert.That(ssResults, Has.Count.EqualTo(3));
        Assert.That(ssResults[0], Is.EqualTo("Alice"));
    }

    #endregion

    #region Ref<> FK in WHERE

    [Test]
    public async Task Where_RefId_ForeignKey()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt= Lite.Orders().Where(o => o.UserId.Id == 5).Prepare();
        var pg = Pg.Orders().Where(o => o.UserId.Id == 5).Prepare();
        var my = My.Orders().Where(o => o.UserId.Id == 5).Prepare();
        var ss = Ss.Orders().Where(o => o.UserId.Id == 5).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\" WHERE \"UserId\" = 5",
            pg:     "SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\" WHERE \"UserId\" = 5",
            mysql:  "SELECT `OrderId`, `UserId`, `Total`, `Status`, `Priority`, `OrderDate`, `Notes` FROM `orders` WHERE `UserId` = 5",
            ss:     "SELECT [OrderId], [UserId], [Total], [Status], [Priority], [OrderDate], [Notes] FROM [orders] WHERE [UserId] = 5");
    }

    #endregion

    #region Computed Column Exclusion in INSERT

    [Test]
    public async Task Insert_ComputedColumnExcluded()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // DiscountedPrice is Computed() — should be excluded even if set
        Assert.That(
            Lite.Products().Insert(new Product { ProductName = "x", Price = 10m, DiscountedPrice = 5m }).ToDiagnostics().Sql,
            Is.EqualTo("INSERT INTO \"products\" (\"ProductName\", \"Price\") VALUES (@p0, @p1) RETURNING \"ProductId\""));
    }

    #endregion

    #region ClientGenerated (GUID Key) -- No RETURNING

    [Test]
    public async Task Insert_ClientGeneratedGuid_NoReturning()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // WidgetId is ClientGenerated() GUID -- no RETURNING/OUTPUT clause
        Assert.That(
            Lite.Widgets().Insert(new Widget { WidgetId = Guid.Empty, WidgetName = "x" }).ToDiagnostics().Sql,
            Is.EqualTo("INSERT INTO \"widgets\" (\"WidgetId\", \"WidgetName\") VALUES (@p0, @p1)"));
    }

    #endregion

    #region Delete All

    [Test]
    public async Task Delete_All_NoWhereClause()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt= Lite.Users().Delete().All().Prepare();
        var pg = Pg.Users().Delete().All().Prepare();
        var my = My.Users().Delete().All().Prepare();
        var ss = Ss.Users().Delete().All().Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "DELETE FROM \"users\"",
            pg:     "DELETE FROM \"users\"",
            mysql:  "DELETE FROM `users`",
            ss:     "DELETE FROM [users]");

        var affected = await lt.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(3)); // All 3 seeded users

        var pgAffected = await pg.ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(3));

        var myAffected = await my.ExecuteNonQueryAsync();
        Assert.That(myAffected, Is.EqualTo(3));

        var ssAffected = await ss.ExecuteNonQueryAsync();
        Assert.That(ssAffected, Is.EqualTo(3));
    }

    #endregion
}
