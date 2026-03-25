using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


[TestFixture]
internal class CrossDialectEnumTests
{
    [Test]
    public async Task Where_EnumCapturedVariable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var priority = OrderPriority.Urgent;

        var lite = Lite.Orders().Where(o => o.Priority == priority).Select(o => (o.OrderId, o.Total)).Prepare();
        var pg   = Pg.Orders().Where(o => o.Priority == priority).Select(o => (o.OrderId, o.Total)).Prepare();
        var my   = My.Orders().Where(o => o.Priority == priority).Select(o => (o.OrderId, o.Total)).Prepare();
        var ss   = Ss.Orders().Where(o => o.Priority == priority).Select(o => (o.OrderId, o.Total)).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"OrderId\", \"Total\" FROM \"orders\" WHERE \"Priority\" = @p0",
            pg:     "SELECT \"OrderId\", \"Total\" FROM \"orders\" WHERE \"Priority\" = $1",
            mysql:  "SELECT `OrderId`, `Total` FROM `orders` WHERE `Priority` = ?",
            ss:     "SELECT [OrderId], [Total] FROM [orders] WHERE [Priority] = @p0");

        // Priority: Order1=2(High), Order2=1(Normal), Order3=3(Urgent) — only Order3 matches Urgent(3)
        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo((3, 150.00m)));
    }

    #region Boolean in INSERT

    [Test]
    public async Task Insert_WithBooleanColumn()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Boolean values are parameterized in INSERT, so no literal TRUE/1 difference
        QueryTestHarness.AssertDialects(
            Lite.Users().Insert(new User { UserName = "x", IsActive = true }).ToDiagnostics().Sql,
            Pg.Users().Insert(new Pg.User { UserName = "x", IsActive = true }).ToDiagnostics().Sql,
            My.Users().Insert(new My.User { UserName = "x", IsActive = true }).ToDiagnostics().Sql,
            Ss.Users().Insert(new Ss.User { UserName = "x", IsActive = true }).ToDiagnostics().Sql,
            sqlite: "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES (@p0, @p1) RETURNING \"UserId\"",
            pg:     "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES ($1, $2) RETURNING \"UserId\"",
            mysql:  "INSERT INTO `users` (`UserName`, `IsActive`) VALUES (?, ?); SELECT LAST_INSERT_ID()",
            ss:     "INSERT INTO [users] ([UserName], [IsActive]) VALUES (@p0, @p1) OUTPUT INSERTED.[UserId]");
    }

    #endregion

    #region Enum in INSERT

    [Test]
    public async Task Insert_WithEnumColumn()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        QueryTestHarness.AssertDialects(
            Lite.Orders().Insert(new Order { UserId = 1, Total = 0m, Status = "x", Priority = OrderPriority.Urgent, OrderDate = default }).ToDiagnostics().Sql,
            Pg.Orders().Insert(new Pg.Order { UserId = 1, Total = 0m, Status = "x", Priority = OrderPriority.Urgent, OrderDate = default }).ToDiagnostics().Sql,
            My.Orders().Insert(new My.Order { UserId = 1, Total = 0m, Status = "x", Priority = OrderPriority.Urgent, OrderDate = default }).ToDiagnostics().Sql,
            Ss.Orders().Insert(new Ss.Order { UserId = 1, Total = 0m, Status = "x", Priority = OrderPriority.Urgent, OrderDate = default }).ToDiagnostics().Sql,
            sqlite: "INSERT INTO \"orders\" (\"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\") VALUES (@p0, @p1, @p2, @p3, @p4) RETURNING \"OrderId\"",
            pg:     "INSERT INTO \"orders\" (\"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\") VALUES ($1, $2, $3, $4, $5) RETURNING \"OrderId\"",
            mysql:  "INSERT INTO `orders` (`UserId`, `Total`, `Status`, `Priority`, `OrderDate`) VALUES (?, ?, ?, ?, ?); SELECT LAST_INSERT_ID()",
            ss:     "INSERT INTO [orders] ([UserId], [Total], [Status], [Priority], [OrderDate]) VALUES (@p0, @p1, @p2, @p3, @p4) OUTPUT INSERTED.[OrderId]");
    }

    #endregion

    #region Enum in UPDATE SET

    [Test]
    public async Task Update_Set_EnumColumn()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Orders().Update().Set(o => o.Priority = OrderPriority.High).Where(o => o.OrderId == 1).Prepare();
        var pg   = Pg.Orders().Update().Set(o => o.Priority = OrderPriority.High).Where(o => o.OrderId == 1).Prepare();
        var my   = My.Orders().Update().Set(o => o.Priority = OrderPriority.High).Where(o => o.OrderId == 1).Prepare();
        var ss   = Ss.Orders().Update().Set(o => o.Priority = OrderPriority.High).Where(o => o.OrderId == 1).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "UPDATE \"orders\" SET \"Priority\" = 2 WHERE \"OrderId\" = 1",
            pg:     "UPDATE \"orders\" SET \"Priority\" = 2 WHERE \"OrderId\" = 1",
            mysql:  "UPDATE `orders` SET `Priority` = 2 WHERE `OrderId` = 1",
            ss:     "UPDATE [orders] SET [Priority] = 2 WHERE [OrderId] = 1");

        var affected = await lite.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));
    }

    #endregion
}
