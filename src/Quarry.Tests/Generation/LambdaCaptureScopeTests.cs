using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using Quarry.Generators;

namespace Quarry.Tests.Generation;

/// <summary>
/// Codegen-level coverage for closure-capture resolution (issue #333).
/// <para>
/// Two things are asserted per shape: that the emitted interceptor <b>compiles</b> (no CS0103 — the
/// original symptom was a bare captured-local reference in generated code), and that an
/// <c>__ExtractVar_</c> accessor was actually emitted. Both matter: before the fix the site was skipped
/// entirely, so no accessor was emitted AND the raw local name was inlined.
/// </para>
/// <para>
/// <b>Enclosing lambdas must be ARGUMENTS TO AN INVOCATION</b> (<c>Select(...)</c>, <c>Task.Run(...)</c>).
/// <see cref="Quarry.Generators.Parsing.UsageSiteDiscovery"/>'s lambda-capture disqualifier skips those
/// but rejects any other enclosing lambda, so writing a shape as <c>new Func&lt;...&gt;(lambda)</c> trips
/// QRY032 and silently masks whatever this file is trying to prove.
/// </para>
/// <para>
/// Compilation success alone is NOT sufficient evidence — a wrong display-class prediction still
/// compiles and then throws <c>MissingFieldException</c>/<c>InvalidCastException</c> at execution.
/// <see cref="LambdaCaptureExecutionTests"/> covers that half.
/// </para>
/// </summary>
[TestFixture]
public class LambdaCaptureScopeTests
{
    private static readonly string QuarryCoreAssemblyPath = typeof(Schema).Assembly.Location;

