using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;

#pragma warning disable QRY001

[TestFixture]
internal class CrossDialectInsertTests : CrossDialectTestBase
{
    #region User Inserts

    [Test]
    public void Insert_SingleUser()
    {
        AssertDialects(
            Lite.Insert(new User { UserName = "x", IsActive = true, CreatedAt = default }).ToSql(),
            Pg.Insert(new Pg.User { UserName = "x", IsActive = true, CreatedAt = default }).ToSql(),
            My.Insert(new My.User { UserName = "x", IsActive = true, CreatedAt = default }).ToSql(),
            Ss.Insert(new Ss.User { UserName = "x", IsActive = true, CreatedAt = default }).ToSql(),
            sqlite: "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES (@p0, @p1, @p2) RETURNING \"UserId\"",
            pg:     "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES ($1, $2, $3) RETURNING \"UserId\"",
            mysql:  "INSERT INTO `users` (`UserName`, `IsActive`, `CreatedAt`) VALUES (?, ?, ?)",
            ss:     "INSERT INTO [users] ([UserName], [IsActive], [CreatedAt]) VALUES (@p0, @p1, @p2) OUTPUT INSERTED.[UserId]");
    }

    [Test]
    public void Insert_BatchUsers()
    {
        AssertDialects(
            Lite.Insert(new User { UserName = "x" }).Values(new User { UserName = "y" }).ToSql(),
            Pg.Insert(new Pg.User { UserName = "x" }).Values(new Pg.User { UserName = "y" }).ToSql(),
            My.Insert(new My.User { UserName = "x" }).Values(new My.User { UserName = "y" }).ToSql(),
            Ss.Insert(new Ss.User { UserName = "x" }).Values(new Ss.User { UserName = "y" }).ToSql(),
            sqlite: "INSERT INTO \"users\" (\"UserName\") VALUES (@p0), (@p1) RETURNING \"UserId\"",
            pg:     "INSERT INTO \"users\" (\"UserName\") VALUES ($1), ($2) RETURNING \"UserId\"",
            mysql:  "INSERT INTO `users` (`UserName`) VALUES (?), (?)",
            ss:     "INSERT INTO [users] ([UserName]) VALUES (@p0), (@p1) OUTPUT INSERTED.[UserId]");
    }

    [Test]
    public void InsertMany_Users()
    {
        AssertDialects(
            Lite.InsertMany(new[] { new User { UserName = "a" }, new User { UserName = "b" }, new User { UserName = "c" } }).ToSql(),
            Pg.InsertMany(new[] { new Pg.User { UserName = "a" }, new Pg.User { UserName = "b" }, new Pg.User { UserName = "c" } }).ToSql(),
            My.InsertMany(new[] { new My.User { UserName = "a" }, new My.User { UserName = "b" }, new My.User { UserName = "c" } }).ToSql(),
            Ss.InsertMany(new[] { new Ss.User { UserName = "a" }, new Ss.User { UserName = "b" }, new Ss.User { UserName = "c" } }).ToSql(),
            sqlite: "INSERT INTO \"users\" (\"UserName\") VALUES (@p0), (@p1), (@p2) RETURNING \"UserId\"",
            pg:     "INSERT INTO \"users\" (\"UserName\") VALUES ($1), ($2), ($3) RETURNING \"UserId\"",
            mysql:  "INSERT INTO `users` (`UserName`) VALUES (?), (?), (?)",
            ss:     "INSERT INTO [users] ([UserName]) VALUES (@p0), (@p1), (@p2) OUTPUT INSERTED.[UserId]");
    }

    #endregion

    #region Order Inserts

