using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


[TestFixture]
internal class CrossDialectMiscTests : CrossDialectTestBase
{
    #region String: ToLower

    [Test]
    public void Where_ToLower()
    {
        AssertDialects(
            Lite.Users().Where(u => u.UserName.ToLower() == "john").ToDiagnostics(),
            Pg.Users().Where(u => u.UserName.ToLower() == "john").ToDiagnostics(),
            My.Users().Where(u => u.UserName.ToLower() == "john").ToDiagnostics(),
            Ss.Users().Where(u => u.UserName.ToLower() == "john").ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE LOWER(\"UserName\") = @p0",
            pg:     "SELECT * FROM \"users\" WHERE LOWER(\"UserName\") = $1",
            mysql:  "SELECT * FROM `users` WHERE LOWER(`UserName`) = ?",
            ss:     "SELECT * FROM [users] WHERE LOWER([UserName]) = @p0");
    }

    #endregion

    #region String: ToUpper

    [Test]
    public void Where_ToUpper()
    {
        AssertDialects(
            Lite.Users().Where(u => u.UserName.ToUpper() == "JOHN").ToDiagnostics(),
            Pg.Users().Where(u => u.UserName.ToUpper() == "JOHN").ToDiagnostics(),
            My.Users().Where(u => u.UserName.ToUpper() == "JOHN").ToDiagnostics(),
            Ss.Users().Where(u => u.UserName.ToUpper() == "JOHN").ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE UPPER(\"UserName\") = @p0",
            pg:     "SELECT * FROM \"users\" WHERE UPPER(\"UserName\") = $1",
            mysql:  "SELECT * FROM `users` WHERE UPPER(`UserName`) = ?",
            ss:     "SELECT * FROM [users] WHERE UPPER([UserName]) = @p0");
    }

    #endregion

    #region String: Trim

    [Test]
    public void Where_Trim()
    {
        AssertDialects(
            Lite.Users().Where(u => u.UserName.Trim() == "john").ToDiagnostics(),
            Pg.Users().Where(u => u.UserName.Trim() == "john").ToDiagnostics(),
            My.Users().Where(u => u.UserName.Trim() == "john").ToDiagnostics(),
            Ss.Users().Where(u => u.UserName.Trim() == "john").ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE TRIM(\"UserName\") = @p0",
            pg:     "SELECT * FROM \"users\" WHERE TRIM(\"UserName\") = $1",
            mysql:  "SELECT * FROM `users` WHERE TRIM(`UserName`) = ?",
            ss:     "SELECT * FROM [users] WHERE TRIM([UserName]) = @p0");
    }

    #endregion

    #region Sql.Raw with column reference

    [Test]
    public void Where_SqlRaw_WithColumnReference()
    {
        AssertDialects(
            Lite.Users().Where(u => Sql.Raw<bool>("custom_func({0})", u.UserId)).ToDiagnostics(),
            Pg.Users().Where(u => Sql.Raw<bool>("custom_func({0})", u.UserId)).ToDiagnostics(),
            My.Users().Where(u => Sql.Raw<bool>("custom_func({0})", u.UserId)).ToDiagnostics(),
            Ss.Users().Where(u => Sql.Raw<bool>("custom_func({0})", u.UserId)).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE custom_func(\"UserId\")",
            pg:     "SELECT * FROM \"users\" WHERE custom_func(\"UserId\")",
            mysql:  "SELECT * FROM `users` WHERE custom_func(`UserId`)",
            ss:     "SELECT * FROM [users] WHERE custom_func([UserId])");
    }

    [Test]
    public void Where_SqlRaw_WithMultipleColumnReferences()
    {
        AssertDialects(
            Lite.Users().Where(u => Sql.Raw<bool>("check_cols({0}, {1})", u.UserId, u.IsActive)).ToDiagnostics(),
            Pg.Users().Where(u => Sql.Raw<bool>("check_cols({0}, {1})", u.UserId, u.IsActive)).ToDiagnostics(),
            My.Users().Where(u => Sql.Raw<bool>("check_cols({0}, {1})", u.UserId, u.IsActive)).ToDiagnostics(),
            Ss.Users().Where(u => Sql.Raw<bool>("check_cols({0}, {1})", u.UserId, u.IsActive)).ToDiagnostics(),
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
            Lite.Users().Where(u => Sql.Raw<bool>("CONTAINS({0}, {1})", u.UserName, searchTerm)).ToDiagnostics(),
            Pg.Users().Where(u => Sql.Raw<bool>("CONTAINS({0}, {1})", u.UserName, searchTerm)).ToDiagnostics(),
            My.Users().Where(u => Sql.Raw<bool>("CONTAINS({0}, {1})", u.UserName, searchTerm)).ToDiagnostics(),
            Ss.Users().Where(u => Sql.Raw<bool>("CONTAINS({0}, {1})", u.UserName, searchTerm)).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE CONTAINS(\"UserName\", @p0)",
            pg:     "SELECT * FROM \"users\" WHERE CONTAINS(\"UserName\", $1)",
            mysql:  "SELECT * FROM `users` WHERE CONTAINS(`UserName`, ?)",
            ss:     "SELECT * FROM [users] WHERE CONTAINS([UserName], @p0)");
    }

    [Test]
    public void Where_SqlRaw_WithLiteralParameter()
    {
        AssertDialects(
            Lite.Users().Where(u => Sql.Raw<bool>("status_check({0}, {1})", u.UserName, 42)).ToDiagnostics(),
            Pg.Users().Where(u => Sql.Raw<bool>("status_check({0}, {1})", u.UserName, 42)).ToDiagnostics(),
            My.Users().Where(u => Sql.Raw<bool>("status_check({0}, {1})", u.UserName, 42)).ToDiagnostics(),
            Ss.Users().Where(u => Sql.Raw<bool>("status_check({0}, {1})", u.UserName, 42)).ToDiagnostics(),
            sqlite: "SELECT * FROM \"users\" WHERE status_check(\"UserName\", 42)",
            pg:     "SELECT * FROM \"users\" WHERE status_check(\"UserName\", 42)",
            mysql:  "SELECT * FROM `users` WHERE status_check(`UserName`, 42)",
            ss:     "SELECT * FROM [users] WHERE status_check([UserName], 42)");
    }

    #endregion
}
