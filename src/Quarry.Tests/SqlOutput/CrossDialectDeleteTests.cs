using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


[TestFixture]
internal class CrossDialectDeleteTests
{
    #region Basic Where

    [Test]
    public async Task Delete_Where_Equality()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Delete().Where(u => u.UserId == 1).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Users().Delete().Where(u => u.UserId == 1).ToDiagnostics(),
            My.Users().Delete().Where(u => u.UserId == 1).ToDiagnostics(),
            Ss.Users().Delete().Where(u => u.UserId == 1).ToDiagnostics(),
            sqlite: "DELETE FROM \"users\" WHERE \"UserId\" = 1",
            pg:     "DELETE FROM \"users\" WHERE \"UserId\" = 1",
            mysql:  "DELETE FROM `users` WHERE `UserId` = 1",
            ss:     "DELETE FROM [users] WHERE [UserId] = 1");

        var affected = await lite.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));
    }

    [Test]
    public async Task Delete_Where_GreaterThan()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Delete().Where(u => u.UserId > 100).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Users().Delete().Where(u => u.UserId > 100).ToDiagnostics(),
            My.Users().Delete().Where(u => u.UserId > 100).ToDiagnostics(),
            Ss.Users().Delete().Where(u => u.UserId > 100).ToDiagnostics(),
            sqlite: "DELETE FROM \"users\" WHERE \"UserId\" > 100",
            pg:     "DELETE FROM \"users\" WHERE \"UserId\" > 100",
            mysql:  "DELETE FROM `users` WHERE `UserId` > 100",
            ss:     "DELETE FROM [users] WHERE [UserId] > 100");

        var affected = await lite.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(0));
    }

    #endregion

    #region Boolean

    [Test]
    public async Task Delete_Where_Boolean()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Delete().Where(u => u.IsActive).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Users().Delete().Where(u => u.IsActive).ToDiagnostics(),
            My.Users().Delete().Where(u => u.IsActive).ToDiagnostics(),
            Ss.Users().Delete().Where(u => u.IsActive).ToDiagnostics(),
            sqlite: "DELETE FROM \"users\" WHERE \"IsActive\" = 1",
            pg:     "DELETE FROM \"users\" WHERE \"IsActive\" = TRUE",
            mysql:  "DELETE FROM `users` WHERE `IsActive` = 1",
            ss:     "DELETE FROM [users] WHERE [IsActive] = 1");

        var affected = await lite.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(2)); // Alice and Bob are active
    }

    [Test]
    public async Task Delete_Where_NegatedBoolean()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Delete().Where(u => !u.IsActive).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Users().Delete().Where(u => !u.IsActive).ToDiagnostics(),
            My.Users().Delete().Where(u => !u.IsActive).ToDiagnostics(),
            Ss.Users().Delete().Where(u => !u.IsActive).ToDiagnostics(),
            sqlite: "DELETE FROM \"users\" WHERE NOT (\"IsActive\")",
            pg:     "DELETE FROM \"users\" WHERE NOT (\"IsActive\")",
            mysql:  "DELETE FROM `users` WHERE NOT (`IsActive`)",
            ss:     "DELETE FROM [users] WHERE NOT ([IsActive])");

        var affected = await lite.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1)); // Only Charlie is inactive
    }

    #endregion

    #region Multiple Where (AND)

    [Test]
    public async Task Delete_MultipleWhere()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Delete().Where(u => u.UserId == 1).Where(u => u.IsActive).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Users().Delete().Where(u => u.UserId == 1).Where(u => u.IsActive).ToDiagnostics(),
            My.Users().Delete().Where(u => u.UserId == 1).Where(u => u.IsActive).ToDiagnostics(),
            Ss.Users().Delete().Where(u => u.UserId == 1).Where(u => u.IsActive).ToDiagnostics(),
            sqlite: "DELETE FROM \"users\" WHERE (\"UserId\" = 1) AND (\"IsActive\" = 1)",
            pg:     "DELETE FROM \"users\" WHERE (\"UserId\" = 1) AND (\"IsActive\" = TRUE)",
            mysql:  "DELETE FROM `users` WHERE (`UserId` = 1) AND (`IsActive` = 1)",
            ss:     "DELETE FROM [users] WHERE ([UserId] = 1) AND ([IsActive] = 1)");

        var affected = await lite.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1)); // Alice: UserId=1, IsActive=true
    }

    #endregion

    #region Other Entities

    [Test]
    public async Task Delete_Order_Where()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Orders().Delete().Where(o => o.OrderId == 42).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Orders().Delete().Where(o => o.OrderId == 42).ToDiagnostics(),
            My.Orders().Delete().Where(o => o.OrderId == 42).ToDiagnostics(),
            Ss.Orders().Delete().Where(o => o.OrderId == 42).ToDiagnostics(),
            sqlite: "DELETE FROM \"orders\" WHERE \"OrderId\" = 42",
            pg:     "DELETE FROM \"orders\" WHERE \"OrderId\" = 42",
            mysql:  "DELETE FROM `orders` WHERE `OrderId` = 42",
            ss:     "DELETE FROM [orders] WHERE [OrderId] = 42");

        var affected = await lite.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(0)); // No order with ID 42
    }

    #endregion
}
