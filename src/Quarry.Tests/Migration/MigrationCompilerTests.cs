using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Quarry.Tool.Schema;

namespace Quarry.Tests.Migration;

/// <summary>
/// Tests for the CLI's migration recompilation seam (the second emit-then-recompile seam
/// audited under #313): a migration that exists but fails to recompile must throw rather
/// than return null, which previously let migrate script emit an incomplete SQL script.
/// </summary>
public class MigrationCompilerTests
{
    private static CSharpCompilation CreateCompilation(params string[] sources)
    {
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Quarry.MigrationAttribute).Assembly.Location),
        };
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var name in new[] { "System.Runtime.dll", "netstandard.dll" })
        {
            var path = Path.Combine(runtimeDir, name);
            if (File.Exists(path))
                references.Add(MetadataReference.CreateFromFile(path));
        }

        return CSharpCompilation.Create(
            "MigrationCompilerTestProject",
            sources.Select(s => CSharpSyntaxTree.ParseText(s)),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    [Test]
    public void CompileAndBuildSql_NoMigrationWithTargetVersion_ReturnsNull()
    {
        var compilation = CreateCompilation("""
            namespace TestApp;
            public class NotAMigration { }
            """);

        var result = MigrationCompiler.CompileAndBuildSql(compilation, 1, Quarry.SqlDialect.SQLite);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void CompileAndBuildSql_MigrationWithoutUpgradeMethod_Throws()
    {
        var compilation = CreateCompilation("""
            using Quarry;

            namespace TestApp.Migrations;

            [Migration(Version = 4, Name = "NoUpgrade")]
            internal static partial class M0004_NoUpgrade
            {
            }
            """);

        Assert.That(
            () => MigrationCompiler.CompileAndBuildSql(compilation, 4, Quarry.SqlDialect.SQLite),
            Throws.InvalidOperationException.With.Message.Contains("no Upgrade() method"));
    }

    [Test]
    public void CompileAndBuildSql_UpgradeFailsToCompile_Throws()
    {
        var compilation = CreateCompilation("""
            using Quarry;
            using Quarry.Migration;

            namespace TestApp.Migrations;

            [Migration(Version = 5, Name = "BadCompile")]
            internal static partial class M0005_BadCompile
            {
                internal static void Upgrade(MigrationBuilder builder)
                {
                    builder.NoSuchMethodOnTheBuilder();
                }
            }
            """);

        Assert.That(
            () => MigrationCompiler.CompileAndBuildSql(compilation, 5, Quarry.SqlDialect.SQLite),
            Throws.InvalidOperationException.With.Message.Contains("failed to recompile"));
    }

    [Test]
    public void CompileAndBuildSql_ValidMigration_ReturnsSql()
    {
        var compilation = CreateCompilation("""
            using Quarry;
            using Quarry.Migration;

            namespace TestApp.Migrations;

            [Migration(Version = 6, Name = "AddTable")]
            internal static partial class M0006_AddTable
            {
                internal static void Upgrade(MigrationBuilder builder)
                {
                    builder.CreateTable("widgets", null, t =>
                    {
                        t.Column("name", c => c.ClrType("string").NotNull());
                    });
                }
            }
            """);

        var sql = MigrationCompiler.CompileAndBuildSql(compilation, 6, Quarry.SqlDialect.SQLite);

        Assert.That(sql, Is.Not.Null);
        Assert.That(sql, Does.Contain("widgets"));
    }
}
