using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;

#pragma warning disable QRY001

[TestFixture]
internal class CrossDialectSchemaTests : CrossDialectTestBase
{
    #region Schema-Qualified Table Names

    [Test]
    public void Select_SchemaQualified_PostgreSQL()
    {
        using var db = new Quarry.Tests.Samples.SchemaPg.SchemaPgDb(Connection);
        Assert.That(db.Users().ToSql(),
            Is.EqualTo("SELECT * FROM \"public\".\"users\""));
    }

    [Test]
    public void Select_SchemaQualified_MySQL()
    {
        using var db = new Quarry.Tests.Samples.SchemaMy.SchemaMyDb(Connection);
        Assert.That(db.Users().ToSql(),
            Is.EqualTo("SELECT * FROM `myapp`.`users`"));
    }

    [Test]
    public void Select_SchemaQualified_SqlServer()
    {
        using var db = new Quarry.Tests.Samples.SchemaSs.SchemaSsDb(Connection);
        Assert.That(db.Users().ToSql(),
            Is.EqualTo("SELECT * FROM [dbo].[users]"));
    }

    #endregion

    #region MapTo Custom Column Name

    [Test]
    public void Select_MapToColumn_CreditLimit()
    {
        AssertDialects(
            Lite.Accounts().Select(a => (a.AccountId, a.CreditLimit)).ToTestCase(),
            Pg.Accounts().Select(a => (a.AccountId, a.CreditLimit)).ToTestCase(),
            My.Accounts().Select(a => (a.AccountId, a.CreditLimit)).ToTestCase(),
            Ss.Accounts().Select(a => (a.AccountId, a.CreditLimit)).ToTestCase(),
            sqlite: "SELECT \"AccountId\", \"credit_limit\" FROM \"accounts\"",
            pg:     "SELECT \"AccountId\", \"credit_limit\" FROM \"accounts\"",
            mysql:  "SELECT `AccountId`, `credit_limit` FROM `accounts`",
            ss:     "SELECT [AccountId], [credit_limit] FROM [accounts]");
    }

    #endregion

    #region Single-Column Select

    [Test]
    public void Select_SingleColumn()
    {
        AssertDialects(
            Lite.Users().Select(u => u.UserName).ToTestCase(),
            Pg.Users().Select(u => u.UserName).ToTestCase(),
            My.Users().Select(u => u.UserName).ToTestCase(),
            Ss.Users().Select(u => u.UserName).ToTestCase(),
            sqlite: "SELECT \"UserName\" FROM \"users\"",
            pg:     "SELECT \"UserName\" FROM \"users\"",
            mysql:  "SELECT `UserName` FROM `users`",
            ss:     "SELECT [UserName] FROM [users]");
    }

    #endregion

    #region Ref<> FK in WHERE

    [Test]
    public void Where_RefId_ForeignKey()
    {
        AssertDialects(
            Lite.Orders().Where(o => o.UserId.Id == 5).ToTestCase(),
            Pg.Orders().Where(o => o.UserId.Id == 5).ToTestCase(),
            My.Orders().Where(o => o.UserId.Id == 5).ToTestCase(),
            Ss.Orders().Where(o => o.UserId.Id == 5).ToTestCase(),
            sqlite: "SELECT * FROM \"orders\" WHERE (\"UserId\" = 5)",
            pg:     "SELECT * FROM \"orders\" WHERE (\"UserId\" = 5)",
            mysql:  "SELECT * FROM `orders` WHERE (`UserId` = 5)",
            ss:     "SELECT * FROM [orders] WHERE ([UserId] = 5)");
    }

    #endregion

    #region Computed Column Exclusion in INSERT

    [Test]
    public void Insert_ComputedColumnExcluded()
    {
        // DiscountedPrice is Computed() — should be excluded even if set
        Assert.That(
            Lite.Products().Insert(new Product { ProductName = "x", Price = 10m, DiscountedPrice = 5m }).ToSql(),
            Is.EqualTo("INSERT INTO \"products\" (\"ProductName\", \"Price\") VALUES (@p0, @p1) RETURNING \"ProductId\""));
    }

    #endregion

    #region ClientGenerated (GUID Key) -- No RETURNING

    [Test]
    public void Insert_ClientGeneratedGuid_NoReturning()
    {
        // WidgetId is ClientGenerated() GUID -- no RETURNING/OUTPUT clause
        Assert.That(
            Lite.Widgets().Insert(new Widget { WidgetId = Guid.Empty, WidgetName = "x" }).ToSql(),
            Is.EqualTo("INSERT INTO \"widgets\" (\"WidgetId\", \"WidgetName\") VALUES (@p0, @p1)"));
    }

    #endregion

    #region Delete All

    [Test]
    public void Delete_All_NoWhereClause()
    {
        AssertDialects(
            Lite.Users().Delete().All().ToTestCase(),
            Pg.Users().Delete().All().ToTestCase(),
            My.Users().Delete().All().ToTestCase(),
            Ss.Users().Delete().All().ToTestCase(),
            sqlite: "DELETE FROM \"users\"",
            pg:     "DELETE FROM \"users\"",
            mysql:  "DELETE FROM `users`",
            ss:     "DELETE FROM [users]");
    }

    #endregion
}
