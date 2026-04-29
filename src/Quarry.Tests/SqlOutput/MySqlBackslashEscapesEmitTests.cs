using Quarry.Tests.Samples;
using My = Quarry.Tests.Samples.My;
using MyDefault = Quarry.Tests.Samples.MyDefault;

namespace Quarry.Tests.SqlOutput;

/// <summary>
/// SQL-shape tests for the MySqlBackslashEscapes mode-flag carried on
/// <c>QuarryContextAttribute</c>. Verifies the renderer emits ANSI-form
/// (single backslash) when the flag is <c>false</c> and doubled-form
/// when <c>true</c> — both for the LIKE-pattern literal and the ESCAPE clause.
/// Inspects emitted SQL via <c>ToDiagnostics()</c>; no live DB required.
/// </summary>
[TestFixture]
public class MySqlBackslashEscapesEmitTests
{
    private MockDbConnection _connection = null!;
    private My.MyDb _myFalse = null!;
    private MyDefault.MyDefaultDb _myTrue = null!;

    [SetUp]
    public void Setup()
    {
        _connection = new MockDbConnection();
        _myFalse = new My.MyDb(_connection);
        _myTrue = new MyDefault.MyDefaultDb(_connection);
    }

    [TearDown]
    public void TearDown()
    {
        _myFalse.Dispose();
        _myTrue.Dispose();
        _connection.Dispose();
    }

    [Test]
    public void Contains_Underscore_BackslashEscapesFalse_EmitsAnsiForm()
    {
        // MyDb has MySqlBackslashEscapes = false — matches MySqlTestContainer's
        // NO_BACKSLASH_ESCAPES sql_mode. Emit is the original ANSI single-backslash form.
        var sql = _myFalse.Users()
            .Where(u => u.UserName.Contains("user_name"))
            .Select(u => (u.UserId, u.UserName))
            .ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo(
            "SELECT `UserId`, `UserName` FROM `users` WHERE `UserName` LIKE '%user\\_name%' ESCAPE '\\'"));
    }

    [Test]
    public void Contains_Underscore_BackslashEscapesTrue_EmitsDoubledForm()
    {
        // MyDefaultDb has MySqlBackslashEscapes = true — matches stock MySQL where
        // backslash IS a string-literal escape character. Pattern and ESCAPE clause
        // both get doubled backslashes so MySQL parses them down to a single backslash.
        var sql = _myTrue.Users()
            .Where(u => u.UserName.Contains("user_name"))
            .Select(u => (u.UserId, u.UserName))
            .ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo(
            "SELECT `UserId`, `UserName` FROM `users` WHERE `UserName` LIKE '%user\\\\_name%' ESCAPE '\\\\'"));
    }

    [Test]
    public void Contains_Percent_BackslashEscapesFalse_EmitsAnsiForm()
    {
        var sql = _myFalse.Users()
            .Where(u => u.UserName.Contains("50%"))
            .Select(u => u.UserId)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("LIKE '%50\\%%' ESCAPE '\\'"));
    }

    [Test]
    public void Contains_Percent_BackslashEscapesTrue_EmitsDoubledForm()
    {
        var sql = _myTrue.Users()
            .Where(u => u.UserName.Contains("50%"))
            .Select(u => u.UserId)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("LIKE '%50\\\\%%' ESCAPE '\\\\'"));
    }

    [Test]
    public void Contains_Backslash_BackslashEscapesTrue_DoublesIt()
    {
        // A literal backslash in user input becomes two backslashes after
        // EscapeLikeMetaChars (escape the backslash itself), then four after
        // doubling for MySQL+default: SQL '%foo\\\\bar%' parses to %foo\\bar%
        // and matches a literal backslash because ESCAPE '\\' (parsed: \) treats
        // the second backslash in '\\\\' as a literal.
        var sql = _myTrue.Users()
            .Where(u => u.UserName.Contains("a\\b"))
            .Select(u => u.UserId)
            .ToDiagnostics().Sql;

        // EscapeLikeMetaChars produces "a\\b" (4 chars: a, \, \, b in C#).
        // MySQL+default doubling produces SQL "%a\\\\\\\\b%" (8 backslashes in C# = 4 in SQL).
        Assert.That(sql, Does.Contain("LIKE '%a\\\\\\\\b%' ESCAPE '\\\\'"));
    }

    [Test]
    public void Contains_NoMetacharacters_NoEscapeClause()
    {
        // Plain string with no LIKE-meta characters → no ESCAPE clause needed
        // on either side, regardless of MySqlBackslashEscapes.
        var sqlFalse = _myFalse.Users()
            .Where(u => u.UserName.Contains("john"))
            .Select(u => u.UserId)
            .ToDiagnostics().Sql;
        var sqlTrue = _myTrue.Users()
            .Where(u => u.UserName.Contains("john"))
            .Select(u => u.UserId)
            .ToDiagnostics().Sql;

        Assert.Multiple(() =>
        {
            Assert.That(sqlFalse, Does.Not.Contain("ESCAPE"));
            Assert.That(sqlTrue, Does.Not.Contain("ESCAPE"));
        });
    }
}
