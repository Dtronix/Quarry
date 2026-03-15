using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;

#pragma warning disable QRY001

[TestFixture]
internal class CrossDialectEnumTests : CrossDialectTestBase
{
    [Test]
    public void Where_EnumCapturedVariable()
    {
        var priority = OrderPriority.Urgent;
        AssertDialects(
            Lite.Orders().Where(o => o.Priority == priority).ToTestCase(),
            Pg.Orders().Where(o => o.Priority == priority).ToTestCase(),
            My.Orders().Where(o => o.Priority == priority).ToTestCase(),
            Ss.Orders().Where(o => o.Priority == priority).ToTestCase(),
            sqlite: "SELECT * FROM \"orders\" WHERE (\"Priority\" = @p0)",
            pg:     "SELECT * FROM \"orders\" WHERE (\"Priority\" = @p0)",
            mysql:  "SELECT * FROM `orders` WHERE (`Priority` = @p0)",
            ss:     "SELECT * FROM [orders] WHERE ([Priority] = @p0)");
    }

    #region Boolean in INSERT

    [Test]
    public void Insert_WithBooleanColumn()
    {
        // Boolean values are parameterized in INSERT, so no literal TRUE/1 difference
        AssertDialects(
            Lite.Insert(new User { UserName = "x", IsActive = true }).ToSql(),
            Pg.Insert(new Pg.User { UserName = "x", IsActive = true }).ToSql(),
            My.Insert(new My.User { UserName = "x", IsActive = true }).ToSql(),
            Ss.Insert(new Ss.User { UserName = "x", IsActive = true }).ToSql(),
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
            Lite.Insert(new Order { UserId = 1, Total = 0m, Status = "x", Priority = OrderPriority.Urgent, OrderDate = default }).ToSql(),
            Pg.Insert(new Pg.Order { UserId = 1, Total = 0m, Status = "x", Priority = OrderPriority.Urgent, OrderDate = default }).ToSql(),
            My.Insert(new My.Order { UserId = 1, Total = 0m, Status = "x", Priority = OrderPriority.Urgent, OrderDate = default }).ToSql(),
            Ss.Insert(new Ss.Order { UserId = 1, Total = 0m, Status = "x", Priority = OrderPriority.Urgent, OrderDate = default }).ToSql(),
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
            Lite.Update<Order>().Set(o => o.Priority, OrderPriority.High).Where(o => o.OrderId == 1).ToTestCase(),
            Pg.Update<Pg.Order>().Set(o => o.Priority, OrderPriority.High).Where(o => o.OrderId == 1).ToTestCase(),
            My.Update<My.Order>().Set(o => o.Priority, OrderPriority.High).Where(o => o.OrderId == 1).ToTestCase(),
            Ss.Update<Ss.Order>().Set(o => o.Priority, OrderPriority.High).Where(o => o.OrderId == 1).ToTestCase(),
            sqlite: "UPDATE \"orders\" SET \"Priority\" = @p0 WHERE (\"OrderId\" = 1)",
            pg:     "UPDATE \"orders\" SET \"Priority\" = $1 WHERE (\"OrderId\" = 1)",
            mysql:  "UPDATE `orders` SET `Priority` = ? WHERE (`OrderId` = 1)",
            ss:     "UPDATE [orders] SET [Priority] = @p0 WHERE ([OrderId] = 1)");
    }

    #endregion
}
