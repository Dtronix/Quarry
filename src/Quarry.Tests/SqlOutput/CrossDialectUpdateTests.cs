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
            Lite.Users().Update().Set(u => u.UserName, "NewName").Where(u => u.UserId == 1).ToDiagnostics(),
            Pg.Users().Update().Set(u => u.UserName, "NewName").Where(u => u.UserId == 1).ToDiagnostics(),
            My.Users().Update().Set(u => u.UserName, "NewName").Where(u => u.UserId == 1).ToDiagnostics(),
            Ss.Users().Update().Set(u => u.UserName, "NewName").Where(u => u.UserId == 1).ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = @p0 WHERE (\"UserId\" = 1)",
            pg:     "UPDATE \"users\" SET \"UserName\" = $1 WHERE (\"UserId\" = 1)",
            mysql:  "UPDATE `users` SET `UserName` = ? WHERE (`UserId` = 1)",
            ss:     "UPDATE [users] SET [UserName] = @p0 WHERE ([UserId] = 1)");
    }

    [Test]
    public void Update_Set_Where_Boolean()
    {
        AssertDialects(
            Lite.Users().Update().Set(u => u.IsActive, false).Where(u => u.IsActive).ToDiagnostics(),
            Pg.Users().Update().Set(u => u.IsActive, false).Where(u => u.IsActive).ToDiagnostics(),
            My.Users().Update().Set(u => u.IsActive, false).Where(u => u.IsActive).ToDiagnostics(),
            Ss.Users().Update().Set(u => u.IsActive, false).Where(u => u.IsActive).ToDiagnostics(),
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
            Lite.Users().Update().Set(new User { UserName = "New", IsActive = false }).Where(u => u.UserId == 1).ToDiagnostics(),
            Pg.Users().Update().Set(new Pg.User { UserName = "New", IsActive = false }).Where(u => u.UserId == 1).ToDiagnostics(),
            My.Users().Update().Set(new My.User { UserName = "New", IsActive = false }).Where(u => u.UserId == 1).ToDiagnostics(),
            Ss.Users().Update().Set(new Ss.User { UserName = "New", IsActive = false }).Where(u => u.UserId == 1).ToDiagnostics(),
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
            Lite.Users().Update().Set(u => u.UserName, "x").Set(u => u.IsActive, false).Where(u => u.UserId == 1).ToDiagnostics(),
            Pg.Users().Update().Set(u => u.UserName, "x").Set(u => u.IsActive, false).Where(u => u.UserId == 1).ToDiagnostics(),
            My.Users().Update().Set(u => u.UserName, "x").Set(u => u.IsActive, false).Where(u => u.UserId == 1).ToDiagnostics(),
            Ss.Users().Update().Set(u => u.UserName, "x").Set(u => u.IsActive, false).Where(u => u.UserId == 1).ToDiagnostics(),
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
            Lite.Users().Update().Set(u => u.UserName, "x").Where(u => u.UserId == id).ToDiagnostics(),
            Pg.Users().Update().Set(u => u.UserName, "x").Where(u => u.UserId == id).ToDiagnostics(),
            My.Users().Update().Set(u => u.UserName, "x").Where(u => u.UserId == id).ToDiagnostics(),
            Ss.Users().Update().Set(u => u.UserName, "x").Where(u => u.UserId == id).ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = @p0 WHERE (\"UserId\" = @p1)",
            pg:     "UPDATE \"users\" SET \"UserName\" = $1 WHERE (\"UserId\" = $2)",
            mysql:  "UPDATE `users` SET `UserName` = ? WHERE (`UserId` = ?)",
            ss:     "UPDATE [users] SET [UserName] = @p0 WHERE ([UserId] = @p1)");
    }

    #endregion

    #region TypeMapping in UPDATE SET

    [Test]
    public void Update_Set_TypeMappedColumn()
    {
        AssertDialects(
            Lite.Accounts().Update().Set(a => a.Balance, new Money(100m)).Where(a => a.AccountId == 1).ToDiagnostics(),
            Pg.Accounts().Update().Set(a => a.Balance, new Money(100m)).Where(a => a.AccountId == 1).ToDiagnostics(),
            My.Accounts().Update().Set(a => a.Balance, new Money(100m)).Where(a => a.AccountId == 1).ToDiagnostics(),
            Ss.Accounts().Update().Set(a => a.Balance, new Money(100m)).Where(a => a.AccountId == 1).ToDiagnostics(),
            sqlite: "UPDATE \"accounts\" SET \"Balance\" = @p0 WHERE (\"AccountId\" = 1)",
            pg:     "UPDATE \"accounts\" SET \"Balance\" = $1 WHERE (\"AccountId\" = 1)",
            mysql:  "UPDATE `accounts` SET `Balance` = ? WHERE (`AccountId` = 1)",
            ss:     "UPDATE [accounts] SET [Balance] = @p0 WHERE ([AccountId] = 1)");
    }

    #endregion
}
