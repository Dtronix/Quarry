using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;

#pragma warning disable QRY001

[TestFixture]
internal class CrossDialectMiscTests : CrossDialectTestBase
{
    #region String: ToLower

    [Test]
    public void Where_ToLower()
    {
        AssertDialects(
            Lite.Users().Where(u => u.UserName.ToLower() == "john").ToTestCase(),
            Pg.Users().Where(u => u.UserName.ToLower() == "john").ToTestCase(),
            My.Users().Where(u => u.UserName.ToLower() == "john").ToTestCase(),
            Ss.Users().Where(u => u.UserName.ToLower() == "john").ToTestCase(),
            sqlite: "SELECT * FROM \"users\" WHERE (LOWER(\"UserName\") = @p0)",
            pg:     "SELECT * FROM \"users\" WHERE (LOWER(\"UserName\") = @p0)",
            mysql:  "SELECT * FROM `users` WHERE (LOWER(`UserName`) = @p0)",
            ss:     "SELECT * FROM [users] WHERE (LOWER([UserName]) = @p0)");
    }

    #endregion

    #region String: ToUpper

    [Test]
    public void Where_ToUpper()
    {
        AssertDialects(
            Lite.Users().Where(u => u.UserName.ToUpper() == "JOHN").ToTestCase(),
            Pg.Users().Where(u => u.UserName.ToUpper() == "JOHN").ToTestCase(),
            My.Users().Where(u => u.UserName.ToUpper() == "JOHN").ToTestCase(),
            Ss.Users().Where(u => u.UserName.ToUpper() == "JOHN").ToTestCase(),
            sqlite: "SELECT * FROM \"users\" WHERE (UPPER(\"UserName\") = @p0)",
            pg:     "SELECT * FROM \"users\" WHERE (UPPER(\"UserName\") = @p0)",
            mysql:  "SELECT * FROM `users` WHERE (UPPER(`UserName`) = @p0)",
            ss:     "SELECT * FROM [users] WHERE (UPPER([UserName]) = @p0)");
    }

    #endregion

    #region String: Trim

    [Test]
    public void Where_Trim()
    {
        AssertDialects(
            Lite.Users().Where(u => u.UserName.Trim() == "john").ToTestCase(),
            Pg.Users().Where(u => u.UserName.Trim() == "john").ToTestCase(),
            My.Users().Where(u => u.UserName.Trim() == "john").ToTestCase(),
            Ss.Users().Where(u => u.UserName.Trim() == "john").ToTestCase(),
            sqlite: "SELECT * FROM \"users\" WHERE (TRIM(\"UserName\") = @p0)",
            pg:     "SELECT * FROM \"users\" WHERE (TRIM(\"UserName\") = @p0)",
            mysql:  "SELECT * FROM `users` WHERE (TRIM(`UserName`) = @p0)",
            ss:     "SELECT * FROM [users] WHERE (TRIM([UserName]) = @p0)");
    }

    #endregion

    #region Sql.Raw with column reference

    [Test]
    public void Where_SqlRaw_WithColumnReference()
    {
        AssertDialects(
            Lite.Users().Where(u => Sql.Raw<bool>("custom_func(@p0)", u.UserId)).ToTestCase(),
            Pg.Users().Where(u => Sql.Raw<bool>("custom_func(@p0)", u.UserId)).ToTestCase(),
            My.Users().Where(u => Sql.Raw<bool>("custom_func(@p0)", u.UserId)).ToTestCase(),
            Ss.Users().Where(u => Sql.Raw<bool>("custom_func(@p0)", u.UserId)).ToTestCase(),
            sqlite: "SELECT * FROM \"users\" WHERE custom_func(\"UserId\")",
            pg:     "SELECT * FROM \"users\" WHERE custom_func(\"UserId\")",
            mysql:  "SELECT * FROM `users` WHERE custom_func(`UserId`)",
            ss:     "SELECT * FROM [users] WHERE custom_func([UserId])");
    }

    [Test]
    public void Where_SqlRaw_WithMultipleColumnReferences()
    {
        AssertDialects(
            Lite.Users().Where(u => Sql.Raw<bool>("check_cols(@p0, @p1)", u.UserId, u.IsActive)).ToTestCase(),
            Pg.Users().Where(u => Sql.Raw<bool>("check_cols(@p0, @p1)", u.UserId, u.IsActive)).ToTestCase(),
            My.Users().Where(u => Sql.Raw<bool>("check_cols(@p0, @p1)", u.UserId, u.IsActive)).ToTestCase(),
            Ss.Users().Where(u => Sql.Raw<bool>("check_cols(@p0, @p1)", u.UserId, u.IsActive)).ToTestCase(),
            sqlite: "SELECT * FROM \"users\" WHERE check_cols(\"UserId\", \"IsActive\")",
            pg:     "SELECT * FROM \"users\" WHERE check_cols(\"UserId\", \"IsActive\")",
            mysql:  "SELECT * FROM `users` WHERE check_cols(`UserId`, `IsActive`)",
            ss:     "SELECT * FROM [users] WHERE check_cols([UserId], [IsActive])");
    }

    [Test]
    public void Where_SqlRaw_WithCapturedVariable()
    {
        var searchTerm = "john";
        AssertDialects(
            Lite.Users().Where(u => Sql.Raw<bool>("CONTAINS(@p0, @p1)", u.UserName, searchTerm)).ToTestCase(),
            Pg.Users().Where(u => Sql.Raw<bool>("CONTAINS(@p0, @p1)", u.UserName, searchTerm)).ToTestCase(),
            My.Users().Where(u => Sql.Raw<bool>("CONTAINS(@p0, @p1)", u.UserName, searchTerm)).ToTestCase(),
            Ss.Users().Where(u => Sql.Raw<bool>("CONTAINS(@p0, @p1)", u.UserName, searchTerm)).ToTestCase(),
            sqlite: "SELECT * FROM \"users\" WHERE CONTAINS(\"UserName\", @p0)",
            pg:     "SELECT * FROM \"users\" WHERE CONTAINS(\"UserName\", @p0)",
            mysql:  "SELECT * FROM `users` WHERE CONTAINS(`UserName`, @p0)",
            ss:     "SELECT * FROM [users] WHERE CONTAINS([UserName], @p0)");
    }

    [Test]
    public void Where_SqlRaw_WithLiteralParameter()
    {
        AssertDialects(
            Lite.Users().Where(u => Sql.Raw<bool>("status_check(@p0, @p1)", u.UserName, 42)).ToTestCase(),
            Pg.Users().Where(u => Sql.Raw<bool>("status_check(@p0, @p1)", u.UserName, 42)).ToTestCase(),
            My.Users().Where(u => Sql.Raw<bool>("status_check(@p0, @p1)", u.UserName, 42)).ToTestCase(),
            Ss.Users().Where(u => Sql.Raw<bool>("status_check(@p0, @p1)", u.UserName, 42)).ToTestCase(),
            sqlite: "SELECT * FROM \"users\" WHERE status_check(\"UserName\", 42)",
            pg:     "SELECT * FROM \"users\" WHERE status_check(\"UserName\", 42)",
            mysql:  "SELECT * FROM `users` WHERE status_check(`UserName`, 42)",
            ss:     "SELECT * FROM [users] WHERE status_check([UserName], 42)");
    }

    #endregion
}
