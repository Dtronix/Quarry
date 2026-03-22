using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


[TestFixture]
internal class CrossDialectEnumTests : CrossDialectTestBase
{
    [Test]
    public void Where_EnumCapturedVariable()
    {
        var priority = OrderPriority.Urgent;
        AssertDialects(
            Lite.Orders().Where(o => o.Priority == priority).ToDiagnostics(),
            Pg.Orders().Where(o => o.Priority == priority).ToDiagnostics(),
            My.Orders().Where(o => o.Priority == priority).ToDiagnostics(),
            Ss.Orders().Where(o => o.Priority == priority).ToDiagnostics(),
            sqlite: "SELECT * FROM \"orders\" WHERE \"Priority\" = @p0",
            pg:     "SELECT * FROM \"orders\" WHERE \"Priority\" = $1",
            mysql:  "SELECT * FROM `orders` WHERE `Priority` = ?",
            ss:     "SELECT * FROM [orders] WHERE [Priority] = @p0");
    }

    #region Boolean in INSERT

    [Test]
    public void Insert_WithBooleanColumn()
    {
        // Boolean values are parameterized in INSERT, so no literal TRUE/1 difference
        AssertDialects(
            Lite.Users().Insert(new User { UserName = "x", IsActive = true }).ToDiagnostics().Sql,
            Pg.Users().Insert(new Pg.User { UserName = "x", IsActive = true }).ToDiagnostics().Sql,
            My.Users().Insert(new My.User { UserName = "x", IsActive = true }).ToDiagnostics().Sql,
            Ss.Users().Insert(new Ss.User { UserName = "x", IsActive = true }).ToDiagnostics().Sql,
            sqlite: "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES (@p0, @p1) RETURNING \"UserId\"",
            pg:     "INSERT INTO \"users\" (\"UserName\", \"IsActive\") VALUES ($1, $2) RETURNING \"UserId\"",
            mysql:  "INSERT INTO `users` (`UserName`, `IsActive`) VALUES (?, ?)",
            ss:     "INSERT INTO [users] ([UserName], [IsActive]) VALUES (@p0, @p1) OUTPUT INSERTED.[UserId]");
    }

    #endregion

    #region Enum in INSERT

    [Test]
    public void Insert_WithEnumColumn()
    {
        AssertDialects(
            Lite.Orders().Insert(new Order { UserId = 1, Total = 0m, Status = "x", Priority = OrderPriority.Urgent, OrderDate = default }).ToDiagnostics().Sql,
            Pg.Orders().Insert(new Pg.Order { UserId = 1, Total = 0m, Status = "x", Priority = OrderPriority.Urgent, OrderDate = default }).ToDiagnostics().Sql,
            My.Orders().Insert(new My.Order { UserId = 1, Total = 0m, Status = "x", Priority = OrderPriority.Urgent, OrderDate = default }).ToDiagnostics().Sql,
            Ss.Orders().Insert(new Ss.Order { UserId = 1, Total = 0m, Status = "x", Priority = OrderPriority.Urgent, OrderDate = default }).ToDiagnostics().Sql,
            sqlite: "INSERT INTO \"orders\" (\"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\") VALUES (@p0, @p1, @p2, @p3, @p4) RETURNING \"OrderId\"",
            pg:     "INSERT INTO \"orders\" (\"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\") VALUES ($1, $2, $3, $4, $5) RETURNING \"OrderId\"",
            mysql:  "INSERT INTO `orders` (`UserId`, `Total`, `Status`, `Priority`, `OrderDate`) VALUES (?, ?, ?, ?, ?)",
            ss:     "INSERT INTO [orders] ([UserId], [Total], [Status], [Priority], [OrderDate]) VALUES (@p0, @p1, @p2, @p3, @p4) OUTPUT INSERTED.[OrderId]");
    }

    #endregion

    #region Enum in UPDATE SET

    [Test]
    public void Update_Set_EnumColumn()
    {
        AssertDialects(
            Lite.Orders().Update().Set(o => o.Priority = OrderPriority.High).Where(o => o.OrderId == 1).ToDiagnostics(),
            Pg.Orders().Update().Set(o => o.Priority = OrderPriority.High).Where(o => o.OrderId == 1).ToDiagnostics(),
            My.Orders().Update().Set(o => o.Priority = OrderPriority.High).Where(o => o.OrderId == 1).ToDiagnostics(),
            Ss.Orders().Update().Set(o => o.Priority = OrderPriority.High).Where(o => o.OrderId == 1).ToDiagnostics(),
            sqlite: "UPDATE \"orders\" SET \"Priority\" = 2 WHERE \"OrderId\" = 1",
            pg:     "UPDATE \"orders\" SET \"Priority\" = 2 WHERE \"OrderId\" = 1",
            mysql:  "UPDATE `orders` SET `Priority` = 2 WHERE `OrderId` = 1",
            ss:     "UPDATE [orders] SET [Priority] = 2 WHERE [OrderId] = 1");
    }

    #endregion
}
