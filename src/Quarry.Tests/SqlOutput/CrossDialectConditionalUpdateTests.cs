using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


/// <summary>
/// End-to-end verification of conditional UPDATE chains (multiple <c>.Set()</c> calls inside
/// <c>if</c> blocks). The <see cref="Generation.ConditionalCarrierTests"/> suite verifies the
/// generator's mask/carrier shape; these tests exercise the actual SQL each mask value
/// produces against all four dialects and confirm the resulting row state.
/// </summary>
[TestFixture]
internal class CrossDialectConditionalUpdateTests
{
    #region Single conditional Set after Where (K=1, mirrors unit test Update_ConditionalAdditionalSet_CarrierWithMask)

    [TestCase(true)]
    [TestCase(false)]
    public async Task Update_ConditionalSet_AfterWhere_OneBit(bool deactivate)
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Build per-dialect chains with the same conditional shape.
        var qLite = Lite.Users().Update().Set(u => u.UserName = "Updated").Where(u => u.UserId == 1);
        if (deactivate) qLite = qLite.Set(u => u.IsActive = false);

        var qPg = Pg.Users().Update().Set(u => u.UserName = "Updated").Where(u => u.UserId == 1);
        if (deactivate) qPg = qPg.Set(u => u.IsActive = false);

        var qMy = My.Users().Update().Set(u => u.UserName = "Updated").Where(u => u.UserId == 1);
        if (deactivate) qMy = qMy.Set(u => u.IsActive = false);

        var qSs = Ss.Users().Update().Set(u => u.UserName = "Updated").Where(u => u.UserId == 1);
        if (deactivate) qSs = qSs.Set(u => u.IsActive = false);

        var lt = qLite.Prepare();
        var pg = qPg.Prepare();
        var my = qMy.Prepare();
        var ss = qSs.Prepare();

