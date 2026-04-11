using Quarry.Migration;

namespace Quarry.Migration.Tests;

[TestFixture]
public class InterfaceTests
{
    [Test]
    public void DapperConversionDiagnostic_Implements_IConversionDiagnostic()
    {
        var diagnostic = new DapperConversionDiagnostic("Warning", "test message");

        IConversionDiagnostic iface = diagnostic;

        Assert.That(iface.Severity, Is.EqualTo("Warning"));
        Assert.That(iface.Message, Is.EqualTo("test message"));
    }

    [Test]
    public void EfCoreConversionDiagnostic_Implements_IConversionDiagnostic()
    {
        var diagnostic = new EfCoreConversionDiagnostic("Error", "ef core error");

        IConversionDiagnostic iface = diagnostic;

        Assert.That(iface.Severity, Is.EqualTo("Error"));
        Assert.That(iface.Message, Is.EqualTo("ef core error"));
    }

    [Test]
    public void AdoNetConversionDiagnostic_Implements_IConversionDiagnostic()
    {
        var diagnostic = new AdoNetConversionDiagnostic("Info", "ado info");

        IConversionDiagnostic iface = diagnostic;

        Assert.That(iface.Severity, Is.EqualTo("Info"));
        Assert.That(iface.Message, Is.EqualTo("ado info"));
    }

    [Test]
    public void SqlKataConversionDiagnostic_Implements_IConversionDiagnostic()
    {
        var diagnostic = new SqlKataConversionDiagnostic("Warning", "kata warning");

        IConversionDiagnostic iface = diagnostic;

        Assert.That(iface.Severity, Is.EqualTo("Warning"));
        Assert.That(iface.Message, Is.EqualTo("kata warning"));
    }

    [Test]
    public void DapperConversionEntry_Implements_IConversionEntry()
    {
        var diag = new DapperConversionDiagnostic("Warning", "w");
        var entry = new DapperConversionEntry(
            "file.cs", 10, "QueryAsync", "User", "SELECT * FROM users",
            "db.Users()", new[] { diag });

        IConversionEntry iface = entry;

        Assert.That(iface.FilePath, Is.EqualTo("file.cs"));
        Assert.That(iface.Line, Is.EqualTo(10));
        Assert.That(iface.ChainCode, Is.EqualTo("db.Users()"));
        Assert.That(iface.IsConvertible, Is.True);
        Assert.That(iface.HasWarnings, Is.True);
        Assert.That(iface.OriginalSource, Is.EqualTo("SELECT * FROM users"));
        Assert.That(iface.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(iface.Diagnostics[0].Severity, Is.EqualTo("Warning"));
    }

    [Test]
    public void EfCoreConversionEntry_Implements_IConversionEntry()
    {
        var diag = new EfCoreConversionDiagnostic("Error", "e");
        var entry = new EfCoreConversionEntry(
            "ctx.cs", 20, "ToListAsync", "Order", "db.Orders.ToListAsync()",
            null, new[] { diag });

        IConversionEntry iface = entry;

        Assert.That(iface.FilePath, Is.EqualTo("ctx.cs"));
        Assert.That(iface.Line, Is.EqualTo(20));
        Assert.That(iface.ChainCode, Is.Null);
        Assert.That(iface.IsConvertible, Is.False);
        Assert.That(iface.HasWarnings, Is.False);
        Assert.That(iface.OriginalSource, Is.EqualTo("db.Orders.ToListAsync()"));
        Assert.That(iface.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(iface.Diagnostics[0].Message, Is.EqualTo("e"));
    }

    [Test]
    public void AdoNetConversionEntry_Implements_IConversionEntry()
    {
        var entry = new AdoNetConversionEntry(
            "dal.cs", 5, "ExecuteReader", "SELECT 1",
            "db.Items()", new AdoNetConversionDiagnostic[0]);

        IConversionEntry iface = entry;

        Assert.That(iface.FilePath, Is.EqualTo("dal.cs"));
        Assert.That(iface.Line, Is.EqualTo(5));
        Assert.That(iface.ChainCode, Is.EqualTo("db.Items()"));
        Assert.That(iface.IsConvertible, Is.True);
        Assert.That(iface.HasWarnings, Is.False);
        Assert.That(iface.OriginalSource, Is.EqualTo("SELECT 1"));
        Assert.That(iface.Diagnostics, Is.Empty);
    }

    [Test]
    public void SqlKataConversionEntry_Implements_IConversionEntry()
    {
        var diag = new SqlKataConversionDiagnostic("Warning", "w");
        var entry = new SqlKataConversionEntry(
            "repo.cs", 42, "users", "query.From(\"users\")",
            "db.Users().ExecuteFetchAllAsync()", new[] { diag });

        IConversionEntry iface = entry;

        Assert.That(iface.FilePath, Is.EqualTo("repo.cs"));
        Assert.That(iface.Line, Is.EqualTo(42));
        Assert.That(iface.ChainCode, Is.EqualTo("db.Users().ExecuteFetchAllAsync()"));
        Assert.That(iface.IsConvertible, Is.True);
        Assert.That(iface.HasWarnings, Is.True);
        Assert.That(iface.OriginalSource, Is.EqualTo("query.From(\"users\")"));
        Assert.That(iface.Diagnostics, Has.Count.EqualTo(1));
    }

    [Test]
    public void DapperConversionEntry_SuggestionOnly_IsNotConvertible_ViaInterface()
    {
        var entry = new DapperConversionEntry(
            "file.cs", 1, "QueryAsync", null, "INSERT INTO t",
            "// suggestion", new DapperConversionDiagnostic[0], isSuggestionOnly: true);

        IConversionEntry iface = entry;

        Assert.That(iface.IsConvertible, Is.False);
        Assert.That(iface.ChainCode, Is.EqualTo("// suggestion"));
    }
}
