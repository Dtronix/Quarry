using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using Quarry.Generators;

namespace Quarry.Tests.Generation;

/// <summary>
/// Tests that terminal parameter binding, logging, and local extraction are
/// mask-aware for conditional WHERE chains.
///
/// Issue #84: The terminal emitter unconditionally binds ALL carrier parameter fields
/// to the DbCommand regardless of which mask variant is active. These tests assert
/// the correct behavior: conditional parameters should be gated by mask checks so
/// that only active parameters are bound, logged, and extracted.
///
/// All tests in this fixture are expected to FAIL against the current code,
/// demonstrating the bug described in issue #84.
/// </summary>
[TestFixture]
public class MaskAwareTerminalBindingTests
{
    private static readonly string QuarryCoreAssemblyPath = typeof(Schema).Assembly.Location;
    private static readonly string SystemRuntimeAssemblyPath = typeof(object).Assembly.Location;

    private const string SharedSchema = @"
using Quarry;
using System;
using System.Threading.Tasks;
namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
    public Col<string?> Email { get; }
    public Col<bool> IsActive { get; }
    public Col<int> Age => Default(0);
}

public class OrderSchema : Schema
{
    public static string Table => ""orders"";
    public Key<int> OrderId => Identity();
    public Col<int> UserId { get; }
    public Col<decimal> Total { get; }
}
";

    private const string ContextDecl = @"
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
}
";

    private static CSharpCompilation CreateCompilation(params string[] sources)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var syntaxTrees = sources.Select(s => CSharpSyntaxTree.ParseText(s, parseOptions)).ToList();

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(QuarryCoreAssemblyPath),
            MetadataReference.CreateFromFile(SystemRuntimeAssemblyPath),
            MetadataReference.CreateFromFile(typeof(System.Data.IDbConnection).Assembly.Location),
        };

        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Collections.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.Expressions.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll")));

        return CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));
    }

    private static GeneratorDriverRunResult RunGenerator(CSharpCompilation compilation)
    {
        var generator = new QuarryGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() });
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private static string GenerateInterceptors(string queryCode)
    {
        var source = SharedSchema + ContextDecl + queryCode;
        var compilation = CreateCompilation(source);
        var result = RunGenerator(compilation);

        var interceptorsTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains(".Interceptors.") && t.FilePath.EndsWith(".g.cs"));
        Assert.That(interceptorsTree, Is.Not.Null, "Should generate interceptors file");
        return interceptorsTree!.GetText().ToString();
    }

    /// <summary>
    /// Extracts the section of generated code between two markers (inclusive of start line).
    /// Used to isolate the terminal execution method body for targeted assertions.
    /// </summary>
    private static string ExtractSection(string code, string startContains, string endContains)
    {
        var lines = code.Split('\n');
        int startIdx = -1, endIdx = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (startIdx < 0 && lines[i].Contains(startContains))
                startIdx = i;
            if (startIdx >= 0 && lines[i].Contains(endContains) && i > startIdx)
            {
                endIdx = i;
                break;
            }
        }
        if (startIdx < 0 || endIdx < 0) return "";
        return string.Join("\n", lines.Skip(startIdx).Take(endIdx - startIdx + 1));
    }

    /// <summary>
    /// Checks whether a given line index is inside a mask-gated block.
    /// Scans backward tracking brace depth so that a closed mask block
    /// (where the closing '}' appears before the target line) is not
    /// mistaken for an enclosing block.
    /// </summary>
    private static bool IsInsideMaskGatedBlock(string[] lines, int lineIdx, int maxLookback = 20)
    {
        int braceDepth = 0;
        for (int i = lineIdx - 1; i >= Math.Max(0, lineIdx - maxLookback); i--)
        {
            var trimmed = lines[i].Trim();

            // Track braces — scanning backward, so '}' increases depth, '{' decreases
            if (trimmed == "}")
                braceDepth++;
            else if (trimmed == "{")
            {
                if (braceDepth > 0)
                    braceDepth--;
                else
                {
                    // This '{' opens a block that encloses our target line.
                    // Check the line above it for a mask check.
                    if (i > 0 && lines[i - 1].Contains("__c.Mask &"))
                        return true;
                }
            }
        }
        return false;
    }

    // ─────────────────────────────────────────────────────────────────
    //  Test 1: Single conditional WHERE with captured param —
    //  parameter binding to DbCommand should be mask-gated
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Terminal_SingleConditionalWhereWithParam_CommandBindingIsMaskGated()
    {
        var code = GenerateInterceptors(@"
public class Svc
{
    private readonly TestDbContext _db;
    public Svc(TestDbContext db) { _db = db; }
    public async Task Run(int minAge, bool applyFilter)
    {
        var q = _db.Users().Select(u => u);
        if (applyFilter)
            q = q.Where(u => u.Age > minAge);
        await q.ExecuteFetchAllAsync();
    }
}");
        // Precondition: carrier-optimized with mask
        Assert.That(code, Does.Contain("file sealed class Chain_"), "Should emit carrier class");
        Assert.That(code, Does.Contain("Mask |="), "Should set mask bit");

        // The terminal method creates DbCommand and binds parameters.
        // For conditional params, CreateParameter + Parameters.Add should be inside
        // a mask-gated block: if ((__c.Mask & ...) != 0) { ... }
        //
        // Current bug: __p0 is created and added unconditionally even when the
        // WHERE clause (bit 0) is inactive.
        var terminalBody = ExtractSection(code, "var __cmd = __c.Ctx.Connection.CreateCommand()", "return QueryExecutor.");
        Assert.That(terminalBody, Is.Not.Empty, "Should have terminal execution body with command binding");

        // The conditional parameter @p0 should only be bound when bit 0 is active.
        // Look for a mask check within the parameter binding region.
        Assert.That(terminalBody, Does.Contain("__c.Mask &"),
            "Terminal should check mask before binding conditional parameter @p0 to DbCommand");
    }

    // ─────────────────────────────────────────────────────────────────
    //  Test 2: Two conditional WHEREs with captured params —
    //  each param's binding should be independently mask-gated
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Terminal_TwoConditionalWheresWithParams_EachBindingIndependentlyGated()
    {
        var code = GenerateInterceptors(@"
public class Svc
{
    private readonly TestDbContext _db;
    public Svc(TestDbContext db) { _db = db; }
    public async Task Run(int minAge, string? name, bool filterAge, bool filterName)
    {
        var q = _db.Users().Select(u => u);
        if (filterAge)
            q = q.Where(u => u.Age > minAge);
        if (filterName)
            q = q.Where(u => u.UserName == name);
        await q.ExecuteFetchAllAsync();
    }
}");
        Assert.That(code, Does.Contain("file sealed class Chain_"), "Should emit carrier class");
        Assert.That(code, Does.Contain("Mask |="), "Should set mask bits");

        var terminalBody = ExtractSection(code, "var __cmd = __c.Ctx.Connection.CreateCommand()", "return QueryExecutor.");
        Assert.That(terminalBody, Is.Not.Empty, "Should have terminal execution body");

        // With two independent conditional WHEREs (bit 0 and bit 1),
        // the terminal should have TWO separate mask checks — one for each parameter group.
        var maskCheckCount = Regex.Matches(terminalBody, @"__c\.Mask\s*&").Count;
        Assert.That(maskCheckCount, Is.GreaterThanOrEqualTo(2),
            "Terminal should have at least 2 mask checks (one per conditional WHERE) in the binding region. " +
            "Currently all parameters are bound unconditionally.");
    }

    // ─────────────────────────────────────────────────────────────────
    //  Test 3: Parameter logging should be mask-gated for conditional params
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Terminal_ConditionalWhereWithParam_LoggingIsMaskGated()
    {
        var code = GenerateInterceptors(@"
public class Svc
{
    private readonly TestDbContext _db;
    public Svc(TestDbContext db) { _db = db; }
    public async Task Run(int minAge, bool applyFilter)
    {
        var q = _db.Users().Select(u => u);
        if (applyFilter)
            q = q.Where(u => u.Age > minAge);
        await q.ExecuteFetchAllAsync();
    }
}");
        // The inline parameter logging section should not log inactive conditional params.
        // Currently ParameterLog.Bound is emitted for ALL params unconditionally.
        var loggingSection = ExtractSection(code, "ParameterLog.CategoryName", "ParameterLog.Bound");
        if (string.IsNullOrEmpty(loggingSection))
        {
            // Try broader extraction — logging may span more lines
            loggingSection = code;
        }

        // Find ParameterLog.Bound calls and check they are mask-gated
        var lines = code.Split('\n');
        var boundLogLines = lines
            .Select((line, idx) => (line, idx))
            .Where(x => x.line.Contains("ParameterLog.Bound") && x.line.Contains("__opId, 0"))
            .ToList();

        Assert.That(boundLogLines, Has.Count.GreaterThan(0),
            "Should have parameter logging for @p0");

        foreach (var (line, idx) in boundLogLines)
        {
            Assert.That(IsInsideMaskGatedBlock(lines, idx), Is.True,
                $"ParameterLog.Bound for conditional param @p0 at line {idx + 1} should be " +
                "inside a mask-gated block. Currently all params are logged unconditionally.");
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  Test 4: Execution terminal inlines value expressions (no __pVal locals)
    //  and the inlined binding is mask-gated
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Terminal_ConditionalWhereWithParam_LocalExtractionIsMaskGated()
    {
        var code = GenerateInterceptors(@"
public class Svc
{
    private readonly TestDbContext _db;
    public Svc(TestDbContext db) { _db = db; }
    public async Task Run(int minAge, bool applyFilter)
    {
        var q = _db.Users().Select(u => u);
        if (applyFilter)
            q = q.Where(u => u.Age > minAge);
        await q.ExecuteFetchAllAsync();
    }
}");
        // After the fix, the execution terminal inlines value expressions directly
        // into __p0.Value = (object?)__c.P0 ?? DBNull.Value instead of using __pVal0
        // intermediate locals. The inlined binding should be inside a mask-gated block.
        var terminalBody = ExtractSection(code, "var __cmd = __c.Ctx.Connection.CreateCommand()", "return QueryExecutor.");
        Assert.That(terminalBody, Is.Not.Empty, "Should have terminal execution body");

        // __pVal0 should NOT appear in the execution terminal (values are inlined)
        Assert.That(terminalBody, Does.Not.Contain("__pVal0"),
            "Execution terminal should not use __pVal0 locals — value expressions are inlined");

        // The inlined parameter binding (__p0.Value = ...) should be mask-gated
        var lines = terminalBody.Split('\n');
        var bindLines = lines
            .Select((line, idx) => (line, idx))
            .Where(x => x.line.Contains("Parameters.Add(__p0)"))
            .ToList();

        Assert.That(bindLines, Has.Count.GreaterThan(0),
            "Should have Parameters.Add(__p0) in terminal body");

        foreach (var (line, idx) in bindLines)
        {
            Assert.That(IsInsideMaskGatedBlock(lines, idx), Is.True,
                $"Parameters.Add(__p0) at line {idx + 1} should be inside a mask-gated block");
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  Test 5: Conditional WHERE + pagination — pagination always bound,
    //  conditional WHERE params gated by mask
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Terminal_ConditionalWhereWithPagination_PaginationAlwaysBound_WhereParamsGated()
    {
        var code = GenerateInterceptors(@"
public class Svc
{
    private readonly TestDbContext _db;
    public Svc(TestDbContext db) { _db = db; }
    public async Task Run(int minAge, int offset, bool applyFilter)
    {
        var q = _db.Users().Select(u => u).Limit(20).Offset(offset);
        if (applyFilter)
            q = q.Where(u => u.Age > minAge);
        await q.ExecuteFetchAllAsync();
    }
}");
        Assert.That(code, Does.Contain("file sealed class Chain_"), "Should emit carrier class");
        Assert.That(code, Does.Contain("Mask |="), "Should set mask bit");

        var terminalBody = ExtractSection(code, "var __cmd = __c.Ctx.Connection.CreateCommand()", "return QueryExecutor.");
        Assert.That(terminalBody, Is.Not.Empty, "Should have terminal execution body");

        // Pagination parameters (Limit/Offset) should be bound unconditionally.
        // Conditional WHERE param (@p0) should be mask-gated.
        // The terminal body should have a mask check for the WHERE param
        // but NOT wrap the pagination binding in a mask check.
        Assert.That(terminalBody, Does.Contain("__c.Mask &"),
            "Terminal should mask-gate the conditional WHERE parameter binding. " +
            "Currently all params including conditional ones are bound unconditionally.");

        // Pagination binding (__pO for offset) should remain unconditional
        Assert.That(terminalBody, Does.Contain("__pO.ParameterName"),
            "Pagination offset parameter should be bound");
    }

    // ─────────────────────────────────────────────────────────────────
    //  Test 6: Two conditional WHEREs with reversed param order —
    //  verifies the "worse scenario" from issue #84
    //  where first conditional has an enum-like type and second has strings
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Terminal_TwoConditionalWheresReversedOrder_NoGhostParametersBound()
    {
        var code = GenerateInterceptors(@"
public class Svc
{
    private readonly TestDbContext _db;
    public Svc(TestDbContext db) { _db = db; }
    public async Task Run(bool filterBool, string? nameFilter, bool applyBool, bool applyName)
    {
        var q = _db.Users().Select(u => u);
        if (applyBool)
            q = q.Where(u => u.IsActive == filterBool);
        if (applyName)
            q = q.Where(u => u.UserName == nameFilter);
        await q.ExecuteFetchAllAsync();
    }
}");
        Assert.That(code, Does.Contain("file sealed class Chain_"), "Should emit carrier class");

        var terminalBody = ExtractSection(code, "var __cmd = __c.Ctx.Connection.CreateCommand()", "return QueryExecutor.");
        Assert.That(terminalBody, Is.Not.Empty, "Should have terminal execution body");

        // When only the second WHERE is active (mask=2, bit 1 set, bit 0 unset),
        // only @p1 should be bound. @p0 should NOT be bound because bit 0 is inactive.
        // Without mask-gating, @p0 is bound with a ghost value (default bool = false).
        var lines = terminalBody.Split('\n');
        var p0BindLines = lines
            .Select((line, idx) => (line, idx))
            .Where(x => x.line.Contains("__p0.ParameterName") || x.line.Contains("Parameters.Add(__p0)"))
            .ToList();

        Assert.That(p0BindLines, Has.Count.GreaterThan(0),
            "Should have binding code for @p0");

        // Each @p0 binding should be inside a mask-gated block for bit 0
        foreach (var (line, idx) in p0BindLines)
        {
            Assert.That(IsInsideMaskGatedBlock(lines, idx), Is.True,
                $"@p0 binding at line {idx + 1} should be mask-gated to prevent ghost " +
                "parameter values when bit 0 (first conditional WHERE) is inactive.");
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  Test 7: Multi-param same bit — two captured vars in one conditional
    //  WHERE should share a single mask gate
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Terminal_MultiParamSameBit_SingleMaskGate()
    {
        var code = GenerateInterceptors(@"
public class Svc
{
    private readonly TestDbContext _db;
    public Svc(TestDbContext db) { _db = db; }
    public async Task Run(int minAge, int maxAge, bool applyFilter)
    {
        var q = _db.Users().Select(u => u);
        if (applyFilter)
            q = q.Where(u => u.Age > minAge && u.Age < maxAge);
        await q.ExecuteFetchAllAsync();
    }
}");
        Assert.That(code, Does.Contain("file sealed class Chain_"), "Should emit carrier class");
        Assert.That(code, Does.Contain("Mask |="), "Should set mask bit");

        var terminalBody = ExtractSection(code, "var __cmd = __c.Ctx.Connection.CreateCommand()", "return QueryExecutor.");
        Assert.That(terminalBody, Is.Not.Empty, "Should have terminal execution body");

        // Both @p0 and @p1 come from the same conditional WHERE (same bit).
        // They should be under ONE mask check, not two separate checks.
        var maskCheckCount = Regex.Matches(terminalBody, @"__c\.Mask\s*&").Count;
        Assert.That(maskCheckCount, Is.EqualTo(1),
            $"Two params from the same conditional WHERE should share one mask gate, got {maskCheckCount}");

        // Both params should be inside the mask-gated block
        var lines = terminalBody.Split('\n');
        foreach (var paramName in new[] { "__p0", "__p1" })
        {
            var addLines = lines
                .Select((line, idx) => (line, idx))
                .Where(x => x.line.Contains($"Parameters.Add({paramName})"))
                .ToList();
            Assert.That(addLines, Has.Count.GreaterThan(0), $"Should bind {paramName}");
            foreach (var (line, idx) in addLines)
            {
                Assert.That(IsInsideMaskGatedBlock(lines, idx), Is.True,
                    $"{paramName} should be inside mask-gated block");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  Test 8: Unconditional WHERE before conditional WHERE —
    //  unconditional param must NOT be gated, conditional param must be
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Terminal_UnconditionalBeforeConditional_OnlyConditionalGated()
    {
        var code = GenerateInterceptors(@"
public class Svc
{
    private readonly TestDbContext _db;
    public Svc(TestDbContext db) { _db = db; }
    public async Task Run(string name, int minAge, bool filterAge)
    {
        var q = _db.Users().Select(u => u)
            .Where(u => u.UserName == name);
        if (filterAge)
            q = q.Where(u => u.Age > minAge);
        await q.ExecuteFetchAllAsync();
    }
}");
        Assert.That(code, Does.Contain("file sealed class Chain_"), "Should emit carrier class");
        Assert.That(code, Does.Contain("Mask |="), "Should set mask bit");

        var terminalBody = ExtractSection(code, "var __cmd = __c.Ctx.Connection.CreateCommand()", "return QueryExecutor.");
        Assert.That(terminalBody, Is.Not.Empty, "Should have terminal execution body");

        var lines = terminalBody.Split('\n');

        // @p0 (unconditional WHERE) should NOT be inside a mask gate
        var p0AddLines = lines
            .Select((line, idx) => (line, idx))
            .Where(x => x.line.Contains("Parameters.Add(__p0)"))
            .ToList();
        Assert.That(p0AddLines, Has.Count.GreaterThan(0), "Should bind @p0");
        foreach (var (line, idx) in p0AddLines)
        {
            Assert.That(IsInsideMaskGatedBlock(lines, idx), Is.False,
                "Unconditional @p0 should NOT be inside a mask-gated block");
        }

        // @p1 (conditional WHERE) SHOULD be inside a mask gate
        var p1AddLines = lines
            .Select((line, idx) => (line, idx))
            .Where(x => x.line.Contains("Parameters.Add(__p1)"))
            .ToList();
        Assert.That(p1AddLines, Has.Count.GreaterThan(0), "Should bind @p1");
        foreach (var (line, idx) in p1AddLines)
        {
            Assert.That(IsInsideMaskGatedBlock(lines, idx), Is.True,
                "Conditional @p1 should be inside a mask-gated block");
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  Test 9: Full interleave — unconditional → conditional → unconditional
    //  Exercises block open/close transitions
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Terminal_InterleavedUnconditionalConditional_CorrectGating()
    {
        var code = GenerateInterceptors(@"
public class Svc
{
    private readonly TestDbContext _db;
    public Svc(TestDbContext db) { _db = db; }
    public async Task Run(string name, int minAge, string email, bool filterAge)
    {
        var q = _db.Users().Select(u => u)
            .Where(u => u.UserName == name);
        if (filterAge)
            q = q.Where(u => u.Age > minAge);
        q = q.Where(u => u.Email == email);
        await q.ExecuteFetchAllAsync();
    }
}");
        Assert.That(code, Does.Contain("file sealed class Chain_"), "Should emit carrier class");

        var terminalBody = ExtractSection(code, "var __cmd = __c.Ctx.Connection.CreateCommand()", "return QueryExecutor.");
        Assert.That(terminalBody, Is.Not.Empty, "Should have terminal execution body");

        var lines = terminalBody.Split('\n');

        // @p0 (unconditional, first WHERE) — NOT gated
        var p0AddLines = lines.Select((l, i) => (l, i)).Where(x => x.l.Contains("Parameters.Add(__p0)")).ToList();
        Assert.That(p0AddLines, Has.Count.GreaterThan(0), "Should bind @p0");
        foreach (var (_, idx) in p0AddLines)
            Assert.That(IsInsideMaskGatedBlock(lines, idx), Is.False,
                "Unconditional @p0 should NOT be mask-gated");

        // @p1 (conditional, bit 0) — gated
        var p1AddLines = lines.Select((l, i) => (l, i)).Where(x => x.l.Contains("Parameters.Add(__p1)")).ToList();
        Assert.That(p1AddLines, Has.Count.GreaterThan(0), "Should bind @p1");
        foreach (var (_, idx) in p1AddLines)
            Assert.That(IsInsideMaskGatedBlock(lines, idx), Is.True,
                "Conditional @p1 should be mask-gated");

        // @p2 (unconditional, third WHERE) — NOT gated
        var p2AddLines = lines.Select((l, i) => (l, i)).Where(x => x.l.Contains("Parameters.Add(__p2)")).ToList();
        Assert.That(p2AddLines, Has.Count.GreaterThan(0), "Should bind @p2");
        foreach (var (_, idx) in p2AddLines)
            Assert.That(IsInsideMaskGatedBlock(lines, idx), Is.False,
                "Unconditional @p2 should NOT be mask-gated (after conditional block closes)");
    }

    // ─────────────────────────────────────────────────────────────────
    //  Test 10: Three conditional WHEREs with params — 3 independent mask gates
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Terminal_ThreeConditionalWheres_ThreeIndependentMaskGates()
    {
        var code = GenerateInterceptors(@"
public class Svc
{
    private readonly TestDbContext _db;
    public Svc(TestDbContext db) { _db = db; }
    public async Task Run(int minAge, string? name, string? email, bool fa, bool fn, bool fe)
    {
        var q = _db.Users().Select(u => u);
        if (fa)
            q = q.Where(u => u.Age > minAge);
        if (fn)
            q = q.Where(u => u.UserName == name);
        if (fe)
            q = q.Where(u => u.Email == email);
        await q.ExecuteFetchAllAsync();
    }
}");
        Assert.That(code, Does.Contain("file sealed class Chain_"), "Should emit carrier class");

        var terminalBody = ExtractSection(code, "var __cmd = __c.Ctx.Connection.CreateCommand()", "return QueryExecutor.");
        Assert.That(terminalBody, Is.Not.Empty, "Should have terminal execution body");

        // Three conditional WHEREs with params → 3 mask checks in binding region
        var maskCheckCount = Regex.Matches(terminalBody, @"__c\.Mask\s*&").Count;
        Assert.That(maskCheckCount, Is.GreaterThanOrEqualTo(3),
            $"Should have at least 3 mask checks for 3 conditional WHEREs, got {maskCheckCount}");

        // Each param should be independently gated
        var lines = terminalBody.Split('\n');
        foreach (var paramName in new[] { "__p0", "__p1", "__p2" })
        {
            var addLines = lines
                .Select((line, idx) => (line, idx))
                .Where(x => x.line.Contains($"Parameters.Add({paramName})"))
                .ToList();
            Assert.That(addLines, Has.Count.GreaterThan(0), $"Should bind {paramName}");
            foreach (var (_, idx) in addLines)
                Assert.That(IsInsideMaskGatedBlock(lines, idx), Is.True,
                    $"{paramName} should be inside a mask-gated block");
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  Test 11: Conditional WHERE with nullable string param —
    //  different value expression and logging path
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Terminal_ConditionalWhereNullableParam_LoggingAndBindingGated()
    {
        var code = GenerateInterceptors(@"
public class Svc
{
    private readonly TestDbContext _db;
    public Svc(TestDbContext db) { _db = db; }
    public async Task Run(string? email, bool filterEmail)
    {
        var q = _db.Users().Select(u => u);
        if (filterEmail)
            q = q.Where(u => u.Email == email);
        await q.ExecuteFetchAllAsync();
    }
}");
        Assert.That(code, Does.Contain("file sealed class Chain_"), "Should emit carrier class");

        var terminalBody = ExtractSection(code, "var __cmd = __c.Ctx.Connection.CreateCommand()", "return QueryExecutor.");
        Assert.That(terminalBody, Is.Not.Empty, "Should have terminal execution body");

        // Binding should be mask-gated
        Assert.That(terminalBody, Does.Contain("__c.Mask &"),
            "Nullable string conditional param binding should be mask-gated");

        // Logging should also be mask-gated for the nullable param.
        // Nullable string uses ?.ToString() ?? "null" in logging.
        var lines = code.Split('\n');
        var logLines = lines
            .Select((line, idx) => (line, idx))
            .Where(x => x.line.Contains("ParameterLog.Bound") && x.line.Contains("__opId, 0"))
            .ToList();

        Assert.That(logLines, Has.Count.GreaterThan(0), "Should log @p0");
        foreach (var (line, idx) in logLines)
        {
            Assert.That(IsInsideMaskGatedBlock(lines, idx), Is.True,
                "Logging for nullable conditional param should be mask-gated");
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  Test 12: Non-query (DELETE) terminal with conditional WHERE —
    //  EmitCarrierNonQueryTerminal uses the same rewritten methods
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Terminal_DeleteConditionalWhere_NonQueryTerminalIsMaskGated()
    {
        var code = GenerateInterceptors(@"
public class Svc
{
    private readonly TestDbContext _db;
    public Svc(TestDbContext db) { _db = db; }
    public async Task Run(int minAge, bool filterAge)
    {
        var q = _db.Users().Delete();
        if (filterAge)
            q = q.Where(u => u.Age > minAge);
        await q.All().ExecuteNonQueryAsync();
    }
}");
        // Check if carrier-optimized — DELETE with conditional WHERE may or may not
        // use carrier optimization depending on eligibility
        if (!code.Contains("file sealed class Chain_"))
        {
            Assert.Inconclusive("DELETE with conditional WHERE did not produce carrier-optimized path — non-query terminal not exercised");
            return;
        }

        Assert.That(code, Does.Contain("Mask |="), "Should set mask bit");

        // Non-query terminal should also mask-gate parameter binding
        var terminalBody = ExtractSection(code, "var __cmd = __c.Ctx.Connection.CreateCommand()", "return QueryExecutor.");
        if (string.IsNullOrEmpty(terminalBody))
        {
            // Try alternate section extraction for non-query
            terminalBody = ExtractSection(code, "var __cmd = __c.Ctx.Connection.CreateCommand()", "ExecuteCarrierNonQuery");
        }
        Assert.That(terminalBody, Is.Not.Empty, "Should have non-query terminal body");

        Assert.That(terminalBody, Does.Contain("__c.Mask &"),
            "Non-query terminal should mask-gate conditional parameter binding");
    }
}