        // ToDiagnostics() must be called at the same nesting depth as the conditional
        // .Set() above — putting it inside an `if (deactivate) { … }` branch would
        // colocate the chain's terminal with the conditional clause and the
        // chain analyzer would (correctly) collapse them into one unconditional variant.
        var (sqlite, pg_, mysql, ss_) = deactivate
            ? ("UPDATE \"users\" SET \"UserName\" = 'Updated', \"IsActive\" = 0 WHERE \"UserId\" = 1",
               "UPDATE \"users\" SET \"UserName\" = 'Updated', \"IsActive\" = FALSE WHERE \"UserId\" = 1",
               "UPDATE `users` SET `UserName` = 'Updated', `IsActive` = 0 WHERE `UserId` = 1",
               "UPDATE [users] SET [UserName] = 'Updated', [IsActive] = 0 WHERE [UserId] = 1")
            : ("UPDATE \"users\" SET \"UserName\" = 'Updated' WHERE \"UserId\" = 1",
               "UPDATE \"users\" SET \"UserName\" = 'Updated' WHERE \"UserId\" = 1",
               "UPDATE `users` SET `UserName` = 'Updated' WHERE `UserId` = 1",
               "UPDATE [users] SET [UserName] = 'Updated' WHERE [UserId] = 1");

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: sqlite, pg: pg_, mysql: mysql, ss: ss_);

        Assert.That(await lt.ExecuteNonQueryAsync(), Is.EqualTo(1));
        Assert.That(await pg.ExecuteNonQueryAsync(), Is.EqualTo(1));
        Assert.That(await my.ExecuteNonQueryAsync(), Is.EqualTo(1));
        Assert.That(await ss.ExecuteNonQueryAsync(), Is.EqualTo(1));

        // Alice's seeded IsActive is true; only the deactivate branch should flip it.
        await AssertUserState(Lite, Pg, My, Ss, userId: 1,
            expectedName: "Updated",
            expectedActive: !deactivate);
    }

    #endregion

    #region Two conditional Sets before Where (K=2, the multi-bit scenario the user asked about)

    [TestCase(true,  true)]
    [TestCase(true,  false)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public async Task Update_TwoConditionalSets_BeforeWhere_TwoBits(bool setEmail, bool deactivate)
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var qLite = Lite.Users().Update().Set(u => u.UserName = "Updated");
        if (setEmail)   qLite = qLite.Set(u => u.Email = "new@test.com");
        if (deactivate) qLite = qLite.Set(u => u.IsActive = false);
        var lt = qLite.Where(u => u.UserId == 1).Prepare();

        var qPg = Pg.Users().Update().Set(u => u.UserName = "Updated");
        if (setEmail)   qPg = qPg.Set(u => u.Email = "new@test.com");
        if (deactivate) qPg = qPg.Set(u => u.IsActive = false);
        var pg = qPg.Where(u => u.UserId == 1).Prepare();

        var qMy = My.Users().Update().Set(u => u.UserName = "Updated");
        if (setEmail)   qMy = qMy.Set(u => u.Email = "new@test.com");
        if (deactivate) qMy = qMy.Set(u => u.IsActive = false);
        var my = qMy.Where(u => u.UserId == 1).Prepare();

        var qSs = Ss.Users().Update().Set(u => u.UserName = "Updated");
        if (setEmail)   qSs = qSs.Set(u => u.Email = "new@test.com");
        if (deactivate) qSs = qSs.Set(u => u.IsActive = false);
        var ss = qSs.Where(u => u.UserId == 1).Prepare();

        // SET clause should reflect exactly the active mask bits.
        var (litSql, pgSql, mySql, ssSql) = ExpectedTwoConditionalSetsSql(setEmail, deactivate);
        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: litSql, pg: pgSql, mysql: mySql, ss: ssSql);

        Assert.That(await lt.ExecuteNonQueryAsync(), Is.EqualTo(1));
        Assert.That(await pg.ExecuteNonQueryAsync(), Is.EqualTo(1));
        Assert.That(await my.ExecuteNonQueryAsync(), Is.EqualTo(1));
        Assert.That(await ss.ExecuteNonQueryAsync(), Is.EqualTo(1));

        // Alice's seed: Email='alice@test.com', IsActive=true. Only the active mask bits should flip values.
        await AssertUserState(Lite, Pg, My, Ss, userId: 1,
            expectedName: "Updated",
            expectedEmail: setEmail ? "new@test.com" : "alice@test.com",
            expectedActive: !deactivate);
    }

    private static (string sqlite, string pg, string mysql, string ss) ExpectedTwoConditionalSetsSql(bool setEmail, bool deactivate)
    {
        var litSet  = BuildSet("\"", "\"", setEmail, deactivate, falseLiteral: "0");
        var pgSet   = BuildSet("\"", "\"", setEmail, deactivate, falseLiteral: "FALSE");
        var mySet   = BuildSet("`",  "`",  setEmail, deactivate, falseLiteral: "0");
        var ssSet   = BuildSet("[",  "]",  setEmail, deactivate, falseLiteral: "0");

        return (
            $"UPDATE \"users\" SET {litSet} WHERE \"UserId\" = 1",
            $"UPDATE \"users\" SET {pgSet} WHERE \"UserId\" = 1",
            $"UPDATE `users` SET {mySet} WHERE `UserId` = 1",
            $"UPDATE [users] SET {ssSet} WHERE [UserId] = 1");
    }

    private static string BuildSet(string lq, string rq, bool setEmail, bool deactivate, string falseLiteral)
    {
        var clauses = new List<string> { $"{lq}UserName{rq} = 'Updated'" };
        if (setEmail)   clauses.Add($"{lq}Email{rq} = 'new@test.com'");
        if (deactivate) clauses.Add($"{lq}IsActive{rq} = {falseLiteral}");
        return string.Join(", ", clauses);
    }

    #endregion

    #region Mutually-exclusive if/else Set (one bit pair, two reachable variants)

    [TestCase(true)]
    [TestCase(false)]
    public async Task Update_IfElseSet_MutuallyExclusive(bool activate)
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        // Target Charlie (UserId=3, seeded IsActive=false) so both branches change row state.
        var qLite = Lite.Users().Update().Set(u => u.UserName = "Flipped");
        if (activate) qLite = qLite.Set(u => u.IsActive = true);
        else          qLite = qLite.Set(u => u.IsActive = false);
        var lt = qLite.Where(u => u.UserId == 3).Prepare();

        var qPg = Pg.Users().Update().Set(u => u.UserName = "Flipped");
        if (activate) qPg = qPg.Set(u => u.IsActive = true);
        else          qPg = qPg.Set(u => u.IsActive = false);
        var pg = qPg.Where(u => u.UserId == 3).Prepare();

        var qMy = My.Users().Update().Set(u => u.UserName = "Flipped");
        if (activate) qMy = qMy.Set(u => u.IsActive = true);
        else          qMy = qMy.Set(u => u.IsActive = false);
        var my = qMy.Where(u => u.UserId == 3).Prepare();

        var qSs = Ss.Users().Update().Set(u => u.UserName = "Flipped");
        if (activate) qSs = qSs.Set(u => u.IsActive = true);
        else          qSs = qSs.Set(u => u.IsActive = false);
        var ss = qSs.Where(u => u.UserId == 3).Prepare();

        var (sqlite, pg_, mysql, ss_) = activate
            ? ("UPDATE \"users\" SET \"UserName\" = 'Flipped', \"IsActive\" = 1 WHERE \"UserId\" = 3",
               "UPDATE \"users\" SET \"UserName\" = 'Flipped', \"IsActive\" = TRUE WHERE \"UserId\" = 3",
               "UPDATE `users` SET `UserName` = 'Flipped', `IsActive` = 1 WHERE `UserId` = 3",
               "UPDATE [users] SET [UserName] = 'Flipped', [IsActive] = 1 WHERE [UserId] = 3")
            : ("UPDATE \"users\" SET \"UserName\" = 'Flipped', \"IsActive\" = 0 WHERE \"UserId\" = 3",
               "UPDATE \"users\" SET \"UserName\" = 'Flipped', \"IsActive\" = FALSE WHERE \"UserId\" = 3",
               "UPDATE `users` SET `UserName` = 'Flipped', `IsActive` = 0 WHERE `UserId` = 3",
               "UPDATE [users] SET [UserName] = 'Flipped', [IsActive] = 0 WHERE [UserId] = 3");

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: sqlite, pg: pg_, mysql: mysql, ss: ss_);

        Assert.That(await lt.ExecuteNonQueryAsync(), Is.EqualTo(1));
        Assert.That(await pg.ExecuteNonQueryAsync(), Is.EqualTo(1));
        Assert.That(await my.ExecuteNonQueryAsync(), Is.EqualTo(1));
        Assert.That(await ss.ExecuteNonQueryAsync(), Is.EqualTo(1));

        await AssertUserState(Lite, Pg, My, Ss, userId: 3,
            expectedName: "Flipped",
            expectedActive: activate);
    }

    #endregion

    #region Conditional Set with captured value (verifies parameter binding under mask)

    [TestCase(true)]
    [TestCase(false)]
    public async Task Update_ConditionalSet_CapturedValue(bool overrideName)
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var newName = "Captured";

        var qLite = Lite.Users().Update().Set(u => u.IsActive = false);
        if (overrideName) qLite = qLite.Set(u => u.UserName = newName);
        var lt = qLite.Where(u => u.UserId == 2).Prepare();

        var qPg = Pg.Users().Update().Set(u => u.IsActive = false);
        if (overrideName) qPg = qPg.Set(u => u.UserName = newName);
        var pg = qPg.Where(u => u.UserId == 2).Prepare();

        var qMy = My.Users().Update().Set(u => u.IsActive = false);
        if (overrideName) qMy = qMy.Set(u => u.UserName = newName);
        var my = qMy.Where(u => u.UserId == 2).Prepare();

        var qSs = Ss.Users().Update().Set(u => u.IsActive = false);
        if (overrideName) qSs = qSs.Set(u => u.UserName = newName);
        var ss = qSs.Where(u => u.UserId == 2).Prepare();

        // Captured 'newName' threads through the conditional SET clause when active;
        // the first SET (IsActive) is a literal regardless of mask.
        var (sqlite, pg_, mysql, ss_) = overrideName
            ? ("UPDATE \"users\" SET \"IsActive\" = 0, \"UserName\" = @p0 WHERE \"UserId\" = 2",
               "UPDATE \"users\" SET \"IsActive\" = FALSE, \"UserName\" = $1 WHERE \"UserId\" = 2",
               "UPDATE `users` SET `IsActive` = 0, `UserName` = ? WHERE `UserId` = 2",
               "UPDATE [users] SET [IsActive] = 0, [UserName] = @p0 WHERE [UserId] = 2")
            : ("UPDATE \"users\" SET \"IsActive\" = 0 WHERE \"UserId\" = 2",
               "UPDATE \"users\" SET \"IsActive\" = FALSE WHERE \"UserId\" = 2",
               "UPDATE `users` SET `IsActive` = 0 WHERE `UserId` = 2",
               "UPDATE [users] SET [IsActive] = 0 WHERE [UserId] = 2");

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: sqlite, pg: pg_, mysql: mysql, ss: ss_);

        Assert.That(await lt.ExecuteNonQueryAsync(), Is.EqualTo(1));
        Assert.That(await pg.ExecuteNonQueryAsync(), Is.EqualTo(1));
        Assert.That(await my.ExecuteNonQueryAsync(), Is.EqualTo(1));
        Assert.That(await ss.ExecuteNonQueryAsync(), Is.EqualTo(1));

        // Bob's seed: UserName='Bob', IsActive=true.
        await AssertUserState(Lite, Pg, My, Ss, userId: 2,
            expectedName: overrideName ? "Captured" : "Bob",
            expectedActive: false);
    }

    #endregion

    private static async Task AssertUserState(
        TestDbContext lite, Pg.PgDb pg, My.MyDb my, Ss.SsDb ss,
        int userId,
        string? expectedName = null,
        string? expectedEmail = null,
        bool? expectedActive = null)
    {
        var liteRow = await lite.Users().Where(u => u.UserId == userId)
            .Select(u => (u.UserName, u.Email, u.IsActive)).ExecuteFetchFirstAsync();
        var pgRow = await pg.Users().Where(u => u.UserId == userId)
            .Select(u => (u.UserName, u.Email, u.IsActive)).ExecuteFetchFirstAsync();
        var myRow = await my.Users().Where(u => u.UserId == userId)
            .Select(u => (u.UserName, u.Email, u.IsActive)).ExecuteFetchFirstAsync();
        var ssRow = await ss.Users().Where(u => u.UserId == userId)
            .Select(u => (u.UserName, u.Email, u.IsActive)).ExecuteFetchFirstAsync();

        Assert.Multiple(() =>
        {
            if (expectedName is not null)
            {
                Assert.That(liteRow.UserName, Is.EqualTo(expectedName), "SQLite UserName");
                Assert.That(pgRow.UserName,   Is.EqualTo(expectedName), "PostgreSQL UserName");
                Assert.That(myRow.UserName,   Is.EqualTo(expectedName), "MySQL UserName");
                Assert.That(ssRow.UserName,   Is.EqualTo(expectedName), "SqlServer UserName");
            }
            if (expectedEmail is not null)
            {
                Assert.That(liteRow.Email, Is.EqualTo(expectedEmail), "SQLite Email");
                Assert.That(pgRow.Email,   Is.EqualTo(expectedEmail), "PostgreSQL Email");
                Assert.That(myRow.Email,   Is.EqualTo(expectedEmail), "MySQL Email");
                Assert.That(ssRow.Email,   Is.EqualTo(expectedEmail), "SqlServer Email");
            }
            if (expectedActive is not null)
            {
                Assert.That(liteRow.IsActive, Is.EqualTo(expectedActive.Value), "SQLite IsActive");
                Assert.That(pgRow.IsActive,   Is.EqualTo(expectedActive.Value), "PostgreSQL IsActive");
                Assert.That(myRow.IsActive,   Is.EqualTo(expectedActive.Value), "MySQL IsActive");
                Assert.That(ssRow.IsActive,   Is.EqualTo(expectedActive.Value), "SqlServer IsActive");
            }
        });
    }
}
