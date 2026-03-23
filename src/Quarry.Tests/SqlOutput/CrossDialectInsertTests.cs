using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


[TestFixture]
internal class CrossDialectInsertTests : CrossDialectTestBase
{
    #region User Inserts

    [Test]
    public void Insert_SingleUser()
    {
        AssertDialects(
            Lite.Users().Insert(new User { UserName = "x", IsActive = true, CreatedAt = default }).ToDiagnostics().Sql,
            Pg.Users().Insert(new Pg.User { UserName = "x", IsActive = true, CreatedAt = default }).ToDiagnostics().Sql,
            My.Users().Insert(new My.User { UserName = "x", IsActive = true, CreatedAt = default }).ToDiagnostics().Sql,
            Ss.Users().Insert(new Ss.User { UserName = "x", IsActive = true, CreatedAt = default }).ToDiagnostics().Sql,
            sqlite: "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES (@p0, @p1, @p2) RETURNING \"UserId\"",
            pg:     "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES ($1, $2, $3) RETURNING \"UserId\"",
            mysql:  "INSERT INTO `users` (`UserName`, `IsActive`, `CreatedAt`) VALUES (?, ?, ?); SELECT LAST_INSERT_ID()",
            ss:     "INSERT INTO [users] ([UserName], [IsActive], [CreatedAt]) VALUES (@p0, @p1, @p2) OUTPUT INSERTED.[UserId]");
    }

    // Batch insert tests removed: old Values()/InsertMany() API has been replaced
    // by the column-selector batch API.

    #endregion

    #region Order Inserts

