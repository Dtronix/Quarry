using Quarry.Tests.Samples;

namespace Quarry.Tests.SqlOutput;

/// <summary>
/// End-to-end SQL output tests using the real compiled TestDbContext (SQLite dialect).
/// These verify the full generator pipeline: schema → context → interceptor → exact SQL.
/// </summary>
[TestFixture]
public class EndToEndSqlTests
{
    private MockDbConnection _connection = null!;
    private TestDbContext _db = null!;

    [SetUp]
    public void Setup()
    {
        _connection = new MockDbConnection();
        _db = new TestDbContext(_connection);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    #region Select Projections

    [Test]
    public void Select_Tuple_TwoColumns()
    {
        var sql = _db.Users().Select(u => (u.UserId, u.UserName)).ToDiagnostics().Sql;
        Assert.That(sql, Is.EqualTo("SELECT \"UserId\", \"UserName\" FROM \"users\""));
    }

    [Test]
    public void Select_Tuple_ThreeColumns()
    {
        var sql = _db.Users().Select(u => (u.UserId, u.UserName, u.IsActive)).ToDiagnostics().Sql;
        Assert.That(sql, Is.EqualTo("SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\""));
    }

    [Test]
    public void Select_Dto_UserSummary()
    {
        var sql = _db.Users().Select(u => new UserSummaryDto
        {
            UserId = u.UserId,
            UserName = u.UserName,
            IsActive = u.IsActive
        }).ToDiagnostics().Sql;
        Assert.That(sql, Is.EqualTo("SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\""));
    }

    [Test]
    public void Select_Dto_UserWithEmail()
    {
        var sql = _db.Users().Select(u => new UserWithEmailDto
        {
            UserId = u.UserId,
            UserName = u.UserName,
            Email = u.Email
        }).ToDiagnostics().Sql;
        Assert.That(sql, Is.EqualTo("SELECT \"UserId\", \"UserName\", \"Email\" FROM \"users\""));
    }

    [Test]
    public void Select_Dto_OrderSummary()
    {
        var sql = _db.Orders().Select(o => new OrderSummaryDto
        {
            OrderId = o.OrderId,
            Total = o.Total,
            Status = o.Status
        }).ToDiagnostics().Sql;
        Assert.That(sql, Is.EqualTo("SELECT \"OrderId\", \"Total\", \"Status\" FROM \"orders\""));
    }

    #endregion

    #region Where Clause

    [Test]
    public void Where_BooleanProperty()
    {
        var sql = _db.Users().Where(u => u.IsActive).ToDiagnostics().Sql;
        Assert.That(sql, Is.EqualTo("SELECT * FROM \"users\" WHERE \"IsActive\" = 1"));
    }

    [Test]
    public void Where_NegatedBooleanProperty()
    {
        var sql = _db.Users().Where(u => !u.IsActive).ToDiagnostics().Sql;
        Assert.That(sql, Is.EqualTo("SELECT * FROM \"users\" WHERE NOT (\"IsActive\")"));
    }

    [Test]
    public void Where_Comparison_GreaterThan()
    {
        var sql = _db.Users().Where(u => u.UserId > 0).ToDiagnostics().Sql;
        Assert.That(sql, Is.EqualTo("SELECT * FROM \"users\" WHERE (\"UserId\" > 0)"));
    }

    [Test]
    public void Where_MultipleChained()
    {
        var sql = _db.Users()
            .Where(u => u.IsActive)
            .Where(u => u.UserId > 0)
            .ToDiagnostics().Sql;
        Assert.That(sql, Is.EqualTo("SELECT * FROM \"users\" WHERE (\"IsActive\" = 1) AND ((\"UserId\" > 0))"));
    }

    #endregion

    #region Where + Select Combined

    [Test]
    public void Where_ThenSelect_Tuple()
    {
        var sql = _db.Users()
            .Where(u => u.IsActive)
            .Select(u => (u.UserId, u.UserName))
            .ToDiagnostics().Sql;
        Assert.That(sql, Is.EqualTo("SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = 1"));
    }

    [Test]
    public void Where_ThenSelect_Dto()
    {
        var sql = _db.Users()
            .Where(u => u.IsActive)
            .Select(u => new UserSummaryDto
            {
                UserId = u.UserId,
                UserName = u.UserName,
                IsActive = u.IsActive
            })
            .ToDiagnostics().Sql;
        Assert.That(sql, Is.EqualTo("SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"IsActive\" = 1"));
    }

    #endregion

    #region Pagination

    [Test]
    public void Limit()
    {
        var sql = _db.Users().Where(u => true).Limit(10).ToDiagnostics().Sql;
        Assert.That(sql, Is.EqualTo("SELECT * FROM \"users\" LIMIT 10"));
    }

    [Test]
    public void LimitAndOffset()
    {
        var sql = _db.Users().Where(u => true).Limit(10).Offset(20).ToDiagnostics().Sql;
        Assert.That(sql, Is.EqualTo("SELECT * FROM \"users\" LIMIT 10 OFFSET 20"));
    }

    [Test]
    public void Distinct()
    {
        var sql = _db.Users().Distinct().ToDiagnostics().Sql;
        Assert.That(sql, Is.EqualTo("SELECT DISTINCT * FROM \"users\""));
    }

    #endregion

    #region Insert

    [Test]
    public void Insert_User_ToSql()
    {
        var user = new User
        {
            UserName = "test",
            IsActive = true,
            CreatedAt = new DateTime(2024, 1, 1)
        };
        var sql = _db.Users().Insert(user).ToDiagnostics().Sql;
        // Insert ToSql before execution shows column list (values added during execution)
        Assert.That(sql, Does.StartWith("INSERT INTO \"users\""));
    }

    #endregion

    #region Orders Table

    [Test]
    public void Orders_Select_Tuple()
    {
        var sql = _db.Orders().Select(o => (o.OrderId, o.Total)).ToDiagnostics().Sql;
        Assert.That(sql, Is.EqualTo("SELECT \"OrderId\", \"Total\" FROM \"orders\""));
    }

    [Test]
    public void Orders_Where()
    {
        var sql = _db.Orders().Where(o => o.OrderId > 0).ToDiagnostics().Sql;
        Assert.That(sql, Is.EqualTo("SELECT * FROM \"orders\" WHERE (\"OrderId\" > 0)"));
    }

    #endregion

    #region Join

    [Test]
    public void Join_TwoTables_Select_Tuple()
    {
        var sql = _db.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total))
            .ToDiagnostics().Sql;
        Assert.That(sql, Does.Contain("FROM \"users\" AS \"t0\""));
        Assert.That(sql, Does.Contain("INNER JOIN"));
        Assert.That(sql, Does.Contain("\"UserName\""));
        Assert.That(sql, Does.Contain("\"Total\""));
    }

    [Test]
    public void Join_TwoTables_Select_Dto()
    {
        var sql = _db.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => new UserOrderDto
            {
                UserName = u.UserName,
                Total = o.Total
            })
            .ToDiagnostics().Sql;
        Assert.That(sql, Does.Contain("FROM \"users\" AS \"t0\""));
        Assert.That(sql, Does.Contain("INNER JOIN"));
        Assert.That(sql, Does.Contain("\"UserName\""));
        Assert.That(sql, Does.Contain("\"Total\""));
    }

    #endregion

    #region Update POCO Set

    [Test]
    public void UpdateSetPoco_SingleColumn()
    {
        var sql = _db.Users().Update()
            .Set(new User { UserName = "NewName" })
            .Where(u => u.UserId == 1)
            .ToDiagnostics().Sql;
        Assert.That(sql, Does.Contain("SET \"UserName\" = @p0"));
        Assert.That(sql, Does.Contain("WHERE"));
    }

    [Test]
    public void UpdateSetPoco_MultipleColumns()
    {
        var sql = _db.Users().Update()
            .Set(new User { UserName = "NewName", IsActive = false })
            .Where(u => u.UserId == 1)
            .ToDiagnostics().Sql;
        Assert.That(sql, Does.Contain("SET"));
        Assert.That(sql, Does.Contain("\"UserName\""));
        Assert.That(sql, Does.Contain("\"IsActive\""));
    }

    [Test]
    public void UpdateSetPoco_WithAll()
    {
        var sql = _db.Users().Update()
            .Set(new User { IsActive = false })
            .All()
            .ToDiagnostics().Sql;
        Assert.That(sql, Does.Contain("SET \"IsActive\" = @p0"));
        Assert.That(sql, Does.Not.Contain("WHERE"));
    }

    [Test]
    public void UpdateSetPoco_MixedWithPropertySet()
    {
        var sql = _db.Users().Update()
            .Set(new User { UserName = "NewName" })
            .Set(u => u.IsActive, true)
            .Where(u => u.UserId == 1)
            .ToDiagnostics().Sql;
        Assert.That(sql, Does.Contain("\"UserName\""));
        Assert.That(sql, Does.Contain("\"IsActive\""));
    }

    #endregion
}
