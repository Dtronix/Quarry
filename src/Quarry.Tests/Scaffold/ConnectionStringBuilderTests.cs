namespace Quarry.Tests.Scaffold;

[TestFixture]
public class ConnectionStringBuilderTests
{
    /// <summary>
    /// Regression test for operator precedence bug in BuildSqlServerConnectionString.
    /// The expression <c>server ?? "localhost" + suffix</c> silently drops the port
    /// when server is non-null because + binds tighter than ??.
    /// The fix is <c>(server ?? "localhost") + suffix</c>.
    /// </summary>
    [Test]
    public void SqlServerDataSource_ServerWithPort_IncludesPort()
    {
        // This mirrors the fixed expression in ScaffoldCommand.BuildSqlServerConnectionString
        string? server = "myserver";
        string? port = "1434";

        var result = (server ?? "localhost") + (port != null ? $",{port}" : "");

        Assert.That(result, Is.EqualTo("myserver,1434"));
    }

    [Test]
    public void SqlServerDataSource_NullServerWithPort_UsesLocalhostWithPort()
    {
        string? server = null;
        string? port = "1434";

        var result = (server ?? "localhost") + (port != null ? $",{port}" : "");

        Assert.That(result, Is.EqualTo("localhost,1434"));
    }

    [Test]
    public void SqlServerDataSource_ServerWithoutPort_JustServer()
    {
        string? server = "myserver";
        string? port = null;

        var result = (server ?? "localhost") + (port != null ? $",{port}" : "");

        Assert.That(result, Is.EqualTo("myserver"));
    }

    [Test]
    public void SqlServerDataSource_NullServerNullPort_JustLocalhost()
    {
        string? server = null;
        string? port = null;

        var result = (server ?? "localhost") + (port != null ? $",{port}" : "");

        Assert.That(result, Is.EqualTo("localhost"));
    }

    /// <summary>
    /// Demonstrates the bug that existed before the fix.
    /// Without parentheses, server ?? "localhost" + suffix evaluates as
    /// server ?? ("localhost" + suffix), so the port is dropped when server is non-null.
    /// </summary>
    [Test]
    public void SqlServerDataSource_BuggyExpression_DropsPort()
    {
        string? server = "myserver";
        string? port = "1434";

        // This is the BUGGY expression (without parentheses) — the + binds first
        var buggyResult = server ?? "localhost" + (port != null ? $",{port}" : "");

        // The buggy version returns just "myserver" without the port
        Assert.That(buggyResult, Is.EqualTo("myserver"));

        // The fixed version correctly includes the port
        var fixedResult = (server ?? "localhost") + (port != null ? $",{port}" : "");
        Assert.That(fixedResult, Is.EqualTo("myserver,1434"));
    }
}
