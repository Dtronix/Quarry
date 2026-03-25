using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


[TestFixture]
internal class CrossDialectInsertTests
{
    #region User Inserts

    [Test]
    public async Task Insert_SingleUser()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        QueryTestHarness.AssertDialects(
            Lite.Users().Insert(new User { UserName = "x", IsActive = true, CreatedAt = default }).ToDiagnostics().Sql,
            Pg.Users().Insert(new Pg.User { UserName = "x", IsActive = true, CreatedAt = default }).ToDiagnostics().Sql,
            My.Users().Insert(new My.User { UserName = "x", IsActive = true, CreatedAt = default }).ToDiagnostics().Sql,
            Ss.Users().Insert(new Ss.User { UserName = "x", IsActive = true, CreatedAt = default }).ToDiagnostics().Sql,
            sqlite: "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES (@p0, @p1, @p2) RETURNING \"UserId\"",
            pg:     "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES ($1, $2, $3) RETURNING \"UserId\"",
            mysql:  "INSERT INTO `users` (`UserName`, `IsActive`, `CreatedAt`) VALUES (?, ?, ?); SELECT LAST_INSERT_ID()",
            ss:     "INSERT INTO [users] ([UserName], [IsActive], [CreatedAt]) VALUES (@p0, @p1, @p2) OUTPUT INSERTED.[UserId]");
    }

    #endregion

    #region Order Inserts

    [Test]
    public async Task Insert_SingleOrder()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        QueryTestHarness.AssertDialects(
            Lite.Orders().Insert(new Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ToDiagnostics().Sql,
            Pg.Orders().Insert(new Pg.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ToDiagnostics().Sql,
            My.Orders().Insert(new My.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ToDiagnostics().Sql,
            Ss.Orders().Insert(new Ss.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ToDiagnostics().Sql,
            sqlite: "INSERT INTO \"orders\" (\"UserId\", \"Total\", \"Status\", \"OrderDate\") VALUES (@p0, @p1, @p2, @p3) RETURNING \"OrderId\"",
            pg:     "INSERT INTO \"orders\" (\"UserId\", \"Total\", \"Status\", \"OrderDate\") VALUES ($1, $2, $3, $4) RETURNING \"OrderId\"",
            mysql:  "INSERT INTO `orders` (`UserId`, `Total`, `Status`, `OrderDate`) VALUES (?, ?, ?, ?); SELECT LAST_INSERT_ID()",
            ss:     "INSERT INTO [orders] ([UserId], [Total], [Status], [OrderDate]) VALUES (@p0, @p1, @p2, @p3) OUTPUT INSERTED.[OrderId]");
    }

    #endregion

    #region OrderItem Inserts

    [Test]
    public async Task Insert_SingleOrderItem()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        QueryTestHarness.AssertDialects(
            Lite.OrderItems().Insert(new OrderItem { OrderId = 1, ProductName = "x", Quantity = 0, UnitPrice = 0m, LineTotal = 0m }).ToDiagnostics().Sql,
            Pg.OrderItems().Insert(new Pg.OrderItem { OrderId = 1, ProductName = "x", Quantity = 0, UnitPrice = 0m, LineTotal = 0m }).ToDiagnostics().Sql,
            My.OrderItems().Insert(new My.OrderItem { OrderId = 1, ProductName = "x", Quantity = 0, UnitPrice = 0m, LineTotal = 0m }).ToDiagnostics().Sql,
            Ss.OrderItems().Insert(new Ss.OrderItem { OrderId = 1, ProductName = "x", Quantity = 0, UnitPrice = 0m, LineTotal = 0m }).ToDiagnostics().Sql,
            sqlite: "INSERT INTO \"order_items\" (\"OrderId\", \"ProductName\", \"Quantity\", \"UnitPrice\", \"LineTotal\") VALUES (@p0, @p1, @p2, @p3, @p4) RETURNING \"OrderItemId\"",
            pg:     "INSERT INTO \"order_items\" (\"OrderId\", \"ProductName\", \"Quantity\", \"UnitPrice\", \"LineTotal\") VALUES ($1, $2, $3, $4, $5) RETURNING \"OrderItemId\"",
            mysql:  "INSERT INTO `order_items` (`OrderId`, `ProductName`, `Quantity`, `UnitPrice`, `LineTotal`) VALUES (?, ?, ?, ?, ?); SELECT LAST_INSERT_ID()",
            ss:     "INSERT INTO [order_items] ([OrderId], [ProductName], [Quantity], [UnitPrice], [LineTotal]) VALUES (@p0, @p1, @p2, @p3, @p4) OUTPUT INSERTED.[OrderItemId]");
    }

    #endregion

    #region ExecuteNonQueryAsync Cross-Dialect

    [Test]
    public async Task ExecuteNonQueryAsync_SingleUser()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;
        var conn = t.MockConnection;

        await Pg.Users().Insert(new Pg.User { UserName = "x", IsActive = true, CreatedAt = default }).ExecuteNonQueryAsync();
        var pgSql = conn.LastCommand!.CommandText;

        await My.Users().Insert(new My.User { UserName = "x", IsActive = true, CreatedAt = default }).ExecuteNonQueryAsync();
        var mySql = conn.LastCommand!.CommandText;

        await Ss.Users().Insert(new Ss.User { UserName = "x", IsActive = true, CreatedAt = default }).ExecuteNonQueryAsync();
        var ssSql = conn.LastCommand!.CommandText;

        // ExecuteNonQueryAsync does not set identity column, so no RETURNING/OUTPUT clause
        Assert.Multiple(() =>
        {
            Assert.That(pgSql, Is.EqualTo(
                "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES ($1, $2, $3)"), "PostgreSQL");
            Assert.That(mySql, Is.EqualTo(
                "INSERT INTO `users` (`UserName`, `IsActive`, `CreatedAt`) VALUES (?, ?, ?)"), "MySQL");
            Assert.That(ssSql, Is.EqualTo(
                "INSERT INTO [users] ([UserName], [IsActive], [CreatedAt]) VALUES (@p0, @p1, @p2)"), "SqlServer");
        });

        // Verify real execution against SQLite
        var affected = await Lite.Users().Insert(new User { UserName = "x", IsActive = true, CreatedAt = default }).ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));
    }

    [Test]
    public async Task ExecuteNonQueryAsync_SingleOrder()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;
        var conn = t.MockConnection;

        await Pg.Orders().Insert(new Pg.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ExecuteNonQueryAsync();
        var pgSql = conn.LastCommand!.CommandText;

        await My.Orders().Insert(new My.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ExecuteNonQueryAsync();
        var mySql = conn.LastCommand!.CommandText;

        await Ss.Orders().Insert(new Ss.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ExecuteNonQueryAsync();
        var ssSql = conn.LastCommand!.CommandText;

        Assert.Multiple(() =>
        {
            Assert.That(pgSql, Is.EqualTo(
                "INSERT INTO \"orders\" (\"UserId\", \"Total\", \"Status\", \"OrderDate\") VALUES ($1, $2, $3, $4)"), "PostgreSQL");
            Assert.That(mySql, Is.EqualTo(
                "INSERT INTO `orders` (`UserId`, `Total`, `Status`, `OrderDate`) VALUES (?, ?, ?, ?)"), "MySQL");
            Assert.That(ssSql, Is.EqualTo(
                "INSERT INTO [orders] ([UserId], [Total], [Status], [OrderDate]) VALUES (@p0, @p1, @p2, @p3)"), "SqlServer");
        });

        // Verify real execution against SQLite
        var affected = await Lite.Orders().Insert(new Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));
    }

    #endregion

    #region ExecuteScalarAsync Cross-Dialect

    [Test]
    public async Task ExecuteScalarAsync_SingleUser_ReturnsIdentity()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;
        var conn = t.MockConnection;
        conn.ScalarResult = 42;

        await Pg.Users().Insert(new Pg.User { UserName = "x", IsActive = true, CreatedAt = default }).ExecuteScalarAsync<int>();
        var pgSql = conn.LastCommand!.CommandText;

        await My.Users().Insert(new My.User { UserName = "x", IsActive = true, CreatedAt = default }).ExecuteScalarAsync<int>();
        var mySql = conn.LastCommand!.CommandText;

        await Ss.Users().Insert(new Ss.User { UserName = "x", IsActive = true, CreatedAt = default }).ExecuteScalarAsync<int>();
        var ssSql = conn.LastCommand!.CommandText;

        Assert.Multiple(() =>
        {
            Assert.That(pgSql, Is.EqualTo(
                "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES ($1, $2, $3) RETURNING \"UserId\""), "PostgreSQL");
            Assert.That(mySql, Is.EqualTo(
                "INSERT INTO `users` (`UserName`, `IsActive`, `CreatedAt`) VALUES (?, ?, ?); SELECT LAST_INSERT_ID()"), "MySQL");
            Assert.That(ssSql, Is.EqualTo(
                "INSERT INTO [users] ([UserName], [IsActive], [CreatedAt]) VALUES (@p0, @p1, @p2) OUTPUT INSERTED.[UserId]"), "SqlServer");
        });

        // Verify real execution against SQLite — returns auto-generated UserId
        var newId = await Lite.Users().Insert(new User { UserName = "x", IsActive = true, CreatedAt = default }).ExecuteScalarAsync<int>();
        Assert.That(newId, Is.GreaterThan(0));
    }

    [Test]
    public async Task ExecuteScalarAsync_SingleOrder_ReturnsIdentity()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;
        var conn = t.MockConnection;
        conn.ScalarResult = 99;

        await Pg.Orders().Insert(new Pg.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ExecuteScalarAsync<int>();
        var pgSql = conn.LastCommand!.CommandText;

        await My.Orders().Insert(new My.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ExecuteScalarAsync<int>();
        var mySql = conn.LastCommand!.CommandText;

        await Ss.Orders().Insert(new Ss.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ExecuteScalarAsync<int>();
        var ssSql = conn.LastCommand!.CommandText;

        Assert.Multiple(() =>
        {
            Assert.That(pgSql, Is.EqualTo(
                "INSERT INTO \"orders\" (\"UserId\", \"Total\", \"Status\", \"OrderDate\") VALUES ($1, $2, $3, $4) RETURNING \"OrderId\""), "PostgreSQL");
            Assert.That(mySql, Is.EqualTo(
                "INSERT INTO `orders` (`UserId`, `Total`, `Status`, `OrderDate`) VALUES (?, ?, ?, ?); SELECT LAST_INSERT_ID()"), "MySQL");
            Assert.That(ssSql, Is.EqualTo(
                "INSERT INTO [orders] ([UserId], [Total], [Status], [OrderDate]) VALUES (@p0, @p1, @p2, @p3) OUTPUT INSERTED.[OrderId]"), "SqlServer");
        });

        var newId = await Lite.Orders().Insert(new Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ExecuteScalarAsync<int>();
        Assert.That(newId, Is.GreaterThan(0));
    }

    #endregion
}