    [Test]
    public void Insert_SingleOrder()
    {
        AssertDialects(
            Lite.Orders().Insert(new Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ToDiagnostics().Sql,
            Pg.Orders().Insert(new Pg.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ToDiagnostics().Sql,
            My.Orders().Insert(new My.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ToDiagnostics().Sql,
            Ss.Orders().Insert(new Ss.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ToDiagnostics().Sql,
            sqlite: "INSERT INTO \"orders\" (\"UserId\", \"Total\", \"Status\", \"OrderDate\") VALUES (@p0, @p1, @p2, @p3) RETURNING \"OrderId\"",
            pg:     "INSERT INTO \"orders\" (\"UserId\", \"Total\", \"Status\", \"OrderDate\") VALUES ($1, $2, $3, $4) RETURNING \"OrderId\"",
            mysql:  "INSERT INTO `orders` (`UserId`, `Total`, `Status`, `OrderDate`) VALUES (?, ?, ?, ?); SELECT LAST_INSERT_ID()",
            ss:     "INSERT INTO [orders] ([UserId], [Total], [Status], [OrderDate]) VALUES (@p0, @p1, @p2, @p3) OUTPUT INSERTED.[OrderId]");
    }

    // Batch order insert SQL preview test removed: see Insert_BatchUsers comment above.
    // Batch execution is tested via ExecuteNonQueryAsync tests.

    #endregion

    #region OrderItem Inserts

    [Test]
    public void Insert_SingleOrderItem()
    {
        AssertDialects(
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
        await Lite.Users().Insert(new User { UserName = "x", IsActive = true, CreatedAt = default }).ExecuteNonQueryAsync();
        var liteSql = Connection.LastCommand!.CommandText;

        await Pg.Users().Insert(new Pg.User { UserName = "x", IsActive = true, CreatedAt = default }).ExecuteNonQueryAsync();
        var pgSql = Connection.LastCommand!.CommandText;

        await My.Users().Insert(new My.User { UserName = "x", IsActive = true, CreatedAt = default }).ExecuteNonQueryAsync();
        var mySql = Connection.LastCommand!.CommandText;

        await Ss.Users().Insert(new Ss.User { UserName = "x", IsActive = true, CreatedAt = default }).ExecuteNonQueryAsync();
        var ssSql = Connection.LastCommand!.CommandText;

        // ExecuteNonQueryAsync does not set identity column, so no RETURNING/OUTPUT clause
        AssertDialects(liteSql, pgSql, mySql, ssSql,
            sqlite: "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES (@p0, @p1, @p2)",
            pg:     "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES ($1, $2, $3)",
            mysql:  "INSERT INTO `users` (`UserName`, `IsActive`, `CreatedAt`) VALUES (?, ?, ?)",
            ss:     "INSERT INTO [users] ([UserName], [IsActive], [CreatedAt]) VALUES (@p0, @p1, @p2)");
    }

    // Batch insert tests (ExecuteNonQueryAsync_BatchUsers, ExecuteNonQueryAsync_InsertMany_Users)
    // removed: old Values()/InsertMany() API has been replaced by column-selector batch API.

    [Test]
    public async Task ExecuteNonQueryAsync_SingleOrder()
    {
        await Lite.Orders().Insert(new Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ExecuteNonQueryAsync();
        var liteSql = Connection.LastCommand!.CommandText;

        await Pg.Orders().Insert(new Pg.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ExecuteNonQueryAsync();
        var pgSql = Connection.LastCommand!.CommandText;

        await My.Orders().Insert(new My.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ExecuteNonQueryAsync();
        var mySql = Connection.LastCommand!.CommandText;

        await Ss.Orders().Insert(new Ss.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ExecuteNonQueryAsync();
        var ssSql = Connection.LastCommand!.CommandText;

        AssertDialects(liteSql, pgSql, mySql, ssSql,
            sqlite: "INSERT INTO \"orders\" (\"UserId\", \"Total\", \"Status\", \"OrderDate\") VALUES (@p0, @p1, @p2, @p3)",
            pg:     "INSERT INTO \"orders\" (\"UserId\", \"Total\", \"Status\", \"OrderDate\") VALUES ($1, $2, $3, $4)",
            mysql:  "INSERT INTO `orders` (`UserId`, `Total`, `Status`, `OrderDate`) VALUES (?, ?, ?, ?)",
            ss:     "INSERT INTO [orders] ([UserId], [Total], [Status], [OrderDate]) VALUES (@p0, @p1, @p2, @p3)");
    }

    #endregion

    #region ExecuteScalarAsync Cross-Dialect

    [Test]
    public async Task ExecuteScalarAsync_SingleUser_ReturnsIdentity()
    {
        Connection.ScalarResult = 42;

        await Lite.Users().Insert(new User { UserName = "x", IsActive = true, CreatedAt = default }).ExecuteScalarAsync<int>();
        var liteSql = Connection.LastCommand!.CommandText;

        await Pg.Users().Insert(new Pg.User { UserName = "x", IsActive = true, CreatedAt = default }).ExecuteScalarAsync<int>();
        var pgSql = Connection.LastCommand!.CommandText;

        // MySQL uses LAST_INSERT_ID() — LastCommand captures the second query
        await My.Users().Insert(new My.User { UserName = "x", IsActive = true, CreatedAt = default }).ExecuteScalarAsync<int>();
        var mySql = Connection.LastCommand!.CommandText;

        await Ss.Users().Insert(new Ss.User { UserName = "x", IsActive = true, CreatedAt = default }).ExecuteScalarAsync<int>();
        var ssSql = Connection.LastCommand!.CommandText;

        Assert.Multiple(() =>
        {
            Assert.That(liteSql, Is.EqualTo(
                "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES (@p0, @p1, @p2) RETURNING \"UserId\""), "SQLite");
            Assert.That(pgSql, Is.EqualTo(
                "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES ($1, $2, $3) RETURNING \"UserId\""), "PostgreSQL");
            Assert.That(mySql, Is.EqualTo(
                "INSERT INTO `users` (`UserName`, `IsActive`, `CreatedAt`) VALUES (?, ?, ?); SELECT LAST_INSERT_ID()"), "MySQL");
            Assert.That(ssSql, Is.EqualTo(
                "INSERT INTO [users] ([UserName], [IsActive], [CreatedAt]) VALUES (@p0, @p1, @p2) OUTPUT INSERTED.[UserId]"), "SqlServer");
        });
    }

    [Test]
    public async Task ExecuteScalarAsync_SingleOrder_ReturnsIdentity()
    {
        Connection.ScalarResult = 99;

        await Lite.Orders().Insert(new Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ExecuteScalarAsync<int>();
        var liteSql = Connection.LastCommand!.CommandText;

        await Pg.Orders().Insert(new Pg.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ExecuteScalarAsync<int>();
        var pgSql = Connection.LastCommand!.CommandText;

        await My.Orders().Insert(new My.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ExecuteScalarAsync<int>();
        var mySql = Connection.LastCommand!.CommandText;

        await Ss.Orders().Insert(new Ss.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ExecuteScalarAsync<int>();
        var ssSql = Connection.LastCommand!.CommandText;

        Assert.Multiple(() =>
        {
            Assert.That(liteSql, Is.EqualTo(
                "INSERT INTO \"orders\" (\"UserId\", \"Total\", \"Status\", \"OrderDate\") VALUES (@p0, @p1, @p2, @p3) RETURNING \"OrderId\""), "SQLite");
            Assert.That(pgSql, Is.EqualTo(
                "INSERT INTO \"orders\" (\"UserId\", \"Total\", \"Status\", \"OrderDate\") VALUES ($1, $2, $3, $4) RETURNING \"OrderId\""), "PostgreSQL");
            Assert.That(mySql, Is.EqualTo(
                "INSERT INTO `orders` (`UserId`, `Total`, `Status`, `OrderDate`) VALUES (?, ?, ?, ?); SELECT LAST_INSERT_ID()"), "MySQL");
            Assert.That(ssSql, Is.EqualTo(
                "INSERT INTO [orders] ([UserId], [Total], [Status], [OrderDate]) VALUES (@p0, @p1, @p2, @p3) OUTPUT INSERTED.[OrderId]"), "SqlServer");
        });
    }

    #endregion
}