    [Test]
    public void Insert_SingleOrder()
    {
        AssertDialects(
            Lite.Insert(new Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ToSql(),
            Pg.Insert(new Pg.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ToSql(),
            My.Insert(new My.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ToSql(),
            Ss.Insert(new Ss.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ToSql(),
            sqlite: "INSERT INTO \"orders\" (\"UserId\", \"Total\", \"Status\", \"OrderDate\") VALUES (@p0, @p1, @p2, @p3) RETURNING \"OrderId\"",
            pg:     "INSERT INTO \"orders\" (\"UserId\", \"Total\", \"Status\", \"OrderDate\") VALUES ($1, $2, $3, $4) RETURNING \"OrderId\"",
            mysql:  "INSERT INTO `orders` (`UserId`, `Total`, `Status`, `OrderDate`) VALUES (?, ?, ?, ?)",
            ss:     "INSERT INTO [orders] ([UserId], [Total], [Status], [OrderDate]) VALUES (@p0, @p1, @p2, @p3) OUTPUT INSERTED.[OrderId]");
    }

    [Test]
    public void Insert_BatchOrders()
    {
        AssertDialects(
            Lite.Insert(new Order { UserId = 1, Status = "x" }).Values(new Order { UserId = 2, Status = "y" }).ToSql(),
            Pg.Insert(new Pg.Order { UserId = 1, Status = "x" }).Values(new Pg.Order { UserId = 2, Status = "y" }).ToSql(),
            My.Insert(new My.Order { UserId = 1, Status = "x" }).Values(new My.Order { UserId = 2, Status = "y" }).ToSql(),
            Ss.Insert(new Ss.Order { UserId = 1, Status = "x" }).Values(new Ss.Order { UserId = 2, Status = "y" }).ToSql(),
            sqlite: "INSERT INTO \"orders\" (\"UserId\", \"Status\") VALUES (@p0, @p1), (@p2, @p3) RETURNING \"OrderId\"",
            pg:     "INSERT INTO \"orders\" (\"UserId\", \"Status\") VALUES ($1, $2), ($3, $4) RETURNING \"OrderId\"",
            mysql:  "INSERT INTO `orders` (`UserId`, `Status`) VALUES (?, ?), (?, ?)",
            ss:     "INSERT INTO [orders] ([UserId], [Status]) VALUES (@p0, @p1), (@p2, @p3) OUTPUT INSERTED.[OrderId]");
    }

    #endregion

    #region OrderItem Inserts

    [Test]
    public void Insert_SingleOrderItem()
    {
        AssertDialects(
            Lite.Insert(new OrderItem { OrderId = 1, ProductName = "x", Quantity = 0, UnitPrice = 0m, LineTotal = 0m }).ToSql(),
            Pg.Insert(new Pg.OrderItem { OrderId = 1, ProductName = "x", Quantity = 0, UnitPrice = 0m, LineTotal = 0m }).ToSql(),
            My.Insert(new My.OrderItem { OrderId = 1, ProductName = "x", Quantity = 0, UnitPrice = 0m, LineTotal = 0m }).ToSql(),
            Ss.Insert(new Ss.OrderItem { OrderId = 1, ProductName = "x", Quantity = 0, UnitPrice = 0m, LineTotal = 0m }).ToSql(),
            sqlite: "INSERT INTO \"order_items\" (\"OrderId\", \"ProductName\", \"Quantity\", \"UnitPrice\", \"LineTotal\") VALUES (@p0, @p1, @p2, @p3, @p4) RETURNING \"OrderItemId\"",
            pg:     "INSERT INTO \"order_items\" (\"OrderId\", \"ProductName\", \"Quantity\", \"UnitPrice\", \"LineTotal\") VALUES ($1, $2, $3, $4, $5) RETURNING \"OrderItemId\"",
            mysql:  "INSERT INTO `order_items` (`OrderId`, `ProductName`, `Quantity`, `UnitPrice`, `LineTotal`) VALUES (?, ?, ?, ?, ?)",
            ss:     "INSERT INTO [order_items] ([OrderId], [ProductName], [Quantity], [UnitPrice], [LineTotal]) VALUES (@p0, @p1, @p2, @p3, @p4) OUTPUT INSERTED.[OrderItemId]");
    }

    #endregion

    #region ExecuteNonQueryAsync Cross-Dialect

    [Test]
    public async Task ExecuteNonQueryAsync_SingleUser()
    {
        await Lite.Insert(new User { UserName = "x", IsActive = true, CreatedAt = default }).ExecuteNonQueryAsync();
        var liteSql = Connection.LastCommand!.CommandText;

        await Pg.Insert(new Pg.User { UserName = "x", IsActive = true, CreatedAt = default }).ExecuteNonQueryAsync();
        var pgSql = Connection.LastCommand!.CommandText;

        await My.Insert(new My.User { UserName = "x", IsActive = true, CreatedAt = default }).ExecuteNonQueryAsync();
        var mySql = Connection.LastCommand!.CommandText;

        await Ss.Insert(new Ss.User { UserName = "x", IsActive = true, CreatedAt = default }).ExecuteNonQueryAsync();
        var ssSql = Connection.LastCommand!.CommandText;

        // ExecuteNonQueryAsync does not set identity column, so no RETURNING/OUTPUT clause
        AssertDialects(liteSql, pgSql, mySql, ssSql,
            sqlite: "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES (@p0, @p1, @p2)",
            pg:     "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES ($1, $2, $3)",
            mysql:  "INSERT INTO `users` (`UserName`, `IsActive`, `CreatedAt`) VALUES (?, ?, ?)",
            ss:     "INSERT INTO [users] ([UserName], [IsActive], [CreatedAt]) VALUES (@p0, @p1, @p2)");
    }

    [Test]
    public async Task ExecuteNonQueryAsync_BatchUsers()
    {
        await Lite.Insert(new User { UserName = "x" }).Values(new User { UserName = "y" }).ExecuteNonQueryAsync();
        var liteSql = Connection.LastCommand!.CommandText;

        await Pg.Insert(new Pg.User { UserName = "x" }).Values(new Pg.User { UserName = "y" }).ExecuteNonQueryAsync();
        var pgSql = Connection.LastCommand!.CommandText;

        await My.Insert(new My.User { UserName = "x" }).Values(new My.User { UserName = "y" }).ExecuteNonQueryAsync();
        var mySql = Connection.LastCommand!.CommandText;

        await Ss.Insert(new Ss.User { UserName = "x" }).Values(new Ss.User { UserName = "y" }).ExecuteNonQueryAsync();
        var ssSql = Connection.LastCommand!.CommandText;

        AssertDialects(liteSql, pgSql, mySql, ssSql,
            sqlite: "INSERT INTO \"users\" (\"UserName\") VALUES (@p0), (@p1)",
            pg:     "INSERT INTO \"users\" (\"UserName\") VALUES ($1), ($2)",
            mysql:  "INSERT INTO `users` (`UserName`) VALUES (?), (?)",
            ss:     "INSERT INTO [users] ([UserName]) VALUES (@p0), (@p1)");
    }

    [Test]
    public async Task ExecuteNonQueryAsync_InsertMany_Users()
    {
        await Lite.InsertMany(new[] { new User { UserName = "a" }, new User { UserName = "b" }, new User { UserName = "c" } }).ExecuteNonQueryAsync();
        var liteSql = Connection.LastCommand!.CommandText;

        await Pg.InsertMany(new[] { new Pg.User { UserName = "a" }, new Pg.User { UserName = "b" }, new Pg.User { UserName = "c" } }).ExecuteNonQueryAsync();
        var pgSql = Connection.LastCommand!.CommandText;

        await My.InsertMany(new[] { new My.User { UserName = "a" }, new My.User { UserName = "b" }, new My.User { UserName = "c" } }).ExecuteNonQueryAsync();
        var mySql = Connection.LastCommand!.CommandText;

        await Ss.InsertMany(new[] { new Ss.User { UserName = "a" }, new Ss.User { UserName = "b" }, new Ss.User { UserName = "c" } }).ExecuteNonQueryAsync();
        var ssSql = Connection.LastCommand!.CommandText;

        AssertDialects(liteSql, pgSql, mySql, ssSql,
            sqlite: "INSERT INTO \"users\" (\"UserName\") VALUES (@p0), (@p1), (@p2)",
            pg:     "INSERT INTO \"users\" (\"UserName\") VALUES ($1), ($2), ($3)",
            mysql:  "INSERT INTO `users` (`UserName`) VALUES (?), (?), (?)",
            ss:     "INSERT INTO [users] ([UserName]) VALUES (@p0), (@p1), (@p2)");
    }

    [Test]
    public async Task ExecuteNonQueryAsync_SingleOrder()
    {
        await Lite.Insert(new Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ExecuteNonQueryAsync();
        var liteSql = Connection.LastCommand!.CommandText;

        await Pg.Insert(new Pg.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ExecuteNonQueryAsync();
        var pgSql = Connection.LastCommand!.CommandText;

        await My.Insert(new My.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ExecuteNonQueryAsync();
        var mySql = Connection.LastCommand!.CommandText;

        await Ss.Insert(new Ss.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ExecuteNonQueryAsync();
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

        await Lite.Insert(new User { UserName = "x", IsActive = true, CreatedAt = default }).ExecuteScalarAsync<int>();
        var liteSql = Connection.LastCommand!.CommandText;

        await Pg.Insert(new Pg.User { UserName = "x", IsActive = true, CreatedAt = default }).ExecuteScalarAsync<int>();
        var pgSql = Connection.LastCommand!.CommandText;

        // MySQL uses LAST_INSERT_ID() — LastCommand captures the second query
        await My.Insert(new My.User { UserName = "x", IsActive = true, CreatedAt = default }).ExecuteScalarAsync<int>();
        var mySql = Connection.LastCommand!.CommandText;

        await Ss.Insert(new Ss.User { UserName = "x", IsActive = true, CreatedAt = default }).ExecuteScalarAsync<int>();
        var ssSql = Connection.LastCommand!.CommandText;

        // SQLite/PG/SS: RETURNING/OUTPUT in the INSERT itself (captured as CommandText)
        // MySQL: second query is SELECT LAST_INSERT_ID() (that's what LastCommand captures)
        Assert.Multiple(() =>
        {
            Assert.That(liteSql, Is.EqualTo(
                "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES (@p0, @p1, @p2) RETURNING \"UserId\""), "SQLite");
            Assert.That(pgSql, Is.EqualTo(
                "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES ($1, $2, $3) RETURNING \"UserId\""), "PostgreSQL");
            Assert.That(mySql, Is.EqualTo("SELECT LAST_INSERT_ID()"), "MySQL");
            Assert.That(ssSql, Is.EqualTo(
                "INSERT INTO [users] ([UserName], [IsActive], [CreatedAt]) VALUES (@p0, @p1, @p2) OUTPUT INSERTED.[UserId]"), "SqlServer");
        });
    }

    [Test]
    public async Task ExecuteScalarAsync_SingleOrder_ReturnsIdentity()
    {
        Connection.ScalarResult = 99;

        await Lite.Insert(new Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ExecuteScalarAsync<int>();
        var liteSql = Connection.LastCommand!.CommandText;

        await Pg.Insert(new Pg.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ExecuteScalarAsync<int>();
        var pgSql = Connection.LastCommand!.CommandText;

        await My.Insert(new My.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ExecuteScalarAsync<int>();
        var mySql = Connection.LastCommand!.CommandText;

        await Ss.Insert(new Ss.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).ExecuteScalarAsync<int>();
        var ssSql = Connection.LastCommand!.CommandText;

        Assert.Multiple(() =>
        {
            Assert.That(liteSql, Is.EqualTo(
                "INSERT INTO \"orders\" (\"UserId\", \"Total\", \"Status\", \"OrderDate\") VALUES (@p0, @p1, @p2, @p3) RETURNING \"OrderId\""), "SQLite");
            Assert.That(pgSql, Is.EqualTo(
                "INSERT INTO \"orders\" (\"UserId\", \"Total\", \"Status\", \"OrderDate\") VALUES ($1, $2, $3, $4) RETURNING \"OrderId\""), "PostgreSQL");
            Assert.That(mySql, Is.EqualTo("SELECT LAST_INSERT_ID()"), "MySQL");
            Assert.That(ssSql, Is.EqualTo(
                "INSERT INTO [orders] ([UserId], [Total], [Status], [OrderDate]) VALUES (@p0, @p1, @p2, @p3) OUTPUT INSERTED.[OrderId]"), "SqlServer");
        });
    }

    #endregion
}
