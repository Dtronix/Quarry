using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Quarry.Tool.Schema;

namespace Quarry.Tests.Migration;

/// <summary>
/// Tests for the CLI's snapshot recompilation seam: a snapshot that exists but fails
/// validation or compilation must throw (loud abort) rather than return null, which
/// previously degraded migrate add/diff to an empty-baseline diff (#313).
/// </summary>
public class SnapshotCompilerTests
{
    private static CSharpCompilation CreateCompilation(params string[] sources)
    {
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Quarry.MigrationSnapshotAttribute).Assembly.Location),
        };
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var name in new[] { "System.Runtime.dll", "netstandard.dll" })
        {
            var path = Path.Combine(runtimeDir, name);
            if (File.Exists(path))
                references.Add(MetadataReference.CreateFromFile(path));
        }

        return CSharpCompilation.Create(
            "SnapshotCompilerTestProject",
            sources.Select(s => CSharpSyntaxTree.ParseText(s)),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    [Test]
    public void CompileAndBuild_NoSnapshotWithTargetVersion_ReturnsNull()
    {
        var compilation = CreateCompilation("""
            namespace TestApp;
            public class NotASnapshot { }
            """);

        var result = SnapshotCompiler.CompileAndBuild(compilation, 1);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void CompileAndBuild_DisallowedMethodCall_ThrowsNamingTheMethod()
    {
        var compilation = CreateCompilation("""
            using System;
            using Quarry;
            using Quarry.Migration;

            namespace TestApp.Migrations;

            [MigrationSnapshot(Version = 2, Name = "Bad", Timestamp = "2026-01-01T00:00:00Z")]
            internal static partial class S0002_Bad
            {
                internal static SchemaSnapshot Build()
                {
                    System.IO.File.Delete("important.txt");
                    var builder = new SchemaSnapshotBuilder().SetVersion(2).SetName("Bad");
                    return builder.Build();
                }
            }
            """);

        Assert.That(
            () => SnapshotCompiler.CompileAndBuild(compilation, 2),
            Throws.InvalidOperationException.With.Message.Contains("'Delete'"));
    }

    [Test]
    public void CompileAndBuild_SnapshotFailsToCompile_Throws()
    {
        // Every method name is whitelisted, but Length() gets a string — the recompile
        // must fail loudly instead of silently returning null.
        var compilation = CreateCompilation("""
            using System;
            using Quarry;
            using Quarry.Migration;

            namespace TestApp.Migrations;

            [MigrationSnapshot(Version = 3, Name = "BadCompile", Timestamp = "2026-01-01T00:00:00Z")]
            internal static partial class S0003_BadCompile
            {
                internal static SchemaSnapshot Build()
                {
                    var builder = new SchemaSnapshotBuilder()
                        .SetVersion(3)
                        .SetName("BadCompile");
                    builder.AddTable(t => t
                        .Name("users")
                        .AddColumn(c => c.Name("id").ClrType("int").Length("not an int")));
                    return builder.Build();
                }
            }
            """);

        Assert.That(
            () => SnapshotCompiler.CompileAndBuild(compilation, 3),
            Throws.InvalidOperationException.With.Message.Contains("failed to recompile"));
    }

    [Test]
    public void FindDisallowedMethodCall_CleanBuilderBody_ReturnsNull()
    {
        var tree = CSharpSyntaxTree.ParseText("""
            internal static class S0001_Clean
            {
                internal static object Build()
                {
                    var builder = new SchemaSnapshotBuilder()
                        .SetVersion(1)
                        .SetName("Clean")
                        .SetTimestamp(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
                    builder.AddTable(t => t
                        .Name("users")
                        .CharacterSet("utf8mb4")
                        .AddColumn(c => c.Name("id").ClrType("int").PrimaryKey().Identity())
                        .AddColumn(c => c.Name("status").ClrType("string").DefaultValue("'active'").Collation("nocase")));
                    return builder.Build();
                }
            }
            """);

        Assert.That(SnapshotCompiler.FindDisallowedMethodCall(tree), Is.Null);
    }

    [Test]
    public void FindDisallowedMethodCall_DisallowedInvocation_ReturnsMethodName()
    {
        var tree = CSharpSyntaxTree.ParseText("""
            internal static class S0001_Evil
            {
                internal static object Build()
                {
                    System.Diagnostics.Process.Start("evil.exe");
                    return null;
                }
            }
            """);

        Assert.That(SnapshotCompiler.FindDisallowedMethodCall(tree), Is.EqualTo("Start"));
    }
}