    private const string Prelude = @"
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Quarry;
namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName { get; }
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}
";

    private static CSharpCompilation CreateCompilation(params string[] sources)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var syntaxTrees = sources.Select(s => CSharpSyntaxTree.ParseText(s, parseOptions)).ToList();

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(QuarryCoreAssemblyPath),
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Data.IDbConnection).Assembly.Location),
        };

        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        foreach (var dll in new[]
        {
            "System.Runtime.dll", "System.Collections.dll", "System.Linq.dll",
            "System.Linq.Expressions.dll", "netstandard.dll",
            "System.Threading.Tasks.dll", "System.Threading.dll", "System.Console.dll",
            "System.Runtime.InteropServices.dll",
        })
        {
            references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, dll)));
        }

        return CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));
    }

    private static (string Interceptors, Diagnostic[] OutputErrors, Diagnostic[] GeneratorDiagnostics) Run(string body)
    {
        var generator = new QuarryGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { generator.AsSourceGenerator() });
        driver = driver.RunGeneratorsAndUpdateCompilation(
            CreateCompilation(Prelude + body), out var outputCompilation, out var genDiags);
        var result = driver.GetRunResult();

        var tree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));

        var errors = outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

        return (tree?.GetText().ToString() ?? "<none>", errors, genDiags.ToArray());
    }

    /// <summary>Asserts the shape yields a compiling interceptor that extracts its captures.</summary>
    private static void AssertCaptureResolved(string body)
    {
        var (code, errors, genDiags) = Run(body);

        var cs0103 = errors.Where(d => d.Id == "CS0103").ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(cs0103, Is.Empty,
                "generated interceptor referenced a captured local that is not in scope: "
                + string.Join(" | ", cs0103.Select(d => d.GetMessage())));
            Assert.That(genDiags.Where(d => d.Id == "QRY032"), Is.Empty,
                "chain should be analyzable, not disqualified");
            Assert.That(code, Does.Contain("__ExtractVar_"),
                "captured variable should be read through an [UnsafeAccessor] extractor");
        });
    }

    /// <summary>Asserts the shape is rejected at build time with the multi-scope reason.</summary>
    private static void AssertMultiScopeRejected(string body)
    {
        var (_, _, genDiags) = Run(body);

        var qry032 = genDiags.Where(d => d.Id == "QRY032").ToArray();
        Assert.That(qry032, Is.Not.Empty, "multi-scope capture should be disqualified at build time");
        Assert.That(qry032[0].GetMessage(), Does.Contain("different closure scopes"),
            "diagnostic should name the actual problem");
        Assert.That(qry032[0].GetMessage(), Does.Contain(".Where("),
            "diagnostic should point at the split-into-separate-clauses workaround");
    }

    // ─────────────────── shapes that must resolve ───────────────────

    [Test]
    public void MethodLocal_NoLambda() => AssertCaptureResolved(@"
public static class Q
{
    public static void Test(TestDbContext db)
    {
        var name = ""Worker1"";
        _ = db.Users().Where(u => u.UserName == name).Select(u => u.UserId).ToDiagnostics();
    }
}");

    [Test]
    public void InsideSingleLambda() => AssertCaptureResolved(@"
public static class Q
{
    public static void Test(TestDbContext db, IEnumerable<int> src)
    {
        _ = src.Select(i =>
        {
            var name = ""Worker"" + i;
            return db.Users().Where(u => u.UserName == name).Select(u => u.UserId).ToDiagnostics();
        }).ToList();
    }
}");

    [Test]
    public void InsideAsyncLambda() => AssertCaptureResolved(@"
public static class Q
{
    public static Task Test(TestDbContext db)
    {
        return Task.Run(() =>
        {
            var name = ""Worker1"";
            _ = db.Users().Where(u => u.UserName == name).Select(u => u.UserId).ToDiagnostics();
        });
    }
}");

    /// <summary>The shape reported in issue #333.</summary>
    [Test]
    public void InsideNestedLambdas_LocalInInner() => AssertCaptureResolved(@"
public static class Q
{
    public static void Test(TestDbContext db, IEnumerable<int> src)
    {
        _ = src.Select(i => Task.Run(() =>
        {
            var name = ""Worker"" + i;
            _ = db.Users().Where(u => u.UserName == name).Select(u => u.UserId).ToDiagnostics();
        })).ToList();
    }
}");

    [Test]
    public void InsideNestedLambdas_NoOuterCapture() => AssertCaptureResolved(@"
public static class Q
{
    public static void Test(TestDbContext db, IEnumerable<int> src)
    {
        _ = src.Select(i => Task.Run(() =>
        {
            var name = ""Worker"";
            _ = db.Users().Where(u => u.UserName == name).Select(u => u.UserId).ToDiagnostics();
        })).ToList();
    }
}");

    [Test]
    public void InsideNestedLambdas_CapturesOuterParameter() => AssertCaptureResolved(@"
public static class Q
{
    public static void Test(TestDbContext db, IEnumerable<string> src)
    {
        _ = src.Select(s => Task.Run(() =>
        {
            _ = db.Users().Where(u => u.UserName == s).Select(u => u.UserId).ToDiagnostics();
        })).ToList();
    }
}");

    /// <summary>The issue's `Update().Set(...)` variant, which uses a different extraction path.</summary>
    [Test]
    public void InsideNestedLambdas_UpdateSet() => AssertCaptureResolved(@"
public static class Q
{
    public static void Test(TestDbContext db, IEnumerable<int> src)
    {
        _ = src.Select(i => Task.Run(async () =>
        {
            var name = ""Worker"" + i;
            await db.Users().Update().Set(u => u.UserName = name).Where(u => u.UserId == 1).ExecuteNonQueryAsync();
        })).ToList();
    }
}");

    [Test]
    public void LocalFunctionInsideLambda() => AssertCaptureResolved(@"
public static class Q
{
    public static void Test(TestDbContext db, IEnumerable<int> src)
    {
        _ = src.Select(i =>
        {
            void Inner()
            {
                var name = ""Worker"" + i;
                _ = db.Users().Where(u => u.UserName == name).Select(u => u.UserId).ToDiagnostics();
            }
            Inner();
            return i;
        }).ToList();
    }
}");

    /// <summary>
    /// Separate clauses, each capturing from its own scope. Works because a loop variable now resolves
    /// to the loop's own display class rather than the enclosing method scope.
    /// </summary>
    [Test]
    public void SeparateClauses_LoopVariableAndMethodLocal() => AssertCaptureResolved(@"
public static class Q
{
    public static void Test(TestDbContext db, string[] names)
    {
        var minId = 0;
        foreach (var name in names)
        {
            _ = db.Users().Where(u => u.UserName == name).Where(u => u.UserId > minId)
                .Select(u => u.UserId).ToDiagnostics();
        }
    }
}");

    // ─────────────────── shapes that must be rejected ───────────────────

    [Test]
    public void MultiScope_LoopVariableAndMethodLocalInOneClause() => AssertMultiScopeRejected(@"
public static class Q
{
    public static void Test(TestDbContext db, string[] names)
    {
        var minId = 0;
        foreach (var name in names)
        {
            _ = db.Users().Where(u => u.UserName == name && u.UserId > minId)
                .Select(u => u.UserId).ToDiagnostics();
        }
    }
}");

    [Test]
    public void MultiScope_IfBlockLocalAndMethodLocalInOneClause() => AssertMultiScopeRejected(@"
public static class Q
{
    public static void Test(TestDbContext db, bool flag)
    {
        var minId = 0;
        if (flag)
        {
            var name = ""Alice"";
            _ = db.Users().Where(u => u.UserName == name && u.UserId > minId)
                .Select(u => u.UserId).ToDiagnostics();
        }
    }
}");

    [Test]
    public void MultiScope_NestedLambdasCapturingBothLevels() => AssertMultiScopeRejected(@"
public static class Q
{
    public static void Test(TestDbContext db, IEnumerable<int> src)
    {
        _ = src.Select((x, i) => Task.Run(() =>
        {
            var name = ""Alice"";
            _ = db.Users().Where(u => u.UserName == name && u.UserId != i)
                .Select(u => u.UserId).ToDiagnostics();
        })).ToList();
    }
}");
}
