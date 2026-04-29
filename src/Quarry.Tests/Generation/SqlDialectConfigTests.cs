using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;
using GenSqlDialectConfig = Quarry.Generators.Sql.SqlDialectConfig;

namespace Quarry.Tests.Generation;

/// <summary>
/// Unit tests for the SqlDialectConfig record and its FromAttribute parsing.
/// </summary>
[TestFixture]
public class SqlDialectConfigTests
{
    [Test]
    public void Record_DefaultsMySqlBackslashEscapesTrue()
    {
        var config = new GenSqlDialectConfig(GenSqlDialect.MySQL);
        Assert.That(config.MySqlBackslashEscapes, Is.True);
    }

    [Test]
    public void Record_ValueEquality_OnDialectAndFlag()
    {
        var a = new GenSqlDialectConfig(GenSqlDialect.MySQL, MySqlBackslashEscapes: true);
        var b = new GenSqlDialectConfig(GenSqlDialect.MySQL, MySqlBackslashEscapes: true);
        var c = new GenSqlDialectConfig(GenSqlDialect.MySQL, MySqlBackslashEscapes: false);
        var d = new GenSqlDialectConfig(GenSqlDialect.PostgreSQL, MySqlBackslashEscapes: true);

        Assert.Multiple(() =>
        {
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a, Is.Not.EqualTo(c));
            Assert.That(a, Is.Not.EqualTo(d));
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        });
    }

    [Test]
    public void FromAttribute_DialectOnly_DefaultsMySqlBackslashEscapesTrue()
    {
        var attr = ParseContextAttribute("[Quarry.QuarryContext(Dialect = Quarry.SqlDialect.MySQL)]");
        var config = GenSqlDialectConfig.FromAttribute(attr);

        Assert.Multiple(() =>
        {
            Assert.That(config.Dialect, Is.EqualTo(GenSqlDialect.MySQL));
            Assert.That(config.MySqlBackslashEscapes, Is.True);
        });
    }

    [Test]
    public void FromAttribute_MySqlBackslashEscapesFalse_Honored()
    {
        var attr = ParseContextAttribute(
            "[Quarry.QuarryContext(Dialect = Quarry.SqlDialect.MySQL, MySqlBackslashEscapes = false)]");
        var config = GenSqlDialectConfig.FromAttribute(attr);

        Assert.Multiple(() =>
        {
            Assert.That(config.Dialect, Is.EqualTo(GenSqlDialect.MySQL));
            Assert.That(config.MySqlBackslashEscapes, Is.False);
        });
    }

    [Test]
    public void FromAttribute_NoNamedArgs_DefaultsToSqliteAndTrue()
    {
        var attr = ParseContextAttribute("[Quarry.QuarryContext]");
        var config = GenSqlDialectConfig.FromAttribute(attr);

        Assert.Multiple(() =>
        {
            Assert.That(config.Dialect, Is.EqualTo(GenSqlDialect.SQLite));
            Assert.That(config.MySqlBackslashEscapes, Is.True);
        });
    }

    private static AttributeData ParseContextAttribute(string attributeSourceLine)
    {
        var source = $@"
namespace Quarry
{{
    public enum SqlDialect {{ SQLite = 0, PostgreSQL = 1, MySQL = 2, SqlServer = 3 }}

    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class QuarryContextAttribute : System.Attribute
    {{
        public SqlDialect Dialect {{ get; set; }}
        public bool MySqlBackslashEscapes {{ get; set; }} = true;
        public string? Schema {{ get; set; }}
    }}
}}

namespace App
{{
    {attributeSourceLine}
    public class Db {{ }}
}}
";
        var tree = CSharpSyntaxTree.ParseText(source);
        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var compilation = CSharpCompilation.Create(
            "TestAttr",
            new[] { tree },
            new MetadataReference[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")),
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        var dbSymbol = compilation.GetTypeByMetadataName("App.Db")!;
        return dbSymbol.GetAttributes().Single();
    }
}
