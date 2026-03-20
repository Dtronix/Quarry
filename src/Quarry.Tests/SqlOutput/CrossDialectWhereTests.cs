using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


[TestFixture]
internal class CrossDialectWhereTests : CrossDialectTestBase
{
    #region Boolean

    [Test]
    public void Where_BooleanProperty()
    {
        AssertDialects(
            Lite.Users().Where(u => u.IsActive).ToDiagnostics(),
            Pg.Users().Where(u => u.IsActive).ToDiagnostics(),
            My.Users().Where(u => u.IsActive).ToDiagnostics(),
            Ss.Users().Where(u => u.IsActive).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE \"IsActive\" = 1",
            pg:     "SELECT * FROM \"users\" WHERE \"IsActive\" = TRUE",
            mysql:  "SELECT * FROM `users` WHERE `IsActive` = 1",
            ss:     "SELECT * FROM [users] WHERE [IsActive] = 1");
    }

    [Test]
    public void Where_NegatedBoolean()
    {
        AssertDialects(
            Lite.Users().Where(u => !u.IsActive).ToDiagnostics(),
            Pg.Users().Where(u => !u.IsActive).ToDiagnostics(),
            My.Users().Where(u => !u.IsActive).ToDiagnostics(),
            Ss.Users().Where(u => !u.IsActive).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE NOT (\"IsActive\")",
            pg:     "SELECT * FROM \"users\" WHERE NOT (\"IsActive\")",
            mysql:  "SELECT * FROM `users` WHERE NOT (`IsActive`)",
            ss:     "SELECT * FROM [users] WHERE NOT ([IsActive])");
    }

    #endregion

    #region Comparison Operators

    [Test]
    public void Where_GreaterThan()
    {
        AssertDialects(
            Lite.Users().Where(u => u.UserId > 0).ToDiagnostics(),
            Pg.Users().Where(u => u.UserId > 0).ToDiagnostics(),
            My.Users().Where(u => u.UserId > 0).ToDiagnostics(),
            Ss.Users().Where(u => u.UserId > 0).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE (\"UserId\" > 0)",
            pg:     "SELECT * FROM \"users\" WHERE (\"UserId\" > 0)",
            mysql:  "SELECT * FROM `users` WHERE (`UserId` > 0)",
            ss:     "SELECT * FROM [users] WHERE ([UserId] > 0)");
    }

    [Test]
    public void Where_LessThanOrEqual()
    {
        AssertDialects(
            Lite.Users().Where(u => u.UserId <= 100).ToDiagnostics(),
            Pg.Users().Where(u => u.UserId <= 100).ToDiagnostics(),
            My.Users().Where(u => u.UserId <= 100).ToDiagnostics(),
            Ss.Users().Where(u => u.UserId <= 100).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE (\"UserId\" <= 100)",
            pg:     "SELECT * FROM \"users\" WHERE (\"UserId\" <= 100)",
            mysql:  "SELECT * FROM `users` WHERE (`UserId` <= 100)",
            ss:     "SELECT * FROM [users] WHERE ([UserId] <= 100)");
    }

    #endregion

    #region Null Checks

    [Test]
    public void Where_IsNull()
    {
        AssertDialects(
            Lite.Users().Where(u => u.Email == null).ToDiagnostics(),
            Pg.Users().Where(u => u.Email == null).ToDiagnostics(),
            My.Users().Where(u => u.Email == null).ToDiagnostics(),
            Ss.Users().Where(u => u.Email == null).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE \"Email\" IS NULL",
            pg:     "SELECT * FROM \"users\" WHERE \"Email\" IS NULL",
            mysql:  "SELECT * FROM `users` WHERE `Email` IS NULL",
            ss:     "SELECT * FROM [users] WHERE [Email] IS NULL");
    }

    [Test]
    public void Where_IsNotNull()
    {
        AssertDialects(
            Lite.Users().Where(u => u.Email != null).ToDiagnostics(),
            Pg.Users().Where(u => u.Email != null).ToDiagnostics(),
            My.Users().Where(u => u.Email != null).ToDiagnostics(),
            Ss.Users().Where(u => u.Email != null).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE \"Email\" IS NOT NULL",
            pg:     "SELECT * FROM \"users\" WHERE \"Email\" IS NOT NULL",
            mysql:  "SELECT * FROM `users` WHERE `Email` IS NOT NULL",
            ss:     "SELECT * FROM [users] WHERE [Email] IS NOT NULL");
    }

    #endregion

    #region Chained Where

    [Test]
    public void Where_MultipleChained()
    {
        AssertDialects(
            Lite.Users().Where(u => u.IsActive).Where(u => u.UserId > 0).ToDiagnostics(),
            Pg.Users().Where(u => u.IsActive).Where(u => u.UserId > 0).ToDiagnostics(),
            My.Users().Where(u => u.IsActive).Where(u => u.UserId > 0).ToDiagnostics(),
            Ss.Users().Where(u => u.IsActive).Where(u => u.UserId > 0).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE (\"IsActive\" = 1) AND ((\"UserId\" > 0))",
            pg:     "SELECT * FROM \"users\" WHERE (\"IsActive\" = TRUE) AND ((\"UserId\" > 0))",
            mysql:  "SELECT * FROM `users` WHERE (`IsActive` = 1) AND ((`UserId` > 0))",
            ss:     "SELECT * FROM [users] WHERE ([IsActive] = 1) AND (([UserId] > 0))");
    }

    [Test]
    public void Where_NullCheck_And_Boolean()
    {
        AssertDialects(
            Lite.Users().Where(u => u.Email != null).Where(u => u.IsActive).ToDiagnostics(),
            Pg.Users().Where(u => u.Email != null).Where(u => u.IsActive).ToDiagnostics(),
            My.Users().Where(u => u.Email != null).Where(u => u.IsActive).ToDiagnostics(),
            Ss.Users().Where(u => u.Email != null).Where(u => u.IsActive).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE (\"Email\" IS NOT NULL) AND (\"IsActive\" = 1)",
            pg:     "SELECT * FROM \"users\" WHERE (\"Email\" IS NOT NULL) AND (\"IsActive\" = TRUE)",
            mysql:  "SELECT * FROM `users` WHERE (`Email` IS NOT NULL) AND (`IsActive` = 1)",
            ss:     "SELECT * FROM [users] WHERE ([Email] IS NOT NULL) AND ([IsActive] = 1)");
    }

    #endregion

    #region Where + Select Combined

    [Test]
    public void Where_ThenSelect_Tuple()
    {
        AssertDialects(
            Lite.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).ToDiagnostics(),
            Pg.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).ToDiagnostics(),
            My.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).ToDiagnostics(),
            Ss.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = 1",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = TRUE",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE `IsActive` = 1",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE [IsActive] = 1");
    }

    [Test]
    public void Where_ThenSelect_Dto()
    {
        AssertDialects(
            Lite.Users().Where(u => u.IsActive).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            Pg.Users().Where(u => u.IsActive).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            My.Users().Where(u => u.IsActive).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            Ss.Users().Where(u => u.IsActive).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"IsActive\" = 1",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"IsActive\" = TRUE",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users` WHERE `IsActive` = 1",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users] WHERE [IsActive] = 1");
    }

    #endregion
}
