using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


[TestFixture]
internal class CrossDialectUpdateTests
{
    #region Update SetAction + Where

    [Test]
    public async Task Update_SetAction_Where_Equality()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Update().Set(u => u.UserName = "NewName").Where(u => u.UserId == 1).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Users().Update().Set(u => u.UserName = "NewName").Where(u => u.UserId == 1).ToDiagnostics(),
            My.Users().Update().Set(u => u.UserName = "NewName").Where(u => u.UserId == 1).ToDiagnostics(),
            Ss.Users().Update().Set(u => u.UserName = "NewName").Where(u => u.UserId == 1).ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = 'NewName' WHERE \"UserId\" = 1",
            pg:     "UPDATE \"users\" SET \"UserName\" = 'NewName' WHERE \"UserId\" = 1",
            mysql:  "UPDATE `users` SET `UserName` = 'NewName' WHERE `UserId` = 1",
            ss:     "UPDATE [users] SET [UserName] = 'NewName' WHERE [UserId] = 1");

        var affected = await lite.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));
    }

    [Test]
    public async Task Update_SetAction_Where_Boolean()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Update().Set(u => u.IsActive = false).Where(u => u.IsActive).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Users().Update().Set(u => u.IsActive = false).Where(u => u.IsActive).ToDiagnostics(),
            My.Users().Update().Set(u => u.IsActive = false).Where(u => u.IsActive).ToDiagnostics(),
            Ss.Users().Update().Set(u => u.IsActive = false).Where(u => u.IsActive).ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"IsActive\" = 0 WHERE \"IsActive\" = 1",
            pg:     "UPDATE \"users\" SET \"IsActive\" = FALSE WHERE \"IsActive\" = TRUE",
            mysql:  "UPDATE `users` SET `IsActive` = 0 WHERE `IsActive` = 1",
            ss:     "UPDATE [users] SET [IsActive] = 0 WHERE [IsActive] = 1");

        // Seed has 2 active users (Alice, Bob)
        var affected = await lite.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(2));
    }

    #endregion

    #region UpdateSetPoco

    [Test]
    public async Task Update_SetPoco()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Update().Set(new User { UserName = "New", IsActive = false }).Where(u => u.UserId == 1).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Users().Update().Set(new Pg.User { UserName = "New", IsActive = false }).Where(u => u.UserId == 1).ToDiagnostics(),
            My.Users().Update().Set(new My.User { UserName = "New", IsActive = false }).Where(u => u.UserId == 1).ToDiagnostics(),
            Ss.Users().Update().Set(new Ss.User { UserName = "New", IsActive = false }).Where(u => u.UserId == 1).ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = @p0, \"IsActive\" = @p1 WHERE \"UserId\" = 1",
            pg:     "UPDATE \"users\" SET \"UserName\" = $1, \"IsActive\" = $2 WHERE \"UserId\" = 1",
            mysql:  "UPDATE `users` SET `UserName` = ?, `IsActive` = ? WHERE `UserId` = 1",
            ss:     "UPDATE [users] SET [UserName] = @p0, [IsActive] = @p1 WHERE [UserId] = 1");

        var affected = await lite.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));
    }

    #endregion

    #region Update with Multiple SetAction Calls

    [Test]
    public async Task Update_MultipleSetAction()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Update().Set(u => { u.UserName = "x"; u.IsActive = false; }).Where(u => u.UserId == 1).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Users().Update().Set(u => { u.UserName = "x"; u.IsActive = false; }).Where(u => u.UserId == 1).ToDiagnostics(),
            My.Users().Update().Set(u => { u.UserName = "x"; u.IsActive = false; }).Where(u => u.UserId == 1).ToDiagnostics(),
            Ss.Users().Update().Set(u => { u.UserName = "x"; u.IsActive = false; }).Where(u => u.UserId == 1).ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = 'x', \"IsActive\" = 0 WHERE \"UserId\" = 1",
            pg:     "UPDATE \"users\" SET \"UserName\" = 'x', \"IsActive\" = FALSE WHERE \"UserId\" = 1",
            mysql:  "UPDATE `users` SET `UserName` = 'x', `IsActive` = 0 WHERE `UserId` = 1",
            ss:     "UPDATE [users] SET [UserName] = 'x', [IsActive] = 0 WHERE [UserId] = 1");

        var affected = await lite.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));
    }

    #endregion

    #region UpdateWhere with Captured Parameter

    [Test]
    public async Task Update_Where_CapturedParameter()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var id = 5;
        var lite = Lite.Users().Update().Set(u => u.UserName = "x").Where(u => u.UserId == id).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Users().Update().Set(u => u.UserName = "x").Where(u => u.UserId == id).ToDiagnostics(),
            My.Users().Update().Set(u => u.UserName = "x").Where(u => u.UserId == id).ToDiagnostics(),
            Ss.Users().Update().Set(u => u.UserName = "x").Where(u => u.UserId == id).ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = 'x' WHERE \"UserId\" = @p0",
            pg:     "UPDATE \"users\" SET \"UserName\" = 'x' WHERE \"UserId\" = $1",
            mysql:  "UPDATE `users` SET `UserName` = 'x' WHERE `UserId` = ?",
            ss:     "UPDATE [users] SET [UserName] = 'x' WHERE [UserId] = @p0");

        // UserId 5 doesn't exist in seed data — 0 rows affected
        var affected = await lite.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(0));
    }

    #endregion

    #region Set(Action<T>) — Single Assignment

    [Test]
    public async Task Update_SetAction_SingleAssignment_Literal()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Update().Set(u => u.UserName = "NewName").Where(u => u.UserId == 1).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Users().Update().Set(u => u.UserName = "NewName").Where(u => u.UserId == 1).ToDiagnostics(),
            My.Users().Update().Set(u => u.UserName = "NewName").Where(u => u.UserId == 1).ToDiagnostics(),
            Ss.Users().Update().Set(u => u.UserName = "NewName").Where(u => u.UserId == 1).ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = 'NewName' WHERE \"UserId\" = 1",
            pg:     "UPDATE \"users\" SET \"UserName\" = 'NewName' WHERE \"UserId\" = 1",
            mysql:  "UPDATE `users` SET `UserName` = 'NewName' WHERE `UserId` = 1",
            ss:     "UPDATE [users] SET [UserName] = 'NewName' WHERE [UserId] = 1");

        var affected = await lite.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));
    }

    [Test]
    public async Task Update_SetAction_SingleAssignment_Boolean()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Update().Set(u => u.IsActive = false).Where(u => u.IsActive).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Users().Update().Set(u => u.IsActive = false).Where(u => u.IsActive).ToDiagnostics(),
            My.Users().Update().Set(u => u.IsActive = false).Where(u => u.IsActive).ToDiagnostics(),
            Ss.Users().Update().Set(u => u.IsActive = false).Where(u => u.IsActive).ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"IsActive\" = 0 WHERE \"IsActive\" = 1",
            pg:     "UPDATE \"users\" SET \"IsActive\" = FALSE WHERE \"IsActive\" = TRUE",
            mysql:  "UPDATE `users` SET `IsActive` = 0 WHERE `IsActive` = 1",
            ss:     "UPDATE [users] SET [IsActive] = 0 WHERE [IsActive] = 1");

        // 2 active users in seed
        var affected = await lite.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(2));
    }

    #endregion

    #region Set(Action<T>) — Multi-Assignment (Statement Lambda)

    [Test]
    public async Task Update_SetAction_MultiAssignment()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Update().Set(u => { u.UserName = "x"; u.IsActive = false; }).Where(u => u.UserId == 1).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Users().Update().Set(u => { u.UserName = "x"; u.IsActive = false; }).Where(u => u.UserId == 1).ToDiagnostics(),
            My.Users().Update().Set(u => { u.UserName = "x"; u.IsActive = false; }).Where(u => u.UserId == 1).ToDiagnostics(),
            Ss.Users().Update().Set(u => { u.UserName = "x"; u.IsActive = false; }).Where(u => u.UserId == 1).ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = 'x', \"IsActive\" = 0 WHERE \"UserId\" = 1",
            pg:     "UPDATE \"users\" SET \"UserName\" = 'x', \"IsActive\" = FALSE WHERE \"UserId\" = 1",
            mysql:  "UPDATE `users` SET `UserName` = 'x', `IsActive` = 0 WHERE `UserId` = 1",
            ss:     "UPDATE [users] SET [UserName] = 'x', [IsActive] = 0 WHERE [UserId] = 1");

        var affected = await lite.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));
    }

    #endregion

    #region Set(Action<T>) — Chained with existing Set overloads

    [Test]
    public async Task Update_SetAction_ChainedCalls()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Update().Set(u => u.UserName = "x").Set(u => u.IsActive = false).Where(u => u.UserId == 1).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Users().Update().Set(u => u.UserName = "x").Set(u => u.IsActive = false).Where(u => u.UserId == 1).ToDiagnostics(),
            My.Users().Update().Set(u => u.UserName = "x").Set(u => u.IsActive = false).Where(u => u.UserId == 1).ToDiagnostics(),
            Ss.Users().Update().Set(u => u.UserName = "x").Set(u => u.IsActive = false).Where(u => u.UserId == 1).ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = 'x', \"IsActive\" = 0 WHERE \"UserId\" = 1",
            pg:     "UPDATE \"users\" SET \"UserName\" = 'x', \"IsActive\" = FALSE WHERE \"UserId\" = 1",
            mysql:  "UPDATE `users` SET `UserName` = 'x', `IsActive` = 0 WHERE `UserId` = 1",
            ss:     "UPDATE [users] SET [UserName] = 'x', [IsActive] = 0 WHERE [UserId] = 1");

        var affected = await lite.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));
    }

    #endregion

    #region Set(Action<T>) — Captured Variables

    [Test]
    public async Task Update_SetAction_CapturedVariable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var name = "captured";
        var lite = Lite.Users().Update().Set(u => u.UserName = name).Where(u => u.UserId == 1).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Users().Update().Set(u => u.UserName = name).Where(u => u.UserId == 1).ToDiagnostics(),
            My.Users().Update().Set(u => u.UserName = name).Where(u => u.UserId == 1).ToDiagnostics(),
            Ss.Users().Update().Set(u => u.UserName = name).Where(u => u.UserId == 1).ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = @p0 WHERE \"UserId\" = 1",
            pg:     "UPDATE \"users\" SET \"UserName\" = $1 WHERE \"UserId\" = 1",
            mysql:  "UPDATE `users` SET `UserName` = ? WHERE `UserId` = 1",
            ss:     "UPDATE [users] SET [UserName] = @p0 WHERE [UserId] = 1");

        var affected = await lite.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));
    }

    [Test]
    public async Task Update_SetAction_MultiAssignment_WithCapturedVariable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var name = "captured";
        var lite = Lite.Users().Update().Set(u => { u.UserName = name; u.IsActive = false; }).Where(u => u.UserId == 1).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Users().Update().Set(u => { u.UserName = name; u.IsActive = false; }).Where(u => u.UserId == 1).ToDiagnostics(),
            My.Users().Update().Set(u => { u.UserName = name; u.IsActive = false; }).Where(u => u.UserId == 1).ToDiagnostics(),
            Ss.Users().Update().Set(u => { u.UserName = name; u.IsActive = false; }).Where(u => u.UserId == 1).ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = @p0, \"IsActive\" = 0 WHERE \"UserId\" = 1",
            pg:     "UPDATE \"users\" SET \"UserName\" = $1, \"IsActive\" = FALSE WHERE \"UserId\" = 1",
            mysql:  "UPDATE `users` SET `UserName` = ?, `IsActive` = 0 WHERE `UserId` = 1",
            ss:     "UPDATE [users] SET [UserName] = @p0, [IsActive] = 0 WHERE [UserId] = 1");

        var affected = await lite.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));
    }

    #endregion

    #region Set(Action<T>) — Type-Mapped Column

    [Test]
    public async Task Update_SetAction_TypeMappedColumn()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // SQL-only verification — accounts table not in harness schema
        QueryTestHarness.AssertDialects(
            Lite.Accounts().Update().Set(a => a.Balance = new Money(200m)).Where(a => a.AccountId == 1).ToDiagnostics(),
            Pg.Accounts().Update().Set(a => a.Balance = new Money(200m)).Where(a => a.AccountId == 1).ToDiagnostics(),
            My.Accounts().Update().Set(a => a.Balance = new Money(200m)).Where(a => a.AccountId == 1).ToDiagnostics(),
            Ss.Accounts().Update().Set(a => a.Balance = new Money(200m)).Where(a => a.AccountId == 1).ToDiagnostics(),
            sqlite: "UPDATE \"accounts\" SET \"Balance\" = @p0 WHERE \"AccountId\" = 1",
            pg:     "UPDATE \"accounts\" SET \"Balance\" = $1 WHERE \"AccountId\" = 1",
            mysql:  "UPDATE `accounts` SET `Balance` = ? WHERE `AccountId` = 1",
            ss:     "UPDATE [accounts] SET [Balance] = @p0 WHERE [AccountId] = 1");
    }

    #endregion
}
