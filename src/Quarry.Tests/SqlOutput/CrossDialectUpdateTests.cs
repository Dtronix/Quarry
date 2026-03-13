using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;

#pragma warning disable QRY001

[TestFixture]
internal class CrossDialectUpdateTests : CrossDialectTestBase
{
    #region Update Set + Where (Strengthened)

    [Test]
    public void Update_Set_Where_Equality()
    {
        AssertDialects(
            Lite.Update<User>().Set(u => u.UserName, "NewName").Where(u => u.UserId == 1).ToTestCase(),
            Pg.Update<Pg.User>().Set(u => u.UserName, "NewName").Where(u => u.UserId == 1).ToTestCase(),
            My.Update<My.User>().Set(u => u.UserName, "NewName").Where(u => u.UserId == 1).ToTestCase(),
            Ss.Update<Ss.User>().Set(u => u.UserName, "NewName").Where(u => u.UserId == 1).ToTestCase(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = @p0 WHERE (\"UserId\" = 1)",
            pg:     "UPDATE \"users\" SET \"UserName\" = $1 WHERE (\"UserId\" = 1)",
            mysql:  "UPDATE `users` SET `UserName` = ? WHERE (`UserId` = 1)",
            ss:     "UPDATE [users] SET [UserName] = @p0 WHERE ([UserId] = 1)");
    }

    [Test]
    public void Update_Set_Where_Boolean()
    {
        AssertDialects(
            Lite.Update<User>().Set(u => u.IsActive, false).Where(u => u.IsActive).ToTestCase(),
            Pg.Update<Pg.User>().Set(u => u.IsActive, false).Where(u => u.IsActive).ToTestCase(),
            My.Update<My.User>().Set(u => u.IsActive, false).Where(u => u.IsActive).ToTestCase(),
            Ss.Update<Ss.User>().Set(u => u.IsActive, false).Where(u => u.IsActive).ToTestCase(),
            sqlite: "UPDATE \"users\" SET \"IsActive\" = @p0 WHERE \"IsActive\" = 1",
            pg:     "UPDATE \"users\" SET \"IsActive\" = $1 WHERE \"IsActive\" = TRUE",
            mysql:  "UPDATE `users` SET `IsActive` = ? WHERE `IsActive` = 1",
            ss:     "UPDATE [users] SET [IsActive] = @p0 WHERE [IsActive] = 1");
    }

    #endregion

    #region UpdateSetPoco

    [Test]
    public void Update_SetPoco()
    {
        AssertDialects(
            Lite.Update<User>().Set(new User { UserName = "New", IsActive = false }).Where(u => u.UserId == 1).ToTestCase(),
            Pg.Update<Pg.User>().Set(new Pg.User { UserName = "New", IsActive = false }).Where(u => u.UserId == 1).ToTestCase(),
            My.Update<My.User>().Set(new My.User { UserName = "New", IsActive = false }).Where(u => u.UserId == 1).ToTestCase(),
            Ss.Update<Ss.User>().Set(new Ss.User { UserName = "New", IsActive = false }).Where(u => u.UserId == 1).ToTestCase(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = @p0, \"IsActive\" = @p1 WHERE (\"UserId\" = 1)",
            pg:     "UPDATE \"users\" SET \"UserName\" = $1, \"IsActive\" = $2 WHERE (\"UserId\" = 1)",
            mysql:  "UPDATE `users` SET `UserName` = ?, `IsActive` = ? WHERE (`UserId` = 1)",
            ss:     "UPDATE [users] SET [UserName] = @p0, [IsActive] = @p1 WHERE ([UserId] = 1)");
    }

    #endregion

    #region Update with Multiple Set Calls

    [Test]
    public void Update_MultipleSet()
    {
        AssertDialects(
            Lite.Update<User>().Set(u => u.UserName, "x").Set(u => u.IsActive, false).Where(u => u.UserId == 1).ToTestCase(),
            Pg.Update<Pg.User>().Set(u => u.UserName, "x").Set(u => u.IsActive, false).Where(u => u.UserId == 1).ToTestCase(),
            My.Update<My.User>().Set(u => u.UserName, "x").Set(u => u.IsActive, false).Where(u => u.UserId == 1).ToTestCase(),
            Ss.Update<Ss.User>().Set(u => u.UserName, "x").Set(u => u.IsActive, false).Where(u => u.UserId == 1).ToTestCase(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = @p0, \"IsActive\" = @p1 WHERE (\"UserId\" = 1)",
            pg:     "UPDATE \"users\" SET \"UserName\" = $1, \"IsActive\" = $2 WHERE (\"UserId\" = 1)",
            mysql:  "UPDATE `users` SET `UserName` = ?, `IsActive` = ? WHERE (`UserId` = 1)",
            ss:     "UPDATE [users] SET [UserName] = @p0, [IsActive] = @p1 WHERE ([UserId] = 1)");
    }

    #endregion

    #region UpdateWhere with Captured Parameter

    [Test]
    public void Update_Where_CapturedParameter()
    {
        var id = 5;
        AssertDialects(
            Lite.Update<User>().Set(u => u.UserName, "x").Where(u => u.UserId == id).ToTestCase(),
            Pg.Update<Pg.User>().Set(u => u.UserName, "x").Where(u => u.UserId == id).ToTestCase(),
            My.Update<My.User>().Set(u => u.UserName, "x").Where(u => u.UserId == id).ToTestCase(),
            Ss.Update<Ss.User>().Set(u => u.UserName, "x").Where(u => u.UserId == id).ToTestCase(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = @p0 WHERE (\"UserId\" = @p1)",
            pg:     "UPDATE \"users\" SET \"UserName\" = $1 WHERE (\"UserId\" = @p1)",
            mysql:  "UPDATE `users` SET `UserName` = ? WHERE (`UserId` = @p1)",
            ss:     "UPDATE [users] SET [UserName] = @p0 WHERE ([UserId] = @p1)");
    }

    #endregion

    #region TypeMapping in UPDATE SET

    [Test]
    public void Update_Set_TypeMappedColumn()
    {
        AssertDialects(
            Lite.Update<Account>().Set(a => a.Balance, new Money(100m)).Where(a => a.AccountId == 1).ToTestCase(),
            Pg.Update<Pg.Account>().Set(a => a.Balance, new Money(100m)).Where(a => a.AccountId == 1).ToTestCase(),
            My.Update<My.Account>().Set(a => a.Balance, new Money(100m)).Where(a => a.AccountId == 1).ToTestCase(),
            Ss.Update<Ss.Account>().Set(a => a.Balance, new Money(100m)).Where(a => a.AccountId == 1).ToTestCase(),
            sqlite: "UPDATE \"accounts\" SET \"Balance\" = @p0 WHERE (\"AccountId\" = 1)",
            pg:     "UPDATE \"accounts\" SET \"Balance\" = $1 WHERE (\"AccountId\" = 1)",
            mysql:  "UPDATE `accounts` SET `Balance` = ? WHERE (`AccountId` = 1)",
            ss:     "UPDATE [accounts] SET [Balance] = @p0 WHERE ([AccountId] = 1)");
    }

    #endregion
}
