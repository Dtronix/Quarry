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

        var lt= Lite.Users().Insert(new User { UserName = "x", IsActive = true, CreatedAt = default }).Prepare();
        var pg = Pg.Users().Insert(new Pg.User { UserName = "x", IsActive = true, CreatedAt = default }).Prepare();
        var my = My.Users().Insert(new My.User { UserName = "x", IsActive = true, CreatedAt = default }).Prepare();
        var ss = Ss.Users().Insert(new Ss.User { UserName = "x", IsActive = true, CreatedAt = default }).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES (@p0, @p1, @p2) RETURNING \"UserId\"",
            pg:     "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES ($1, $2, $3) RETURNING \"UserId\"",
            mysql:  "INSERT INTO `users` (`UserName`, `IsActive`, `CreatedAt`) VALUES (?, ?, ?); SELECT LAST_INSERT_ID()",
            ss:     "INSERT INTO [users] ([UserName], [IsActive], [CreatedAt]) OUTPUT INSERTED.[UserId] VALUES (@p0, @p1, @p2)");

        var newId = await lt.ExecuteScalarAsync<int>();
        Assert.That(newId, Is.GreaterThan(0));

        var pgNewId = await pg.ExecuteScalarAsync<int>();
        Assert.That(pgNewId, Is.GreaterThan(0));

        var myNewId = await my.ExecuteScalarAsync<int>();
        Assert.That(myNewId, Is.GreaterThan(0));

        var ssNewId = await ss.ExecuteScalarAsync<int>();
        Assert.That(ssNewId, Is.GreaterThan(0));
    }

    #endregion

    #region Order Inserts

    [Test]
    public async Task Insert_SingleOrder()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt= Lite.Orders().Insert(new Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).Prepare();
        var pg = Pg.Orders().Insert(new Pg.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).Prepare();
        var my = My.Orders().Insert(new My.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).Prepare();
        var ss = Ss.Orders().Insert(new Ss.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "INSERT INTO \"orders\" (\"UserId\", \"Total\", \"Status\", \"OrderDate\") VALUES (@p0, @p1, @p2, @p3) RETURNING \"OrderId\"",
            pg:     "INSERT INTO \"orders\" (\"UserId\", \"Total\", \"Status\", \"OrderDate\") VALUES ($1, $2, $3, $4) RETURNING \"OrderId\"",
            mysql:  "INSERT INTO `orders` (`UserId`, `Total`, `Status`, `OrderDate`) VALUES (?, ?, ?, ?); SELECT LAST_INSERT_ID()",
            ss:     "INSERT INTO [orders] ([UserId], [Total], [Status], [OrderDate]) OUTPUT INSERTED.[OrderId] VALUES (@p0, @p1, @p2, @p3)");

        var newId = await lt.ExecuteScalarAsync<int>();
        Assert.That(newId, Is.GreaterThan(0));

        var pgNewId = await pg.ExecuteScalarAsync<int>();
        Assert.That(pgNewId, Is.GreaterThan(0));

        var myNewId = await my.ExecuteScalarAsync<int>();
        Assert.That(myNewId, Is.GreaterThan(0));

        var ssNewId = await ss.ExecuteScalarAsync<int>();
        Assert.That(ssNewId, Is.GreaterThan(0));
    }

    #endregion

    #region OrderItem Inserts

    [Test]
    public async Task Insert_SingleOrderItem()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt= Lite.OrderItems().Insert(new OrderItem { OrderId = 1, ProductName = "x", Quantity = 0, UnitPrice = 0m, LineTotal = 0m }).Prepare();
        var pg = Pg.OrderItems().Insert(new Pg.OrderItem { OrderId = 1, ProductName = "x", Quantity = 0, UnitPrice = 0m, LineTotal = 0m }).Prepare();
        var my = My.OrderItems().Insert(new My.OrderItem { OrderId = 1, ProductName = "x", Quantity = 0, UnitPrice = 0m, LineTotal = 0m }).Prepare();
        var ss = Ss.OrderItems().Insert(new Ss.OrderItem { OrderId = 1, ProductName = "x", Quantity = 0, UnitPrice = 0m, LineTotal = 0m }).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "INSERT INTO \"order_items\" (\"OrderId\", \"ProductName\", \"Quantity\", \"UnitPrice\", \"LineTotal\") VALUES (@p0, @p1, @p2, @p3, @p4) RETURNING \"OrderItemId\"",
            pg:     "INSERT INTO \"order_items\" (\"OrderId\", \"ProductName\", \"Quantity\", \"UnitPrice\", \"LineTotal\") VALUES ($1, $2, $3, $4, $5) RETURNING \"OrderItemId\"",
            mysql:  "INSERT INTO `order_items` (`OrderId`, `ProductName`, `Quantity`, `UnitPrice`, `LineTotal`) VALUES (?, ?, ?, ?, ?); SELECT LAST_INSERT_ID()",
            ss:     "INSERT INTO [order_items] ([OrderId], [ProductName], [Quantity], [UnitPrice], [LineTotal]) OUTPUT INSERTED.[OrderItemId] VALUES (@p0, @p1, @p2, @p3, @p4)");

        var newId = await lt.ExecuteScalarAsync<int>();
        Assert.That(newId, Is.GreaterThan(0));

        var pgNewId = await pg.ExecuteScalarAsync<int>();
        Assert.That(pgNewId, Is.GreaterThan(0));

        var myNewId = await my.ExecuteScalarAsync<int>();
        Assert.That(myNewId, Is.GreaterThan(0));

        var ssNewId = await ss.ExecuteScalarAsync<int>();
        Assert.That(ssNewId, Is.GreaterThan(0));
    }

    #endregion

    #region ExecuteNonQueryAsync Cross-Dialect

    [Test]
    public async Task ExecuteNonQueryAsync_SingleUser()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt= Lite.Users().Insert(new User { UserName = "x", IsActive = true, CreatedAt = default }).Prepare();
        var pg = Pg.Users().Insert(new Pg.User { UserName = "x", IsActive = true, CreatedAt = default }).Prepare();
        var my = My.Users().Insert(new My.User { UserName = "x", IsActive = true, CreatedAt = default }).Prepare();
        var ss = Ss.Users().Insert(new Ss.User { UserName = "x", IsActive = true, CreatedAt = default }).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES (@p0, @p1, @p2) RETURNING \"UserId\"",
            pg:     "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES ($1, $2, $3) RETURNING \"UserId\"",
            mysql:  "INSERT INTO `users` (`UserName`, `IsActive`, `CreatedAt`) VALUES (?, ?, ?); SELECT LAST_INSERT_ID()",
            ss:     "INSERT INTO [users] ([UserName], [IsActive], [CreatedAt]) OUTPUT INSERTED.[UserId] VALUES (@p0, @p1, @p2)");

        var affected = await lt.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));

        var pgAffected = await pg.ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(1));

        var myAffected = await my.ExecuteNonQueryAsync();
        Assert.That(myAffected, Is.EqualTo(1));

        var ssAffected = await ss.ExecuteNonQueryAsync();
        Assert.That(ssAffected, Is.EqualTo(1));
    }

    [Test]
    public async Task ExecuteNonQueryAsync_SingleOrder()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt= Lite.Orders().Insert(new Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).Prepare();
        var pg = Pg.Orders().Insert(new Pg.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).Prepare();
        var my = My.Orders().Insert(new My.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).Prepare();
        var ss = Ss.Orders().Insert(new Ss.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "INSERT INTO \"orders\" (\"UserId\", \"Total\", \"Status\", \"OrderDate\") VALUES (@p0, @p1, @p2, @p3) RETURNING \"OrderId\"",
            pg:     "INSERT INTO \"orders\" (\"UserId\", \"Total\", \"Status\", \"OrderDate\") VALUES ($1, $2, $3, $4) RETURNING \"OrderId\"",
            mysql:  "INSERT INTO `orders` (`UserId`, `Total`, `Status`, `OrderDate`) VALUES (?, ?, ?, ?); SELECT LAST_INSERT_ID()",
            ss:     "INSERT INTO [orders] ([UserId], [Total], [Status], [OrderDate]) OUTPUT INSERTED.[OrderId] VALUES (@p0, @p1, @p2, @p3)");

        var affected = await lt.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));

        var pgAffected = await pg.ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(1));

        var myAffected = await my.ExecuteNonQueryAsync();
        Assert.That(myAffected, Is.EqualTo(1));

        var ssAffected = await ss.ExecuteNonQueryAsync();
        Assert.That(ssAffected, Is.EqualTo(1));
    }

    #endregion

    #region ExecuteScalarAsync Cross-Dialect

    [Test]
    public async Task ExecuteScalarAsync_SingleUser_ReturnsIdentity()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt= Lite.Users().Insert(new User { UserName = "x", IsActive = true, CreatedAt = default }).Prepare();
        var pg = Pg.Users().Insert(new Pg.User { UserName = "x", IsActive = true, CreatedAt = default }).Prepare();
        var my = My.Users().Insert(new My.User { UserName = "x", IsActive = true, CreatedAt = default }).Prepare();
        var ss = Ss.Users().Insert(new Ss.User { UserName = "x", IsActive = true, CreatedAt = default }).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES (@p0, @p1, @p2) RETURNING \"UserId\"",
            pg:     "INSERT INTO \"users\" (\"UserName\", \"IsActive\", \"CreatedAt\") VALUES ($1, $2, $3) RETURNING \"UserId\"",
            mysql:  "INSERT INTO `users` (`UserName`, `IsActive`, `CreatedAt`) VALUES (?, ?, ?); SELECT LAST_INSERT_ID()",
            ss:     "INSERT INTO [users] ([UserName], [IsActive], [CreatedAt]) OUTPUT INSERTED.[UserId] VALUES (@p0, @p1, @p2)");

        var newId = await lt.ExecuteScalarAsync<int>();
        Assert.That(newId, Is.GreaterThan(0));

        var pgNewId = await pg.ExecuteScalarAsync<int>();
        Assert.That(pgNewId, Is.GreaterThan(0));

        var myNewId = await my.ExecuteScalarAsync<int>();
        Assert.That(myNewId, Is.GreaterThan(0));

        var ssNewId = await ss.ExecuteScalarAsync<int>();
        Assert.That(ssNewId, Is.GreaterThan(0));
    }

    [Test]
    public async Task ExecuteScalarAsync_SingleOrder_ReturnsIdentity()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt= Lite.Orders().Insert(new Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).Prepare();
        var pg = Pg.Orders().Insert(new Pg.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).Prepare();
        var my = My.Orders().Insert(new My.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).Prepare();
        var ss = Ss.Orders().Insert(new Ss.Order { UserId = 1, Total = 0m, Status = "x", OrderDate = default }).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "INSERT INTO \"orders\" (\"UserId\", \"Total\", \"Status\", \"OrderDate\") VALUES (@p0, @p1, @p2, @p3) RETURNING \"OrderId\"",
            pg:     "INSERT INTO \"orders\" (\"UserId\", \"Total\", \"Status\", \"OrderDate\") VALUES ($1, $2, $3, $4) RETURNING \"OrderId\"",
            mysql:  "INSERT INTO `orders` (`UserId`, `Total`, `Status`, `OrderDate`) VALUES (?, ?, ?, ?); SELECT LAST_INSERT_ID()",
            ss:     "INSERT INTO [orders] ([UserId], [Total], [Status], [OrderDate]) OUTPUT INSERTED.[OrderId] VALUES (@p0, @p1, @p2, @p3)");

        var newId = await lt.ExecuteScalarAsync<int>();
        Assert.That(newId, Is.GreaterThan(0));

        var pgNewId = await pg.ExecuteScalarAsync<int>();
        Assert.That(pgNewId, Is.GreaterThan(0));

        var myNewId = await my.ExecuteScalarAsync<int>();
        Assert.That(myNewId, Is.GreaterThan(0));

        var ssNewId = await ss.ExecuteScalarAsync<int>();
        Assert.That(ssNewId, Is.GreaterThan(0));
    }

    #endregion
}
