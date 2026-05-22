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

        var lt = Lite.Users().Update().Set(u => u.UserName = "NewName").Where(u => u.UserId == 1).Prepare();
        var pg = Pg.Users().Update().Set(u => u.UserName = "NewName").Where(u => u.UserId == 1).Prepare();
        var my = My.Users().Update().Set(u => u.UserName = "NewName").Where(u => u.UserId == 1).Prepare();
        var ss = Ss.Users().Update().Set(u => u.UserName = "NewName").Where(u => u.UserId == 1).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = 'NewName' WHERE \"UserId\" = 1",
            pg:     "UPDATE \"users\" SET \"UserName\" = 'NewName' WHERE \"UserId\" = 1",
            mysql:  "UPDATE `users` SET `UserName` = 'NewName' WHERE `UserId` = 1",
            ss:     "UPDATE [users] SET [UserName] = 'NewName' WHERE [UserId] = 1");

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
    public async Task Update_SetAction_Where_Boolean()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Update().Set(u => u.IsActive = false).Where(u => u.IsActive).Prepare();
        var pg = Pg.Users().Update().Set(u => u.IsActive = false).Where(u => u.IsActive).Prepare();
        var my = My.Users().Update().Set(u => u.IsActive = false).Where(u => u.IsActive).Prepare();
        var ss = Ss.Users().Update().Set(u => u.IsActive = false).Where(u => u.IsActive).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"IsActive\" = 0 WHERE \"IsActive\" = 1",
            pg:     "UPDATE \"users\" SET \"IsActive\" = FALSE WHERE \"IsActive\" = TRUE",
            mysql:  "UPDATE `users` SET `IsActive` = 0 WHERE `IsActive` = 1",
            ss:     "UPDATE [users] SET [IsActive] = 0 WHERE [IsActive] = 1");

        // Seed has 2 active users (Alice, Bob)
        var affected = await lt.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(2));

        var pgAffected = await pg.ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(2));

        var myAffected = await my.ExecuteNonQueryAsync();
        Assert.That(myAffected, Is.EqualTo(2));

        var ssAffected = await ss.ExecuteNonQueryAsync();
        Assert.That(ssAffected, Is.EqualTo(2));
    }

    #endregion

    #region UpdateSetPoco

    [Test]
    public async Task Update_SetPoco()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Update().Set(new User { UserName = "New", IsActive = false }).Where(u => u.UserId == 1).Prepare();
        var pg = Pg.Users().Update().Set(new Pg.User { UserName = "New", IsActive = false }).Where(u => u.UserId == 1).Prepare();
        var my = My.Users().Update().Set(new My.User { UserName = "New", IsActive = false }).Where(u => u.UserId == 1).Prepare();
        var ss = Ss.Users().Update().Set(new Ss.User { UserName = "New", IsActive = false }).Where(u => u.UserId == 1).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = @p0, \"IsActive\" = @p1 WHERE \"UserId\" = 1",
            pg:     "UPDATE \"users\" SET \"UserName\" = $1, \"IsActive\" = $2 WHERE \"UserId\" = 1",
            mysql:  "UPDATE `users` SET `UserName` = ?, `IsActive` = ? WHERE `UserId` = 1",
            ss:     "UPDATE [users] SET [UserName] = @p0, [IsActive] = @p1 WHERE [UserId] = 1");

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

    #region Update with Multiple SetAction Calls

    [Test]
    public async Task Update_MultipleSetAction()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Update().Set(u => { u.UserName = "x"; u.IsActive = false; }).Where(u => u.UserId == 1).Prepare();
        var pg = Pg.Users().Update().Set(u => { u.UserName = "x"; u.IsActive = false; }).Where(u => u.UserId == 1).Prepare();
        var my = My.Users().Update().Set(u => { u.UserName = "x"; u.IsActive = false; }).Where(u => u.UserId == 1).Prepare();
        var ss = Ss.Users().Update().Set(u => { u.UserName = "x"; u.IsActive = false; }).Where(u => u.UserId == 1).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = 'x', \"IsActive\" = 0 WHERE \"UserId\" = 1",
            pg:     "UPDATE \"users\" SET \"UserName\" = 'x', \"IsActive\" = FALSE WHERE \"UserId\" = 1",
            mysql:  "UPDATE `users` SET `UserName` = 'x', `IsActive` = 0 WHERE `UserId` = 1",
            ss:     "UPDATE [users] SET [UserName] = 'x', [IsActive] = 0 WHERE [UserId] = 1");

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

    #region UpdateWhere with Captured Parameter

    [Test]
    public async Task Update_Where_CapturedParameter()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var id = 5;
        var lt = Lite.Users().Update().Set(u => u.UserName = "x").Where(u => u.UserId == id).Prepare();
        var pg = Pg.Users().Update().Set(u => u.UserName = "x").Where(u => u.UserId == id).Prepare();
        var my = My.Users().Update().Set(u => u.UserName = "x").Where(u => u.UserId == id).Prepare();
        var ss = Ss.Users().Update().Set(u => u.UserName = "x").Where(u => u.UserId == id).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = 'x' WHERE \"UserId\" = @p0",
            pg:     "UPDATE \"users\" SET \"UserName\" = 'x' WHERE \"UserId\" = $1",
            mysql:  "UPDATE `users` SET `UserName` = 'x' WHERE `UserId` = ?",
            ss:     "UPDATE [users] SET [UserName] = 'x' WHERE [UserId] = @p0");

        // UserId 5 doesn't exist in seed data — 0 rows affected
        var affected = await lt.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(0));

        var pgAffected = await pg.ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(0));

        var myAffected = await my.ExecuteNonQueryAsync();
        Assert.That(myAffected, Is.EqualTo(0));

        var ssAffected = await ss.ExecuteNonQueryAsync();
        Assert.That(ssAffected, Is.EqualTo(0));
    }

    #endregion

    #region Set(Action<T>) — Single Assignment

    [Test]
    public async Task Update_SetAction_SingleAssignment_Literal()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Update().Set(u => u.UserName = "NewName").Where(u => u.UserId == 1).Prepare();
        var pg = Pg.Users().Update().Set(u => u.UserName = "NewName").Where(u => u.UserId == 1).Prepare();
        var my = My.Users().Update().Set(u => u.UserName = "NewName").Where(u => u.UserId == 1).Prepare();
        var ss = Ss.Users().Update().Set(u => u.UserName = "NewName").Where(u => u.UserId == 1).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = 'NewName' WHERE \"UserId\" = 1",
            pg:     "UPDATE \"users\" SET \"UserName\" = 'NewName' WHERE \"UserId\" = 1",
            mysql:  "UPDATE `users` SET `UserName` = 'NewName' WHERE `UserId` = 1",
            ss:     "UPDATE [users] SET [UserName] = 'NewName' WHERE [UserId] = 1");

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
    public async Task Update_SetAction_SingleAssignment_Boolean()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Update().Set(u => u.IsActive = false).Where(u => u.IsActive).Prepare();
        var pg = Pg.Users().Update().Set(u => u.IsActive = false).Where(u => u.IsActive).Prepare();
        var my = My.Users().Update().Set(u => u.IsActive = false).Where(u => u.IsActive).Prepare();
        var ss = Ss.Users().Update().Set(u => u.IsActive = false).Where(u => u.IsActive).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"IsActive\" = 0 WHERE \"IsActive\" = 1",
            pg:     "UPDATE \"users\" SET \"IsActive\" = FALSE WHERE \"IsActive\" = TRUE",
            mysql:  "UPDATE `users` SET `IsActive` = 0 WHERE `IsActive` = 1",
            ss:     "UPDATE [users] SET [IsActive] = 0 WHERE [IsActive] = 1");

        // 2 active users in seed
        var affected = await lt.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(2));

        var pgAffected = await pg.ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(2));

        var myAffected = await my.ExecuteNonQueryAsync();
        Assert.That(myAffected, Is.EqualTo(2));

        var ssAffected = await ss.ExecuteNonQueryAsync();
        Assert.That(ssAffected, Is.EqualTo(2));
    }

    #endregion

    #region Set(Action<T>) — Multi-Assignment (Statement Lambda)

    [Test]
    public async Task Update_SetAction_MultiAssignment()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Update().Set(u => { u.UserName = "x"; u.IsActive = false; }).Where(u => u.UserId == 1).Prepare();
        var pg = Pg.Users().Update().Set(u => { u.UserName = "x"; u.IsActive = false; }).Where(u => u.UserId == 1).Prepare();
        var my = My.Users().Update().Set(u => { u.UserName = "x"; u.IsActive = false; }).Where(u => u.UserId == 1).Prepare();
        var ss = Ss.Users().Update().Set(u => { u.UserName = "x"; u.IsActive = false; }).Where(u => u.UserId == 1).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = 'x', \"IsActive\" = 0 WHERE \"UserId\" = 1",
            pg:     "UPDATE \"users\" SET \"UserName\" = 'x', \"IsActive\" = FALSE WHERE \"UserId\" = 1",
            mysql:  "UPDATE `users` SET `UserName` = 'x', `IsActive` = 0 WHERE `UserId` = 1",
            ss:     "UPDATE [users] SET [UserName] = 'x', [IsActive] = 0 WHERE [UserId] = 1");

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

    #region Set(Action<T>) — Chained with existing Set overloads

    [Test]
    public async Task Update_SetAction_ChainedCalls()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Update().Set(u => u.UserName = "x").Set(u => u.IsActive = false).Where(u => u.UserId == 1).Prepare();
        var pg = Pg.Users().Update().Set(u => u.UserName = "x").Set(u => u.IsActive = false).Where(u => u.UserId == 1).Prepare();
        var my = My.Users().Update().Set(u => u.UserName = "x").Set(u => u.IsActive = false).Where(u => u.UserId == 1).Prepare();
        var ss = Ss.Users().Update().Set(u => u.UserName = "x").Set(u => u.IsActive = false).Where(u => u.UserId == 1).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = 'x', \"IsActive\" = 0 WHERE \"UserId\" = 1",
            pg:     "UPDATE \"users\" SET \"UserName\" = 'x', \"IsActive\" = FALSE WHERE \"UserId\" = 1",
            mysql:  "UPDATE `users` SET `UserName` = 'x', `IsActive` = 0 WHERE `UserId` = 1",
            ss:     "UPDATE [users] SET [UserName] = 'x', [IsActive] = 0 WHERE [UserId] = 1");

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

    #region Set(Action<T>) — Captured Variables

    [Test]
    public async Task Update_SetAction_CapturedVariable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var name = "captured";
        var lt = Lite.Users().Update().Set(u => u.UserName = name).Where(u => u.UserId == 1).Prepare();
        var pg = Pg.Users().Update().Set(u => u.UserName = name).Where(u => u.UserId == 1).Prepare();
        var my = My.Users().Update().Set(u => u.UserName = name).Where(u => u.UserId == 1).Prepare();
        var ss = Ss.Users().Update().Set(u => u.UserName = name).Where(u => u.UserId == 1).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = @p0 WHERE \"UserId\" = 1",
            pg:     "UPDATE \"users\" SET \"UserName\" = $1 WHERE \"UserId\" = 1",
            mysql:  "UPDATE `users` SET `UserName` = ? WHERE `UserId` = 1",
            ss:     "UPDATE [users] SET [UserName] = @p0 WHERE [UserId] = 1");

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
    public async Task Update_SetAction_MultiAssignment_WithCapturedVariable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var name = "captured";
        var lt = Lite.Users().Update().Set(u => { u.UserName = name; u.IsActive = false; }).Where(u => u.UserId == 1).Prepare();
        var pg = Pg.Users().Update().Set(u => { u.UserName = name; u.IsActive = false; }).Where(u => u.UserId == 1).Prepare();
        var my = My.Users().Update().Set(u => { u.UserName = name; u.IsActive = false; }).Where(u => u.UserId == 1).Prepare();
        var ss = Ss.Users().Update().Set(u => { u.UserName = name; u.IsActive = false; }).Where(u => u.UserId == 1).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = @p0, \"IsActive\" = 0 WHERE \"UserId\" = 1",
            pg:     "UPDATE \"users\" SET \"UserName\" = $1, \"IsActive\" = FALSE WHERE \"UserId\" = 1",
            mysql:  "UPDATE `users` SET `UserName` = ?, `IsActive` = 0 WHERE `UserId` = 1",
            ss:     "UPDATE [users] SET [UserName] = @p0, [IsActive] = 0 WHERE [UserId] = 1");

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

    #region Set(Action<T>) — Property Chain and Computed Expressions

    [Test]
    public async Task Update_SetAction_PropertyChainCapture()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var source = new User { UserName = "fromDto" };

        // Cross-dialect SQL verification
        QueryTestHarness.AssertDialects(
            Lite.Users().Update().Set(u => u.UserName = source.UserName).Where(u => u.UserId == 1).ToDiagnostics(),
            Pg.Users().Update().Set(u => u.UserName = source.UserName).Where(u => u.UserId == 1).ToDiagnostics(),
            My.Users().Update().Set(u => u.UserName = source.UserName).Where(u => u.UserId == 1).ToDiagnostics(),
            Ss.Users().Update().Set(u => u.UserName = source.UserName).Where(u => u.UserId == 1).ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = @p0 WHERE \"UserId\" = 1",
            pg:     "UPDATE \"users\" SET \"UserName\" = $1 WHERE \"UserId\" = 1",
            mysql:  "UPDATE `users` SET `UserName` = ? WHERE `UserId` = 1",
            ss:     "UPDATE [users] SET [UserName] = @p0 WHERE [UserId] = 1");

        // Execute on SQLite and verify the property chain value was written
        var affected = await Lite.Users().Update()
            .Set(u => u.UserName = source.UserName)
            .Where(u => u.UserId == 1)
            .ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));

        var user = await Lite.Users()
            .Where(u => u.UserId == 1)
            .Select(u => u.UserName)
            .ExecuteFetchFirstAsync();
        Assert.That(user, Is.EqualTo("fromDto"));

        var pgSource = new Pg.User { UserName = "fromDto" };
        var pgAffected = await Pg.Users().Update()
            .Set(u => u.UserName = pgSource.UserName)
            .Where(u => u.UserId == 1)
            .ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(1));

        var pgUser = await Pg.Users()
            .Where(u => u.UserId == 1)
            .Select(u => u.UserName)
            .ExecuteFetchFirstAsync();
        Assert.That(pgUser, Is.EqualTo("fromDto"));
    }

    [Test]
    public async Task Update_SetAction_CapturedWithLiteral_ExecuteCorrectly()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, _, _) = t;

        var name = "updated";
        var affected = await Lite.Users().Update()
            .Set(u => { u.UserName = name; u.IsActive = false; })
            .Where(u => u.UserId == 2)
            .ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));

        // Verify captured var and literal were both written correctly
        var user = await Lite.Users()
            .Where(u => u.UserId == 2)
            .Select(u => (u.UserName, u.IsActive))
            .ExecuteFetchFirstAsync();
        Assert.That(user.UserName, Is.EqualTo("updated"));
        Assert.That(user.IsActive, Is.False);

        var pgAffected = await Pg.Users().Update()
            .Set(u => { u.UserName = name; u.IsActive = false; })
            .Where(u => u.UserId == 2)
            .ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(1));

        var pgUser = await Pg.Users()
            .Where(u => u.UserId == 2)
            .Select(u => (u.UserName, u.IsActive))
            .ExecuteFetchFirstAsync();
        Assert.That(pgUser.UserName, Is.EqualTo("updated"));
        Assert.That(pgUser.IsActive, Is.False);
    }

    [Test]
    public async Task Update_SetAction_MultipleCapturedVars_ExecuteCorrectly()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, _, _) = t;

        var name = "multiCap";
        var active = false;
        var affected = await Lite.Users().Update()
            .Set(u => { u.UserName = name; u.IsActive = active; })
            .Where(u => u.UserId == 2)
            .ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));

        var user = await Lite.Users()
            .Where(u => u.UserId == 2)
            .Select(u => (u.UserName, u.IsActive))
            .ExecuteFetchFirstAsync();
        Assert.That(user.UserName, Is.EqualTo("multiCap"));
        Assert.That(user.IsActive, Is.False);

        var pgAffected = await Pg.Users().Update()
            .Set(u => { u.UserName = name; u.IsActive = active; })
            .Where(u => u.UserId == 2)
            .ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(1));

        var pgUser = await Pg.Users()
            .Where(u => u.UserId == 2)
            .Select(u => (u.UserName, u.IsActive))
            .ExecuteFetchFirstAsync();
        Assert.That(pgUser.UserName, Is.EqualTo("multiCap"));
        Assert.That(pgUser.IsActive, Is.False);
    }

    #endregion

    #region Set(Action<T>) — Computed Expressions with Multiple Captured Variables

    [Test]
    public async Task Update_SetAction_BinaryExpression_MultipleCapturedVars()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var a = "Hello";
        var b = "World";

        // Binary expression with two captured locals → single parameter
        QueryTestHarness.AssertDialects(
            Lite.Users().Update().Set(u => u.UserName = a + b).Where(u => u.UserId == 1).ToDiagnostics(),
            Pg.Users().Update().Set(u => u.UserName = a + b).Where(u => u.UserId == 1).ToDiagnostics(),
            My.Users().Update().Set(u => u.UserName = a + b).Where(u => u.UserId == 1).ToDiagnostics(),
            Ss.Users().Update().Set(u => u.UserName = a + b).Where(u => u.UserId == 1).ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = @p0 WHERE \"UserId\" = 1",
            pg:     "UPDATE \"users\" SET \"UserName\" = $1 WHERE \"UserId\" = 1",
            mysql:  "UPDATE `users` SET `UserName` = ? WHERE `UserId` = 1",
            ss:     "UPDATE [users] SET [UserName] = @p0 WHERE [UserId] = 1");

        var affected = await Lite.Users().Update()
            .Set(u => u.UserName = a + b)
            .Where(u => u.UserId == 1)
            .ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));

        var user = await Lite.Users()
            .Where(u => u.UserId == 1)
            .Select(u => u.UserName)
            .ExecuteFetchFirstAsync();
        Assert.That(user, Is.EqualTo("HelloWorld"));

        var pgAffected = await Pg.Users().Update()
            .Set(u => u.UserName = a + b)
            .Where(u => u.UserId == 1)
            .ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(1));

        var pgUser = await Pg.Users()
            .Where(u => u.UserId == 1)
            .Select(u => u.UserName)
            .ExecuteFetchFirstAsync();
        Assert.That(pgUser, Is.EqualTo("HelloWorld"));
    }

    [Test]
    public async Task Update_SetAction_MethodCallOnCapture_ExecuteCorrectly()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, _, _) = t;

        var name = "hello";
        var affected = await Lite.Users().Update()
            .Set(u => u.UserName = name.ToUpper())
            .Where(u => u.UserId == 1)
            .ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));

        var user = await Lite.Users()
            .Where(u => u.UserId == 1)
            .Select(u => u.UserName)
            .ExecuteFetchFirstAsync();
        Assert.That(user, Is.EqualTo("HELLO"));

        var pgAffected = await Pg.Users().Update()
            .Set(u => u.UserName = name.ToUpper())
            .Where(u => u.UserId == 1)
            .ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(1));

        var pgUser = await Pg.Users()
            .Where(u => u.UserId == 1)
            .Select(u => u.UserName)
            .ExecuteFetchFirstAsync();
        Assert.That(pgUser, Is.EqualTo("HELLO"));
    }

    [Test]
    public async Task Update_SetAction_TernaryExpression_ExecuteCorrectly()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var useAlias = true;

        QueryTestHarness.AssertDialects(
            Lite.Users().Update().Set(u => u.UserName = useAlias ? "Alias" : "Real").Where(u => u.UserId == 1).ToDiagnostics(),
            Pg.Users().Update().Set(u => u.UserName = useAlias ? "Alias" : "Real").Where(u => u.UserId == 1).ToDiagnostics(),
            My.Users().Update().Set(u => u.UserName = useAlias ? "Alias" : "Real").Where(u => u.UserId == 1).ToDiagnostics(),
            Ss.Users().Update().Set(u => u.UserName = useAlias ? "Alias" : "Real").Where(u => u.UserId == 1).ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = @p0 WHERE \"UserId\" = 1",
            pg:     "UPDATE \"users\" SET \"UserName\" = $1 WHERE \"UserId\" = 1",
            mysql:  "UPDATE `users` SET `UserName` = ? WHERE `UserId` = 1",
            ss:     "UPDATE [users] SET [UserName] = @p0 WHERE [UserId] = 1");

        var affected = await Lite.Users().Update()
            .Set(u => u.UserName = useAlias ? "Alias" : "Real")
            .Where(u => u.UserId == 1)
            .ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));

        var user = await Lite.Users()
            .Where(u => u.UserId == 1)
            .Select(u => u.UserName)
            .ExecuteFetchFirstAsync();
        Assert.That(user, Is.EqualTo("Alias"));

        var pgAffected = await Pg.Users().Update()
            .Set(u => u.UserName = useAlias ? "Alias" : "Real")
            .Where(u => u.UserId == 1)
            .ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(1));

        var pgUser = await Pg.Users()
            .Where(u => u.UserId == 1)
            .Select(u => u.UserName)
            .ExecuteFetchFirstAsync();
        Assert.That(pgUser, Is.EqualTo("Alias"));
    }

    [Test]
    public async Task Update_SetAction_BlockLambda_ComputedAndSimpleCapture()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, _, _) = t;

        var first = "Jane";
        var last = "Doe";
        var email = "jane@test.com";
        var affected = await Lite.Users().Update()
            .Set(u => { u.UserName = first + last; u.Email = email; })
            .Where(u => u.UserId == 1)
            .ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));

        var user = await Lite.Users()
            .Where(u => u.UserId == 1)
            .Select(u => (u.UserName, u.Email))
            .ExecuteFetchFirstAsync();
        Assert.That(user.UserName, Is.EqualTo("JaneDoe"));
        Assert.That(user.Email, Is.EqualTo("jane@test.com"));

        var pgAffected = await Pg.Users().Update()
            .Set(u => { u.UserName = first + last; u.Email = email; })
            .Where(u => u.UserId == 1)
            .ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(1));

        var pgUser = await Pg.Users()
            .Where(u => u.UserId == 1)
            .Select(u => (u.UserName, u.Email))
            .ExecuteFetchFirstAsync();
        Assert.That(pgUser.UserName, Is.EqualTo("JaneDoe"));
        Assert.That(pgUser.Email, Is.EqualTo("jane@test.com"));
    }

    [Test]
    public async Task Update_SetAction_BlockLambda_InlinedConstantAndComputed()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, _, _) = t;

        var first = "Updated";
        var last = "User";
        var affected = await Lite.Users().Update()
            .Set(u => { u.UserName = first + last; u.IsActive = false; })
            .Where(u => u.UserId == 1)
            .ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));

        var user = await Lite.Users()
            .Where(u => u.UserId == 1)
            .Select(u => (u.UserName, u.IsActive))
            .ExecuteFetchFirstAsync();
        Assert.That(user.UserName, Is.EqualTo("UpdatedUser"));
        Assert.That(user.IsActive, Is.False);

        var pgAffected = await Pg.Users().Update()
            .Set(u => { u.UserName = first + last; u.IsActive = false; })
            .Where(u => u.UserId == 1)
            .ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(1));

        var pgUser = await Pg.Users()
            .Where(u => u.UserId == 1)
            .Select(u => (u.UserName, u.IsActive))
            .ExecuteFetchFirstAsync();
        Assert.That(pgUser.UserName, Is.EqualTo("UpdatedUser"));
        Assert.That(pgUser.IsActive, Is.False);
    }

    [Test]
    public async Task Update_SetAction_ComputedSet_CapturedWhere()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var prefix = "Admin_";
        var suffix = "User";
        var targetId = 2;

        // Computed expression in Set + captured variable in Where → two parameters
        QueryTestHarness.AssertDialects(
            Lite.Users().Update().Set(u => u.UserName = prefix + suffix).Where(u => u.UserId == targetId).ToDiagnostics(),
            Pg.Users().Update().Set(u => u.UserName = prefix + suffix).Where(u => u.UserId == targetId).ToDiagnostics(),
            My.Users().Update().Set(u => u.UserName = prefix + suffix).Where(u => u.UserId == targetId).ToDiagnostics(),
            Ss.Users().Update().Set(u => u.UserName = prefix + suffix).Where(u => u.UserId == targetId).ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = @p0 WHERE \"UserId\" = @p1",
            pg:     "UPDATE \"users\" SET \"UserName\" = $1 WHERE \"UserId\" = $2",
            mysql:  "UPDATE `users` SET `UserName` = ? WHERE `UserId` = ?",
            ss:     "UPDATE [users] SET [UserName] = @p0 WHERE [UserId] = @p1");

        var affected = await Lite.Users().Update()
            .Set(u => u.UserName = prefix + suffix)
            .Where(u => u.UserId == targetId)
            .ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));

        var user = await Lite.Users()
            .Where(u => u.UserId == 2)
            .Select(u => u.UserName)
            .ExecuteFetchFirstAsync();
        Assert.That(user, Is.EqualTo("Admin_User"));

        var pgAffected = await Pg.Users().Update()
            .Set(u => u.UserName = prefix + suffix)
            .Where(u => u.UserId == targetId)
            .ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(1));

        var pgUser = await Pg.Users()
            .Where(u => u.UserId == 2)
            .Select(u => u.UserName)
            .ExecuteFetchFirstAsync();
        Assert.That(pgUser, Is.EqualTo("Admin_User"));
    }

    [Test]
    public async Task Update_SetAction_ChainedSet_FirstComputed()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, _, _) = t;

        var first = "Chain";
        var last = "Test";
        var affected = await Lite.Users().Update()
            .Set(u => u.UserName = first + last)
            .Set(u => u.IsActive = false)
            .Where(u => u.UserId == 1)
            .ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));

        var user = await Lite.Users()
            .Where(u => u.UserId == 1)
            .Select(u => (u.UserName, u.IsActive))
            .ExecuteFetchFirstAsync();
        Assert.That(user.UserName, Is.EqualTo("ChainTest"));
        Assert.That(user.IsActive, Is.False);

        var pgAffected = await Pg.Users().Update()
            .Set(u => u.UserName = first + last)
            .Set(u => u.IsActive = false)
            .Where(u => u.UserId == 1)
            .ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(1));

        var pgUser = await Pg.Users()
            .Where(u => u.UserId == 1)
            .Select(u => (u.UserName, u.IsActive))
            .ExecuteFetchFirstAsync();
        Assert.That(pgUser.UserName, Is.EqualTo("ChainTest"));
        Assert.That(pgUser.IsActive, Is.False);
    }

    [Test]
    public async Task Update_SetAction_DecimalArithmetic_ExecuteCorrectly()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, _, _) = t;

        var qty = 5;
        var price = 99.50m;
        var affected = await Lite.OrderItems().Update()
            .Set(o => o.LineTotal = qty * price)
            .Where(o => o.OrderItemId == 1)
            .ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));

        var lineTotal = await Lite.OrderItems()
            .Where(o => o.OrderItemId == 1)
            .Select(o => o.LineTotal)
            .ExecuteFetchFirstAsync();
        Assert.That(lineTotal, Is.EqualTo(497.50m));

        var pgAffected = await Pg.OrderItems().Update()
            .Set(o => o.LineTotal = qty * price)
            .Where(o => o.OrderItemId == 1)
            .ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(1));

        var pgLineTotal = await Pg.OrderItems()
            .Where(o => o.OrderItemId == 1)
            .Select(o => o.LineTotal)
            .ExecuteFetchFirstAsync();
        Assert.That(pgLineTotal, Is.EqualTo(497.50m));
    }

    [Test]
    public async Task Update_SetAction_MethodChainOnCapture_ExecuteCorrectly()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, _, _) = t;

        var name = "  trimmed  ";
        var affected = await Lite.Users().Update()
            .Set(u => u.UserName = name.Trim())
            .Where(u => u.UserId == 1)
            .ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));

        var user = await Lite.Users()
            .Where(u => u.UserId == 1)
            .Select(u => u.UserName)
            .ExecuteFetchFirstAsync();
        Assert.That(user, Is.EqualTo("trimmed"));

        var pgAffected = await Pg.Users().Update()
            .Set(u => u.UserName = name.Trim())
            .Where(u => u.UserId == 1)
            .ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(1));

        var pgUser = await Pg.Users()
            .Where(u => u.UserId == 1)
            .Select(u => u.UserName)
            .ExecuteFetchFirstAsync();
        Assert.That(pgUser, Is.EqualTo("trimmed"));
    }

    [Test]
    public async Task Update_SetAction_NullCoalescing_ExecuteCorrectly()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, _, _) = t;

        string? maybeNull = null;
        var affected = await Lite.Users().Update()
            .Set(u => u.UserName = maybeNull ?? "fallback")
            .Where(u => u.UserId == 1)
            .ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));

        var user = await Lite.Users()
            .Where(u => u.UserId == 1)
            .Select(u => u.UserName)
            .ExecuteFetchFirstAsync();
        Assert.That(user, Is.EqualTo("fallback"));

        var pgAffected = await Pg.Users().Update()
            .Set(u => u.UserName = maybeNull ?? "fallback")
            .Where(u => u.UserId == 1)
            .ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(1));

        var pgUser = await Pg.Users()
            .Where(u => u.UserId == 1)
            .Select(u => u.UserName)
            .ExecuteFetchFirstAsync();
        Assert.That(pgUser, Is.EqualTo("fallback"));
    }

    #endregion

    #region Set(Action<T>) — Column Expression in RHS

    [Test]
    public async Task Update_SetAction_PureColumnExpression()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.OrderItems().Update().Set(o => o.LineTotal = o.Quantity * o.UnitPrice).Where(o => o.OrderItemId == 1).Prepare();
        var pg = Pg.OrderItems().Update().Set(o => o.LineTotal = o.Quantity * o.UnitPrice).Where(o => o.OrderItemId == 1).Prepare();
        var my = My.OrderItems().Update().Set(o => o.LineTotal = o.Quantity * o.UnitPrice).Where(o => o.OrderItemId == 1).Prepare();
        var ss = Ss.OrderItems().Update().Set(o => o.LineTotal = o.Quantity * o.UnitPrice).Where(o => o.OrderItemId == 1).Prepare();

        // Pure column expression: no captured variables, both operands are entity columns
        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "UPDATE \"order_items\" SET \"LineTotal\" = (\"Quantity\" * \"UnitPrice\") WHERE \"OrderItemId\" = 1",
            pg:     "UPDATE \"order_items\" SET \"LineTotal\" = (\"Quantity\" * \"UnitPrice\") WHERE \"OrderItemId\" = 1",
            mysql:  "UPDATE `order_items` SET `LineTotal` = (`Quantity` * `UnitPrice`) WHERE `OrderItemId` = 1",
            ss:     "UPDATE [order_items] SET [LineTotal] = ([Quantity] * [UnitPrice]) WHERE [OrderItemId] = 1");

        // Execute on SQLite: item 1 has Quantity=2, UnitPrice=125.00 → LineTotal should be 250.00
        var affected = await lt.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));

        var lineTotal = await Lite.OrderItems()
            .Where(o => o.OrderItemId == 1)
            .Select(o => o.LineTotal)
            .ExecuteFetchFirstAsync();
        Assert.That(lineTotal, Is.EqualTo(250.00m));

        var pgAffected = await pg.ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(1));

        var myAffected = await my.ExecuteNonQueryAsync();
        Assert.That(myAffected, Is.EqualTo(1));

        var pgLineTotal = await Pg.OrderItems()
            .Where(o => o.OrderItemId == 1)
            .Select(o => o.LineTotal)
            .ExecuteFetchFirstAsync();
        Assert.That(pgLineTotal, Is.EqualTo(250.00m));

        var ssAffected = await ss.ExecuteNonQueryAsync();
        Assert.That(ssAffected, Is.EqualTo(1));

        var ssLineTotal = await Ss.OrderItems()
            .Where(o => o.OrderItemId == 1)
            .Select(o => o.LineTotal)
            .ExecuteFetchFirstAsync();
        Assert.That(ssLineTotal, Is.EqualTo(250.00m));
    }

    [Test]
    public async Task Update_SetAction_ColumnPlusCapturedVariable()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var bonus = 10.00m;

        var lt = Lite.OrderItems().Update().Set(o => o.LineTotal = o.UnitPrice + bonus).Where(o => o.OrderItemId == 2).Prepare();
        var pg = Pg.OrderItems().Update().Set(o => o.LineTotal = o.UnitPrice + bonus).Where(o => o.OrderItemId == 2).Prepare();
        var my = My.OrderItems().Update().Set(o => o.LineTotal = o.UnitPrice + bonus).Where(o => o.OrderItemId == 2).Prepare();
        var ss = Ss.OrderItems().Update().Set(o => o.LineTotal = o.UnitPrice + bonus).Where(o => o.OrderItemId == 2).Prepare();

        // Mixed: column reference + captured variable → column ref in SQL + one parameter
        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "UPDATE \"order_items\" SET \"LineTotal\" = (\"UnitPrice\" + @p0) WHERE \"OrderItemId\" = 2",
            pg:     "UPDATE \"order_items\" SET \"LineTotal\" = (\"UnitPrice\" + $1) WHERE \"OrderItemId\" = 2",
            mysql:  "UPDATE `order_items` SET `LineTotal` = (`UnitPrice` + ?) WHERE `OrderItemId` = 2",
            ss:     "UPDATE [order_items] SET [LineTotal] = ([UnitPrice] + @p0) WHERE [OrderItemId] = 2");

        // Execute on SQLite: item 2 has UnitPrice=75.50, bonus=10 → LineTotal should be 85.50
        var affected = await lt.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));

        var lineTotal = await Lite.OrderItems()
            .Where(o => o.OrderItemId == 2)
            .Select(o => o.LineTotal)
            .ExecuteFetchFirstAsync();
        Assert.That(lineTotal, Is.EqualTo(85.50m));

        var pgAffected = await pg.ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(1));

        var myAffected = await my.ExecuteNonQueryAsync();
        Assert.That(myAffected, Is.EqualTo(1));

        var pgLineTotal = await Pg.OrderItems()
            .Where(o => o.OrderItemId == 2)
            .Select(o => o.LineTotal)
            .ExecuteFetchFirstAsync();
        Assert.That(pgLineTotal, Is.EqualTo(85.50m));

        var ssAffected = await ss.ExecuteNonQueryAsync();
        Assert.That(ssAffected, Is.EqualTo(1));

        var ssLineTotal = await Ss.OrderItems()
            .Where(o => o.OrderItemId == 2)
            .Select(o => o.LineTotal)
            .ExecuteFetchFirstAsync();
        Assert.That(ssLineTotal, Is.EqualTo(85.50m));
    }

    [Test]
    public async Task Update_SetAction_ColumnSubtractionPlusCaptured()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var offset = 1000.00m;

        var lt = Lite.OrderItems().Update().Set(o => o.LineTotal = (o.LineTotal - o.UnitPrice) + offset).Where(o => o.OrderItemId == 1).Prepare();
        var pg = Pg.OrderItems().Update().Set(o => o.LineTotal = (o.LineTotal - o.UnitPrice) + offset).Where(o => o.OrderItemId == 1).Prepare();
        var my = My.OrderItems().Update().Set(o => o.LineTotal = (o.LineTotal - o.UnitPrice) + offset).Where(o => o.OrderItemId == 1).Prepare();
        var ss = Ss.OrderItems().Update().Set(o => o.LineTotal = (o.LineTotal - o.UnitPrice) + offset).Where(o => o.OrderItemId == 1).Prepare();

        // Mirrors TimeSheet pattern: (column - column) + captured
        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "UPDATE \"order_items\" SET \"LineTotal\" = ((\"LineTotal\" - \"UnitPrice\") + @p0) WHERE \"OrderItemId\" = 1",
            pg:     "UPDATE \"order_items\" SET \"LineTotal\" = ((\"LineTotal\" - \"UnitPrice\") + $1) WHERE \"OrderItemId\" = 1",
            mysql:  "UPDATE `order_items` SET `LineTotal` = ((`LineTotal` - `UnitPrice`) + ?) WHERE `OrderItemId` = 1",
            ss:     "UPDATE [order_items] SET [LineTotal] = (([LineTotal] - [UnitPrice]) + @p0) WHERE [OrderItemId] = 1");

        // Execute on SQLite: item 1 has LineTotal=250, UnitPrice=125, offset=1000
        // → (250 - 125) + 1000 = 1125
        var affected = await lt.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));

        var lineTotal = await Lite.OrderItems()
            .Where(o => o.OrderItemId == 1)
            .Select(o => o.LineTotal)
            .ExecuteFetchFirstAsync();
        Assert.That(lineTotal, Is.EqualTo(1125.00m));

        var pgAffected = await pg.ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(1));

        var myAffected = await my.ExecuteNonQueryAsync();
        Assert.That(myAffected, Is.EqualTo(1));

        var pgLineTotal = await Pg.OrderItems()
            .Where(o => o.OrderItemId == 1)
            .Select(o => o.LineTotal)
            .ExecuteFetchFirstAsync();
        Assert.That(pgLineTotal, Is.EqualTo(1125.00m));

        var ssAffected = await ss.ExecuteNonQueryAsync();
        Assert.That(ssAffected, Is.EqualTo(1));

        var ssLineTotal = await Ss.OrderItems()
            .Where(o => o.OrderItemId == 1)
            .Select(o => o.LineTotal)
            .ExecuteFetchFirstAsync();
        Assert.That(ssLineTotal, Is.EqualTo(1125.00m));
    }

    [Test]
    public async Task Update_SetAction_MultiAssignment_ColumnExprAndInlined()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.OrderItems().Update().Set(o => { o.LineTotal = o.Quantity * o.UnitPrice; o.Quantity = 0; }).Where(o => o.OrderItemId == 3).Prepare();
        var pg = Pg.OrderItems().Update().Set(o => { o.LineTotal = o.Quantity * o.UnitPrice; o.Quantity = 0; }).Where(o => o.OrderItemId == 3).Prepare();
        var my = My.OrderItems().Update().Set(o => { o.LineTotal = o.Quantity * o.UnitPrice; o.Quantity = 0; }).Where(o => o.OrderItemId == 3).Prepare();
        var ss = Ss.OrderItems().Update().Set(o => { o.LineTotal = o.Quantity * o.UnitPrice; o.Quantity = 0; }).Where(o => o.OrderItemId == 3).Prepare();

        // Block lambda: first assignment is column expression, second is inlined constant
        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "UPDATE \"order_items\" SET \"LineTotal\" = (\"Quantity\" * \"UnitPrice\"), \"Quantity\" = 0 WHERE \"OrderItemId\" = 3",
            pg:     "UPDATE \"order_items\" SET \"LineTotal\" = (\"Quantity\" * \"UnitPrice\"), \"Quantity\" = 0 WHERE \"OrderItemId\" = 3",
            mysql:  "UPDATE `order_items` SET `LineTotal` = (`Quantity` * `UnitPrice`), `Quantity` = 0 WHERE `OrderItemId` = 3",
            ss:     "UPDATE [order_items] SET [LineTotal] = ([Quantity] * [UnitPrice]), [Quantity] = 0 WHERE [OrderItemId] = 3");

        // Execute on SQLite: item 3 has Quantity=3, UnitPrice=50 → LineTotal=150, Quantity→0
        var affected = await lt.ExecuteNonQueryAsync();
        Assert.That(affected, Is.EqualTo(1));

        var result = await Lite.OrderItems()
            .Where(o => o.OrderItemId == 3)
            .Select(o => (o.LineTotal, o.Quantity))
            .ExecuteFetchFirstAsync();
        Assert.That(result.LineTotal, Is.EqualTo(150.00m));
        Assert.That(result.Quantity, Is.EqualTo(0));

        var pgAffected = await pg.ExecuteNonQueryAsync();
        Assert.That(pgAffected, Is.EqualTo(1));

        var myAffected = await my.ExecuteNonQueryAsync();
        Assert.That(myAffected, Is.EqualTo(1));

        var pgResult = await Pg.OrderItems()
            .Where(o => o.OrderItemId == 3)
            .Select(o => (o.LineTotal, o.Quantity))
            .ExecuteFetchFirstAsync();
        Assert.That(pgResult.LineTotal, Is.EqualTo(150.00m));
        Assert.That(pgResult.Quantity, Is.EqualTo(0));

        var ssAffected = await ss.ExecuteNonQueryAsync();
        Assert.That(ssAffected, Is.EqualTo(1));

        var ssResult = await Ss.OrderItems()
            .Where(o => o.OrderItemId == 3)
            .Select(o => (o.LineTotal, o.Quantity))
            .ExecuteFetchFirstAsync();
        Assert.That(ssResult.LineTotal, Is.EqualTo(150.00m));
        Assert.That(ssResult.Quantity, Is.EqualTo(0));
    }

    #endregion

    #region Set(Action<T>) — Type-Mapped Column

    [Test]
    public async Task Update_SetAction_TypeMappedColumn()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // SQL-only verification — accounts table not in harness schema
        var lt = Lite.Accounts().Update().Set(a => a.Balance = new Money(200m)).Where(a => a.AccountId == 1).Prepare();
        var pg = Pg.Accounts().Update().Set(a => a.Balance = new Money(200m)).Where(a => a.AccountId == 1).Prepare();
        var my = My.Accounts().Update().Set(a => a.Balance = new Money(200m)).Where(a => a.AccountId == 1).Prepare();
        var ss = Ss.Accounts().Update().Set(a => a.Balance = new Money(200m)).Where(a => a.AccountId == 1).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "UPDATE \"accounts\" SET \"Balance\" = @p0 WHERE \"AccountId\" = 1",
            pg:     "UPDATE \"accounts\" SET \"Balance\" = $1 WHERE \"AccountId\" = 1",
            mysql:  "UPDATE `accounts` SET `Balance` = ? WHERE `AccountId` = 1",
            ss:     "UPDATE [accounts] SET [Balance] = @p0 WHERE [AccountId] = 1");
    }

    #endregion

    #region UpdateSetPatch — value form

    [Test]
    public async Task Update_SetPatch_SingleColumn()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var ltPatch = new User.Patch { UserName = "Renamed" };
        var pgPatch = new Pg.User.Patch { UserName = "Renamed" };
        var myPatch = new My.User.Patch { UserName = "Renamed" };
        var ssPatch = new Ss.User.Patch { UserName = "Renamed" };

        var lt = Lite.Users().Update().Set(ltPatch).Where(u => u.UserId == 1).Prepare();
        var pg = Pg.Users().Update().Set(pgPatch).Where(u => u.UserId == 1).Prepare();
        var my = My.Users().Update().Set(myPatch).Where(u => u.UserId == 1).Prepare();
        var ss = Ss.Users().Update().Set(ssPatch).Where(u => u.UserId == 1).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = @p0 WHERE \"UserId\" = 1",
            pg:     "UPDATE \"users\" SET \"UserName\" = $1 WHERE \"UserId\" = 1",
            mysql:  "UPDATE `users` SET `UserName` = ? WHERE `UserId` = 1",
            ss:     "UPDATE [users] SET [UserName] = @p0 WHERE [UserId] = 1");

        Assert.That(await lt.ExecuteNonQueryAsync(), Is.EqualTo(1));
        Assert.That(await pg.ExecuteNonQueryAsync(), Is.EqualTo(1));
        Assert.That(await my.ExecuteNonQueryAsync(), Is.EqualTo(1));
        Assert.That(await ss.ExecuteNonQueryAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task Update_SetPatch_TwoColumns()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var ltPatch = new User.Patch { UserName = "Renamed", IsActive = false };
        var pgPatch = new Pg.User.Patch { UserName = "Renamed", IsActive = false };
        var myPatch = new My.User.Patch { UserName = "Renamed", IsActive = false };
        var ssPatch = new Ss.User.Patch { UserName = "Renamed", IsActive = false };

        var lt = Lite.Users().Update().Set(ltPatch).Where(u => u.UserId == 1).Prepare();
        var pg = Pg.Users().Update().Set(pgPatch).Where(u => u.UserId == 1).Prepare();
        var my = My.Users().Update().Set(myPatch).Where(u => u.UserId == 1).Prepare();
        var ss = Ss.Users().Update().Set(ssPatch).Where(u => u.UserId == 1).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = @p0, \"IsActive\" = @p1 WHERE \"UserId\" = 1",
            pg:     "UPDATE \"users\" SET \"UserName\" = $1, \"IsActive\" = $2 WHERE \"UserId\" = 1",
            mysql:  "UPDATE `users` SET `UserName` = ?, `IsActive` = ? WHERE `UserId` = 1",
            ss:     "UPDATE [users] SET [UserName] = @p0, [IsActive] = @p1 WHERE [UserId] = 1");

        Assert.That(await lt.ExecuteNonQueryAsync(), Is.EqualTo(1));
        Assert.That(await pg.ExecuteNonQueryAsync(), Is.EqualTo(1));
        Assert.That(await my.ExecuteNonQueryAsync(), Is.EqualTo(1));
        Assert.That(await ss.ExecuteNonQueryAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task Update_SetPatch_ThreeColumns()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var ltPatch = new User.Patch { UserName = "Renamed", Email = "x@example.com", IsActive = false };
        var pgPatch = new Pg.User.Patch { UserName = "Renamed", Email = "x@example.com", IsActive = false };
        var myPatch = new My.User.Patch { UserName = "Renamed", Email = "x@example.com", IsActive = false };
        var ssPatch = new Ss.User.Patch { UserName = "Renamed", Email = "x@example.com", IsActive = false };

        var lt = Lite.Users().Update().Set(ltPatch).Where(u => u.UserId == 1).Prepare();
        var pg = Pg.Users().Update().Set(pgPatch).Where(u => u.UserId == 1).Prepare();
        var my = My.Users().Update().Set(myPatch).Where(u => u.UserId == 1).Prepare();
        var ss = Ss.Users().Update().Set(ssPatch).Where(u => u.UserId == 1).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = @p0, \"Email\" = @p1, \"IsActive\" = @p2 WHERE \"UserId\" = 1",
            pg:     "UPDATE \"users\" SET \"UserName\" = $1, \"Email\" = $2, \"IsActive\" = $3 WHERE \"UserId\" = 1",
            mysql:  "UPDATE `users` SET `UserName` = ?, `Email` = ?, `IsActive` = ? WHERE `UserId` = 1",
            ss:     "UPDATE [users] SET [UserName] = @p0, [Email] = @p1, [IsActive] = @p2 WHERE [UserId] = 1");

        Assert.That(await lt.ExecuteNonQueryAsync(), Is.EqualTo(1));
        Assert.That(await pg.ExecuteNonQueryAsync(), Is.EqualTo(1));
        Assert.That(await my.ExecuteNonQueryAsync(), Is.EqualTo(1));
        Assert.That(await ss.ExecuteNonQueryAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task Update_SetPatch_CapturedWhereParam_NamesShiftPastSetParams()
    {
        // WHERE param uses a captured variable so it consumes a real placeholder.
        // SET binds @p0/@p1; WHERE must land on @p2 — verifies the __setShift wiring
        // through both the inline SQL builder and the post-build name shift.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var id = 1;
        var ltPatch = new User.Patch { UserName = "Renamed", IsActive = false };
        var pgPatch = new Pg.User.Patch { UserName = "Renamed", IsActive = false };
        var myPatch = new My.User.Patch { UserName = "Renamed", IsActive = false };
        var ssPatch = new Ss.User.Patch { UserName = "Renamed", IsActive = false };

        var lt = Lite.Users().Update().Set(ltPatch).Where(u => u.UserId == id).Prepare();
        var pg = Pg.Users().Update().Set(pgPatch).Where(u => u.UserId == id).Prepare();
        var my = My.Users().Update().Set(myPatch).Where(u => u.UserId == id).Prepare();
        var ss = Ss.Users().Update().Set(ssPatch).Where(u => u.UserId == id).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = @p0, \"IsActive\" = @p1 WHERE \"UserId\" = @p2",
            pg:     "UPDATE \"users\" SET \"UserName\" = $1, \"IsActive\" = $2 WHERE \"UserId\" = $3",
            mysql:  "UPDATE `users` SET `UserName` = ?, `IsActive` = ? WHERE `UserId` = ?",
            ss:     "UPDATE [users] SET [UserName] = @p0, [IsActive] = @p1 WHERE [UserId] = @p2");

        Assert.That(await lt.ExecuteNonQueryAsync(), Is.EqualTo(1));
        Assert.That(await pg.ExecuteNonQueryAsync(), Is.EqualTo(1));
        Assert.That(await my.ExecuteNonQueryAsync(), Is.EqualTo(1));
        Assert.That(await ss.ExecuteNonQueryAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task Update_SetPatch_EmptyMask_Throws()
    {
        // Default-constructed Patch has __mask = 0 — generated terminal must throw
        // because an empty SET clause is invalid SQL in every dialect.
        //
        // The chain is built inside the Assert.ThrowsAsync lambda so we don't
        // hoist a Prepare()-returned variable into a captured lambda, which
        // would trigger QRY035 (PreparedQuery escaping the declaring scope).
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var empty = default(User.Patch);
        Assert.ThrowsAsync<System.InvalidOperationException>(
            async () => await Lite.Users().Update().Set(empty).Where(u => u.UserId == 1).ExecuteNonQueryAsync());
    }

    #endregion

    #region UpdateSetPatch — integration matrix (Phase 9)

    [Test]
    public async Task Update_SetPatch_FkColumn_BindsEntityRef()
    {
        // Account.Patch.UserId is EntityRef<User, int>; the per-column binder must
        // extract the .Id value (not bind the whole struct). The implicit conversion
        // from int → EntityRef<User, int> lets us assign a literal.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var ltPatch = new Account.Patch { UserId = 2, AccountName = "Repointed" };
        var pgPatch = new Pg.Account.Patch { UserId = 2, AccountName = "Repointed" };
        var myPatch = new My.Account.Patch { UserId = 2, AccountName = "Repointed" };
        var ssPatch = new Ss.Account.Patch { UserId = 2, AccountName = "Repointed" };

        var lt = Lite.Accounts().Update().Set(ltPatch).Where(a => a.AccountId == 1).Prepare();
        var pg = Pg.Accounts().Update().Set(pgPatch).Where(a => a.AccountId == 1).Prepare();
        var my = My.Accounts().Update().Set(myPatch).Where(a => a.AccountId == 1).Prepare();
        var ss = Ss.Accounts().Update().Set(ssPatch).Where(a => a.AccountId == 1).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "UPDATE \"accounts\" SET \"UserId\" = @p0, \"AccountName\" = @p1 WHERE \"AccountId\" = 1",
            pg:     "UPDATE \"accounts\" SET \"UserId\" = $1, \"AccountName\" = $2 WHERE \"AccountId\" = 1",
            mysql:  "UPDATE `accounts` SET `UserId` = ?, `AccountName` = ? WHERE `AccountId` = 1",
            ss:     "UPDATE [accounts] SET [UserId] = @p0, [AccountName] = @p1 WHERE [AccountId] = 1");
    }

    [Test]
    public async Task Update_SetPatch_EnumColumn_CastsUnderlying()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var ltPatch = new Order.Patch { Priority = OrderPriority.Urgent };
        var pgPatch = new Pg.Order.Patch { Priority = OrderPriority.Urgent };
        var myPatch = new My.Order.Patch { Priority = OrderPriority.Urgent };
        var ssPatch = new Ss.Order.Patch { Priority = OrderPriority.Urgent };

        var lt = Lite.Orders().Update().Set(ltPatch).Where(o => o.OrderId == 1).Prepare();
        var pg = Pg.Orders().Update().Set(pgPatch).Where(o => o.OrderId == 1).Prepare();
        var my = My.Orders().Update().Set(myPatch).Where(o => o.OrderId == 1).Prepare();
        var ss = Ss.Orders().Update().Set(ssPatch).Where(o => o.OrderId == 1).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "UPDATE \"orders\" SET \"Priority\" = @p0 WHERE \"OrderId\" = 1",
            pg:     "UPDATE \"orders\" SET \"Priority\" = $1 WHERE \"OrderId\" = 1",
            mysql:  "UPDATE `orders` SET `Priority` = ? WHERE `OrderId` = 1",
            ss:     "UPDATE [orders] SET [Priority] = @p0 WHERE [OrderId] = 1");
    }

    [Test]
    public async Task Update_SetPatch_CustomMapper_InvokesToDb()
    {
        // Account.Patch.Balance is Money mapped via MoneyMapping.ToDb. The
        // per-column binder must route through the mapper rather than calling
        // Convert.ChangeType on the Money struct directly.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var ltPatch = new Account.Patch { Balance = new Money(250m) };
        var pgPatch = new Pg.Account.Patch { Balance = new Money(250m) };
        var myPatch = new My.Account.Patch { Balance = new Money(250m) };
        var ssPatch = new Ss.Account.Patch { Balance = new Money(250m) };

        var lt = Lite.Accounts().Update().Set(ltPatch).Where(a => a.AccountId == 1).Prepare();
        var pg = Pg.Accounts().Update().Set(pgPatch).Where(a => a.AccountId == 1).Prepare();
        var my = My.Accounts().Update().Set(myPatch).Where(a => a.AccountId == 1).Prepare();
        var ss = Ss.Accounts().Update().Set(ssPatch).Where(a => a.AccountId == 1).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "UPDATE \"accounts\" SET \"Balance\" = @p0 WHERE \"AccountId\" = 1",
            pg:     "UPDATE \"accounts\" SET \"Balance\" = $1 WHERE \"AccountId\" = 1",
            mysql:  "UPDATE `accounts` SET `Balance` = ? WHERE `AccountId` = 1",
            ss:     "UPDATE [accounts] SET [Balance] = @p0 WHERE [AccountId] = 1");
    }

    [Test]
    public async Task Update_SetPatch_ExecutableUpdateBuilder_WhereFirst()
    {
        // Set comes AFTER Where — exercises the IExecutableUpdateBuilder<T>
        // extension overload (separate signature from IUpdateBuilder<T>).
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var ltPatch = new User.Patch { UserName = "PostWhere" };
        var pgPatch = new Pg.User.Patch { UserName = "PostWhere" };
        var myPatch = new My.User.Patch { UserName = "PostWhere" };
        var ssPatch = new Ss.User.Patch { UserName = "PostWhere" };

        var lt = Lite.Users().Update().Where(u => u.UserId == 1).Set(ltPatch).Prepare();
        var pg = Pg.Users().Update().Where(u => u.UserId == 1).Set(pgPatch).Prepare();
        var my = My.Users().Update().Where(u => u.UserId == 1).Set(myPatch).Prepare();
        var ss = Ss.Users().Update().Where(u => u.UserId == 1).Set(ssPatch).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = @p0 WHERE \"UserId\" = 1",
            pg:     "UPDATE \"users\" SET \"UserName\" = $1 WHERE \"UserId\" = 1",
            mysql:  "UPDATE `users` SET `UserName` = ? WHERE `UserId` = 1",
            ss:     "UPDATE [users] SET [UserName] = @p0 WHERE [UserId] = 1");

        Assert.That(await lt.ExecuteNonQueryAsync(), Is.EqualTo(1));
    }

    #endregion

    #region UpdateSetPatchAction — lambda form

    [Test]
    public async Task Update_SetPatchAction_SingleColumn()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Update().Set((ref User.Patch p) => p.UserName = "Renamed").Where(u => u.UserId == 1).Prepare();
        var pg = Pg.Users().Update().Set((ref Pg.User.Patch p) => p.UserName = "Renamed").Where(u => u.UserId == 1).Prepare();
        var my = My.Users().Update().Set((ref My.User.Patch p) => p.UserName = "Renamed").Where(u => u.UserId == 1).Prepare();
        var ss = Ss.Users().Update().Set((ref Ss.User.Patch p) => p.UserName = "Renamed").Where(u => u.UserId == 1).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = @p0 WHERE \"UserId\" = 1",
            pg:     "UPDATE \"users\" SET \"UserName\" = $1 WHERE \"UserId\" = 1",
            mysql:  "UPDATE `users` SET `UserName` = ? WHERE `UserId` = 1",
            ss:     "UPDATE [users] SET [UserName] = @p0 WHERE [UserId] = 1");

        Assert.That(await lt.ExecuteNonQueryAsync(), Is.EqualTo(1));
        Assert.That(await pg.ExecuteNonQueryAsync(), Is.EqualTo(1));
        Assert.That(await my.ExecuteNonQueryAsync(), Is.EqualTo(1));
        Assert.That(await ss.ExecuteNonQueryAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task Update_SetPatchAction_RuntimeConditional_TogglesColumns()
    {
        // Lambda mutates the Patch with C# conditionals — exercises that the
        // runtime SET assembler only writes the columns whose property setters
        // actually fired (mask bits set in the lambda body).
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        var updateName = true;
        var updateEmail = false;

        var diag = Lite.Users().Update().Set((ref User.Patch p) =>
        {
            if (updateName) p.UserName = "OnlyName";
            if (updateEmail) p.Email = "ignored@example.com";
        }).Where(u => u.UserId == 1).Prepare().ToDiagnostics();

        // Only UserName bit is set — runtime emits just that column.
        Assert.That(diag.Sql, Is.EqualTo("UPDATE \"users\" SET \"UserName\" = @p0 WHERE \"UserId\" = 1"));
    }

    [Test]
    public async Task Update_SetPatchAction_EmptyLambda_Throws()
    {
        // A lambda that flips no mask bits leaves __mask = 0 — runtime must throw.
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, _, _, _) = t;

        Assert.ThrowsAsync<System.InvalidOperationException>(
            async () => await Lite.Users().Update().Set((ref User.Patch _) => { }).Where(u => u.UserId == 1).ExecuteNonQueryAsync());
    }

    #endregion
}
