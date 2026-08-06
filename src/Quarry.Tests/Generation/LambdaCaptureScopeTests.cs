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
            "System.Runtime.InteropServices.dll", "System.ComponentModel.Primitives.dll",
            "System.Data.Common.dll",
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

        // Assert on ALL generated-code errors, not just the original CS0103 symptom. This feature's
        // failure mode is "emits uncompilable C#", and narrowing to one diagnostic id let a duplicate
        // __ExtractThis_ member (CS0111) and an inaccessible return type (CS0122) pass unnoticed.
        //
        // Two ids are artifacts of compiling in isolation rather than defects: CS9137 (interceptors
        // are enabled by an MSBuild property this synthetic compilation has no way to set) and
        // CS1729 (QueryDiagnostics overload resolution, which succeeds in the real project). They
        // are excluded by id so that every other generated-code error still fails the test.
        var real = errors.Where(d => d.Id is not ("CS9137" or "CS1729")).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(real, Is.Empty,
                "generated interceptor does not compile: "
                + string.Join(" | ", real.Select(d => d.Id + ": " + d.GetMessage())));
            Assert.That(genDiags.Where(d => d.Id == "QRY032"), Is.Empty,
                "chain should be analyzable, not disqualified");
            Assert.That(code, Does.Contain("__ExtractVar_"),
                "captured variable should be read through an [UnsafeAccessor] extractor");
        });
    }

    /// <summary>Asserts the shape is rejected at build time, with a reason containing the given text.</summary>
    private static void AssertRejected(string body, string expectedReasonFragment)
    {
        var (code, _, genDiags) = Run(body);

        var qry032 = genDiags.Where(d => d.Id == "QRY032").ToArray();
        Assert.That(qry032, Is.Not.Empty, "shape should be disqualified at build time");
        Assert.That(qry032[0].GetMessage(), Does.Contain(expectedReasonFragment));
        Assert.That(code, Does.Not.Contain("__ExtractThis_"),
            "no hop accessor should be emitted for a rejected chain");
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

    /// <summary>`for`-declaration variable alone — its own per-iteration display class.</summary>
    [Test]
    public void ForDeclarationVariable() => AssertCaptureResolved(@"
public static class Q
{
    public static void Test(TestDbContext db, string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            _ = db.Users().Where(u => u.UserId > i).Select(u => u.UserId).ToDiagnostics();
        }
    }
}");

    /// <summary>`using`-statement variable captured by a clause.</summary>
    [Test]
    public void UsingStatementVariable() => AssertCaptureResolved(@"
public static class Q
{
    public static void Test(TestDbContext db)
    {
        using (var d = new System.IO.MemoryStream())
        {
            _ = db.Users().Where(u => u.UserId > (int)d.Length).Select(u => u.UserId).ToDiagnostics();
        }
    }
}");

    /// <summary>`switch`-section local captured by a clause.</summary>
    [Test]
    public void SwitchSectionLocal() => AssertCaptureResolved(@"
public static class Q
{
    public static void Test(TestDbContext db, int k)
    {
        switch (k)
        {
            case 1:
                var name = ""Alice"";
                _ = db.Users().Where(u => u.UserName == name).Select(u => u.UserId).ToDiagnostics();
                break;
            default:
                break;
        }
    }
}");

    /// <summary>
    /// `catch`-clause variable captured by a clause. The catch variable owns its own display class,
    /// so resolving it to the block enclosing the `try` mispredicted the ordinal — and, worse, made
    /// a catch-variable-plus-method-local clause look single-scope so the guard did not fire.
    /// </summary>
    [Test]
    public void CatchClauseVariable() => AssertCaptureResolved(@"
public static class Q
{
    public static void Test(TestDbContext db)
    {
        try { throw new InvalidOperationException(""Alice""); }
        catch (InvalidOperationException ex)
        {
            _ = db.Users().Where(u => u.UserName == ex.Message).Select(u => u.UserId).ToDiagnostics();
        }
    }
}");

    /// <summary>
    /// Two clauses on ONE chain, each mixing an instance field with a local. The `<>4__this` hop
    /// accessor is declared on the carrier, so naming it after the containing type emitted it twice
    /// with identical signatures (CS0111). It is named per clause instead.
    /// </summary>
    [Test]
    public void TwoClausesEachMixingFieldAndLocal() => AssertCaptureResolved(@"
public class Q
{
    private readonly int _min = 0;
    private readonly int _max = 99;

    public void Test(TestDbContext db)
    {
        var name = ""Alice"";
        var other = ""Bob"";
        _ = db.Users()
            .Where(u => u.UserId > _min && u.UserName == name)
            .Where(u => u.UserId < _max && u.UserName != other)
            .Select(u => u.UserId).ToDiagnostics();
    }
}");

    [Test]
    public void MultiScope_CatchVariableAndMethodLocalInOneClause() => AssertMultiScopeRejected(@"
public static class Q
{
    public static void Test(TestDbContext db)
    {
        var minId = 0;
        try { throw new InvalidOperationException(""Alice""); }
        catch (InvalidOperationException ex)
        {
            _ = db.Users().Where(u => u.UserName == ex.Message && u.UserId > minId)
                .Select(u => u.UserId).ToDiagnostics();
        }
    }
}");

    [Test]
    public void MultiScope_ForBodyLocalAndMethodLocalInOneClause() => AssertMultiScopeRejected(@"
public static class Q
{
    public static void Test(TestDbContext db, string[] names)
    {
        var minId = 0;
        for (int i = 0; i < names.Length; i++)
        {
            var name = names[i];
            _ = db.Users().Where(u => u.UserName == name && u.UserId > minId)
                .Select(u => u.UserId).ToDiagnostics();
        }
    }
}");

    // ─────────── containing types the <>4__this hop cannot name ───────────

    /// <summary>
    /// The hop accessor must return the containing type as a real type name, which is impossible for
    /// a generic type (no type parameters in scope on a file-scoped carrier) — CS0305 if emitted.
    /// </summary>
    [Test]
    public void GenericContainingType_WithFieldAndLocal_IsRejected() => AssertRejected(@"
public class Repo<T>
{
    private readonly int _min = 0;
    public void Test(TestDbContext db)
    {
        var name = ""Alice"";
        _ = db.Users().Where(u => u.UserId > _min && u.UserName == name)
            .Select(u => u.UserId).ToDiagnostics();
    }
}", "generic or not accessible");

    /// <summary>Same, for a type the generated file cannot see at all — CS0122 if emitted.</summary>
    [Test]
    public void InaccessibleContainingType_WithFieldAndLocal_IsRejected() => AssertRejected(@"
public class Outer
{
    private class Inner
    {
        private readonly int _min = 0;
        public void Test(TestDbContext db)
        {
            var name = ""Alice"";
            _ = db.Users().Where(u => u.UserId > _min && u.UserName == name)
                .Select(u => u.UserId).ToDiagnostics();
        }
    }
}", "generic or not accessible");
}
