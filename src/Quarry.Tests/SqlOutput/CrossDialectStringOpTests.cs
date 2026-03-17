using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;

#pragma warning disable QRY001

[TestFixture]
internal class CrossDialectStringOpTests : CrossDialectTestBase
{
    #region Contains

    [Test]
    public void Where_Contains_LiteralString()
    {
        AssertDialects(
            Lite.Users().Where(u => u.UserName.Contains("User05")).ToTestCase(),
            Pg.Users().Where(u => u.UserName.Contains("User05")).ToTestCase(),
            My.Users().Where(u => u.UserName.Contains("User05")).ToTestCase(),
            Ss.Users().Where(u => u.UserName.Contains("User05")).ToTestCase(),
            sqlite: "SELECT * FROM \"users\" WHERE \"UserName\" LIKE '%' || @p0 || '%'",
            pg:     "SELECT * FROM \"users\" WHERE \"UserName\" LIKE '%' || @p0 || '%'",
            mysql:  "SELECT * FROM `users` WHERE `UserName` LIKE CONCAT('%', @p0, '%')",
            ss:     "SELECT * FROM [users] WHERE [UserName] LIKE '%' + @p0 + '%'");
    }

    [Test]
    public void Where_Contains_WithSelect()
    {
        AssertDialects(
            Lite.Users().Where(u => u.UserName.Contains("test")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToTestCase(),
            Pg.Users().Where(u => u.UserName.Contains("test")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToTestCase(),
            My.Users().Where(u => u.UserName.Contains("test")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToTestCase(),
            Ss.Users().Where(u => u.UserName.Contains("test")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToTestCase(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%' || @p0 || '%'",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%' || @p0 || '%'",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users` WHERE `UserName` LIKE CONCAT('%', @p0, '%')",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users] WHERE [UserName] LIKE '%' + @p0 + '%'");
    }

    [Test]
    public void Where_Contains_ChainedWithBoolean()
    {
        AssertDialects(
            Lite.Users().Where(u => u.UserName.Contains("admin")).Where(u => u.IsActive).ToTestCase(),
            Pg.Users().Where(u => u.UserName.Contains("admin")).Where(u => u.IsActive).ToTestCase(),
            My.Users().Where(u => u.UserName.Contains("admin")).Where(u => u.IsActive).ToTestCase(),
            Ss.Users().Where(u => u.UserName.Contains("admin")).Where(u => u.IsActive).ToTestCase(),
            sqlite: "SELECT * FROM \"users\" WHERE (\"UserName\" LIKE '%' || @p0 || '%') AND (\"IsActive\" = 1)",
            pg:     "SELECT * FROM \"users\" WHERE (\"UserName\" LIKE '%' || @p0 || '%') AND (\"IsActive\" = TRUE)",
            mysql:  "SELECT * FROM `users` WHERE (`UserName` LIKE CONCAT('%', @p0, '%')) AND (`IsActive` = 1)",
            ss:     "SELECT * FROM [users] WHERE ([UserName] LIKE '%' + @p0 + '%') AND ([IsActive] = 1)");
    }

    #endregion

    #region StartsWith

    [Test]
    public void Where_StartsWith_LiteralString()
    {
        AssertDialects(
            Lite.Users().Where(u => u.UserName.StartsWith("User0")).ToTestCase(),
            Pg.Users().Where(u => u.UserName.StartsWith("User0")).ToTestCase(),
            My.Users().Where(u => u.UserName.StartsWith("User0")).ToTestCase(),
            Ss.Users().Where(u => u.UserName.StartsWith("User0")).ToTestCase(),
            sqlite: "SELECT * FROM \"users\" WHERE \"UserName\" LIKE @p0 || '%'",
            pg:     "SELECT * FROM \"users\" WHERE \"UserName\" LIKE @p0 || '%'",
            mysql:  "SELECT * FROM `users` WHERE `UserName` LIKE CONCAT(@p0, '%')",
            ss:     "SELECT * FROM [users] WHERE [UserName] LIKE @p0 + '%'");
    }

    [Test]
    public void Where_StartsWith_WithSelect()
    {
        AssertDialects(
            Lite.Users().Where(u => u.UserName.StartsWith("A")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToTestCase(),
            Pg.Users().Where(u => u.UserName.StartsWith("A")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToTestCase(),
            My.Users().Where(u => u.UserName.StartsWith("A")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToTestCase(),
            Ss.Users().Where(u => u.UserName.StartsWith("A")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToTestCase(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE @p0 || '%'",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE @p0 || '%'",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users` WHERE `UserName` LIKE CONCAT(@p0, '%')",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users] WHERE [UserName] LIKE @p0 + '%'");
    }

    #endregion

    #region EndsWith

    [Test]
    public void Where_EndsWith_LiteralString()
    {
        AssertDialects(
            Lite.Users().Where(u => u.UserName.EndsWith("son")).ToTestCase(),
            Pg.Users().Where(u => u.UserName.EndsWith("son")).ToTestCase(),
            My.Users().Where(u => u.UserName.EndsWith("son")).ToTestCase(),
            Ss.Users().Where(u => u.UserName.EndsWith("son")).ToTestCase(),
            sqlite: "SELECT * FROM \"users\" WHERE \"UserName\" LIKE '%' || @p0",
            pg:     "SELECT * FROM \"users\" WHERE \"UserName\" LIKE '%' || @p0",
            mysql:  "SELECT * FROM `users` WHERE `UserName` LIKE CONCAT('%', @p0)",
            ss:     "SELECT * FROM [users] WHERE [UserName] LIKE '%' + @p0");
    }

    [Test]
    public void Where_EndsWith_WithSelect()
    {
        AssertDialects(
            Lite.Users().Where(u => u.UserName.EndsWith("z")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToTestCase(),
            Pg.Users().Where(u => u.UserName.EndsWith("z")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToTestCase(),
            My.Users().Where(u => u.UserName.EndsWith("z")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToTestCase(),
            Ss.Users().Where(u => u.UserName.EndsWith("z")).Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive }).ToTestCase(),
            sqlite: "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%' || @p0",
            pg:     "SELECT \"UserId\", \"UserName\", \"IsActive\" FROM \"users\" WHERE \"UserName\" LIKE '%' || @p0",
            mysql:  "SELECT `UserId`, `UserName`, `IsActive` FROM `users` WHERE `UserName` LIKE CONCAT('%', @p0)",
            ss:     "SELECT [UserId], [UserName], [IsActive] FROM [users] WHERE [UserName] LIKE '%' + @p0");
    }

    #endregion

    #region Nullable String Column

    [Test]
    public void Where_Contains_NullableColumn()
    {
        AssertDialects(
            Lite.Users().Where(u => u.Email!.Contains("@example")).ToTestCase(),
            Pg.Users().Where(u => u.Email!.Contains("@example")).ToTestCase(),
            My.Users().Where(u => u.Email!.Contains("@example")).ToTestCase(),
            Ss.Users().Where(u => u.Email!.Contains("@example")).ToTestCase(),
            sqlite: "SELECT * FROM \"users\" WHERE \"Email\" LIKE '%' || @p0 || '%'",
            pg:     "SELECT * FROM \"users\" WHERE \"Email\" LIKE '%' || @p0 || '%'",
            mysql:  "SELECT * FROM `users` WHERE `Email` LIKE CONCAT('%', @p0, '%')",
            ss:     "SELECT * FROM [users] WHERE [Email] LIKE '%' + @p0 + '%'");
    }

    #endregion

    #region Combined String Ops

    [Test]
    public void Where_Contains_And_StartsWith_Chained()
    {
        AssertDialects(
            Lite.Users().Where(u => u.UserName.Contains("er")).Where(u => u.UserName.StartsWith("Us")).ToTestCase(),
            Pg.Users().Where(u => u.UserName.Contains("er")).Where(u => u.UserName.StartsWith("Us")).ToTestCase(),
            My.Users().Where(u => u.UserName.Contains("er")).Where(u => u.UserName.StartsWith("Us")).ToTestCase(),
            Ss.Users().Where(u => u.UserName.Contains("er")).Where(u => u.UserName.StartsWith("Us")).ToTestCase(),
            sqlite: "SELECT * FROM \"users\" WHERE (\"UserName\" LIKE '%' || @p0 || '%') AND (\"UserName\" LIKE @p1 || '%')",
            pg:     "SELECT * FROM \"users\" WHERE (\"UserName\" LIKE '%' || @p0 || '%') AND (\"UserName\" LIKE @p1 || '%')",
            mysql:  "SELECT * FROM `users` WHERE (`UserName` LIKE CONCAT('%', @p0, '%')) AND (`UserName` LIKE CONCAT(@p1, '%'))",
            ss:     "SELECT * FROM [users] WHERE ([UserName] LIKE '%' + @p0 + '%') AND ([UserName] LIKE @p1 + '%')");
    }

    #endregion
}
