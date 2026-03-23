using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


[TestFixture]
internal class CrossDialectDeleteTests : CrossDialectTestBase
{
    #region Basic Where

    [Test]
    public void Delete_Where_Equality()
    {
        AssertDialects(
            Lite.Users().Delete().Where(u => u.UserId == 1).ToDiagnostics(),
            Pg.Users().Delete().Where(u => u.UserId == 1).ToDiagnostics(),
            My.Users().Delete().Where(u => u.UserId == 1).ToDiagnostics(),
            Ss.Users().Delete().Where(u => u.UserId == 1).ToDiagnostics(),
            sqlite: "DELETE FROM \"users\" WHERE \"UserId\" = 1",
            pg:     "DELETE FROM \"users\" WHERE \"UserId\" = 1",
            mysql:  "DELETE FROM `users` WHERE `UserId` = 1",
            ss:     "DELETE FROM [users] WHERE [UserId] = 1");
    }

    [Test]
    public void Delete_Where_GreaterThan()
    {
        AssertDialects(
            Lite.Users().Delete().Where(u => u.UserId > 100).ToDiagnostics(),
            Pg.Users().Delete().Where(u => u.UserId > 100).ToDiagnostics(),
            My.Users().Delete().Where(u => u.UserId > 100).ToDiagnostics(),
            Ss.Users().Delete().Where(u => u.UserId > 100).ToDiagnostics(),
            sqlite: "DELETE FROM \"users\" WHERE \"UserId\" > 100",
            pg:     "DELETE FROM \"users\" WHERE \"UserId\" > 100",
            mysql:  "DELETE FROM `users` WHERE `UserId` > 100",
            ss:     "DELETE FROM [users] WHERE [UserId] > 100");
    }

    #endregion

    #region Boolean

    [Test]
    public void Delete_Where_Boolean()
    {
        AssertDialects(
            Lite.Users().Delete().Where(u => u.IsActive).ToDiagnostics(),
            Pg.Users().Delete().Where(u => u.IsActive).ToDiagnostics(),
            My.Users().Delete().Where(u => u.IsActive).ToDiagnostics(),
            Ss.Users().Delete().Where(u => u.IsActive).ToDiagnostics(),
            sqlite: "DELETE FROM \"users\" WHERE \"IsActive\" = 1",
            pg:     "DELETE FROM \"users\" WHERE \"IsActive\" = TRUE",
            mysql:  "DELETE FROM `users` WHERE `IsActive` = 1",
            ss:     "DELETE FROM [users] WHERE [IsActive] = 1");
    }

    [Test]
    public void Delete_Where_NegatedBoolean()
    {
        AssertDialects(
            Lite.Users().Delete().Where(u => !u.IsActive).ToDiagnostics(),
            Pg.Users().Delete().Where(u => !u.IsActive).ToDiagnostics(),
            My.Users().Delete().Where(u => !u.IsActive).ToDiagnostics(),
            Ss.Users().Delete().Where(u => !u.IsActive).ToDiagnostics(),
            sqlite: "DELETE FROM \"users\" WHERE NOT (\"IsActive\")",
            pg:     "DELETE FROM \"users\" WHERE NOT (\"IsActive\")",
            mysql:  "DELETE FROM `users` WHERE NOT (`IsActive`)",
            ss:     "DELETE FROM [users] WHERE NOT ([IsActive])");
    }

    #endregion

    #region Multiple Where (AND)

    [Test]
    public void Delete_MultipleWhere()
    {
        AssertDialects(
            Lite.Users().Delete().Where(u => u.UserId == 1).Where(u => u.IsActive).ToDiagnostics(),
            Pg.Users().Delete().Where(u => u.UserId == 1).Where(u => u.IsActive).ToDiagnostics(),
            My.Users().Delete().Where(u => u.UserId == 1).Where(u => u.IsActive).ToDiagnostics(),
            Ss.Users().Delete().Where(u => u.UserId == 1).Where(u => u.IsActive).ToDiagnostics(),
            sqlite: "DELETE FROM \"users\" WHERE (\"UserId\" = 1) AND (\"IsActive\" = 1)",
            pg:     "DELETE FROM \"users\" WHERE (\"UserId\" = 1) AND (\"IsActive\" = TRUE)",
            mysql:  "DELETE FROM `users` WHERE (`UserId` = 1) AND (`IsActive` = 1)",
            ss:     "DELETE FROM [users] WHERE ([UserId] = 1) AND ([IsActive] = 1)");
    }

    #endregion

    #region Other Entities

    [Test]
    public void Delete_Order_Where()
    {
        AssertDialects(
            Lite.Orders().Delete().Where(o => o.OrderId == 42).ToDiagnostics(),
            Pg.Orders().Delete().Where(o => o.OrderId == 42).ToDiagnostics(),
            My.Orders().Delete().Where(o => o.OrderId == 42).ToDiagnostics(),
            Ss.Orders().Delete().Where(o => o.OrderId == 42).ToDiagnostics(),
            sqlite: "DELETE FROM \"orders\" WHERE \"OrderId\" = 42",
            pg:     "DELETE FROM \"orders\" WHERE \"OrderId\" = 42",
            mysql:  "DELETE FROM `orders` WHERE `OrderId` = 42",
            ss:     "DELETE FROM [orders] WHERE [OrderId] = 42");
    }

    #endregion
}
