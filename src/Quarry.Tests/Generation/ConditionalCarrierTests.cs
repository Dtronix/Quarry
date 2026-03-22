using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using Quarry.Generators;

namespace Quarry.Tests.Generation;

/// <summary>
/// Tests that conditional (variable-based) chains across all query operations produce
/// carrier-optimized interceptors with bitmask dispatch.
///
/// Every test verifies:
///   1. A carrier class is emitted (file sealed class Chain_).
///   2. The chain is marked "Carrier-Optimized" in remarks.
///   3. Conditional clauses set a Mask bit (Mask |= …(1 &lt;&lt; N)).
///   4. The terminal switches on the mask to select the correct SQL variant.
/// </summary>
[TestFixture]
public class ConditionalCarrierTests
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

    /// <summary>
    /// Runs the generator and returns the interceptors source text.
    /// Fails the test if no interceptors file is generated.
    /// </summary>
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

    private static void AssertCarrierOptimizedWithMask(string code, string? expectedSql = null)
    {
        Assert.That(code, Does.Contain("file sealed class Chain_"),
            "Should emit a carrier class");
        Assert.That(code, Does.Contain("Carrier-Optimized"),
            "Should be marked Carrier-Optimized in remarks");
        Assert.That(code, Does.Contain("Mask |="),
            "Conditional clause should set a bit on the carrier Mask");
        if (expectedSql != null)
            Assert.That(code, Does.Contain(expectedSql));
    }

    /// <summary>
    /// Asserts that the generated code contains exactly <paramref name="expectedCount"/> mask switch arms.
    /// The carrier terminal emits: <c>var sql = __c.Mask switch { N =&gt; @"...", M =&gt; @"...", ... };</c>
    /// Mutually-exclusive branches may skip mask value 0 (e.g., values 1 and 2 for an if/else pair).
    /// </summary>
    private static void AssertMaskVariantCount(string code, int expectedCount)
    {
        // Count lines matching the pattern "  N => @" in the switch expression
        var armCount = code.Split('\n').Count(line => line.TrimStart().Length > 0
            && char.IsDigit(line.TrimStart()[0])
            && line.Contains("=> @\""));
        Assert.That(armCount, Is.EqualTo(expectedCount),
            $"Expected {expectedCount} mask switch arms but found {armCount}");
    }

    // ─────────────────────────────────────────────────────────────────
    //  SELECT — conditional Where
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Select_ConditionalWhere_CarrierWithMask()
    {
        var code = GenerateInterceptors(@"
public class Svc
{
    private readonly TestDbContext _db;
    public Svc(TestDbContext db) { _db = db; }
    public async Task Run(bool filter)
    {
        var q = _db.Users().Select(u => u);
        if (filter)
            q = q.Where(u => u.IsActive);
        await q.ExecuteFetchAllAsync();
    }
}
");
        AssertCarrierOptimizedWithMask(code, "SELECT");
        AssertMaskVariantCount(code, 2);
    }

    // ─────────────────────────────────────────────────────────────────
    //  SELECT — conditional OrderBy
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Select_ConditionalOrderBy_CarrierWithMask()
    {
        var code = GenerateInterceptors(@"
public class Svc
{
    private readonly TestDbContext _db;
    public Svc(TestDbContext db) { _db = db; }
    public async Task Run(bool sort)
    {
        var q = _db.Users().Where(u => u.IsActive).Select(u => u);
        if (sort)
            q = q.OrderBy(u => u.UserName);
        await q.ExecuteFetchAllAsync();
    }
}
");
        AssertCarrierOptimizedWithMask(code, "SELECT");
        AssertMaskVariantCount(code, 2);
    }

    // ─────────────────────────────────────────────────────────────────
    //  SELECT — two independent conditionals (Where + OrderBy) → 2 bits
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Select_TwoConditionals_WhereAndOrderBy_TwoBits()
    {
        var code = GenerateInterceptors(@"
public class Svc
{
    private readonly TestDbContext _db;
    public Svc(TestDbContext db) { _db = db; }
    public async Task Run(bool filter, bool sort)
    {
        var q = _db.Users().Select(u => u);
        if (filter)
            q = q.Where(u => u.IsActive);
        if (sort)
            q = q.OrderBy(u => u.UserName);
        await q.ExecuteFetchAllAsync();
    }
}
");
        AssertCarrierOptimizedWithMask(code, "SELECT");
        // Two bits → 4 SQL variants dispatched by mask value
        AssertMaskVariantCount(code, 4);
    }

    // ─────────────────────────────────────────────────────────────────
    //  SELECT — mutually exclusive OrderBy (if/else) → 1 bit
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Select_MutuallyExclusiveOrderBy_OneBit()
    {
        var code = GenerateInterceptors(@"
public class Svc
{
    private readonly TestDbContext _db;
    public Svc(TestDbContext db) { _db = db; }
    public async Task Run(bool sortByName)
    {
        var q = _db.Users().Where(u => u.IsActive).Select(u => u);
        if (sortByName)
            q = q.OrderBy(u => u.UserName);
        else
            q = q.OrderBy(u => u.Age);
        await q.ExecuteFetchAllAsync();
    }
}
");
        AssertCarrierOptimizedWithMask(code, "SELECT");
        // If/else → 1 bit, 2 mask variants
        AssertMaskVariantCount(code, 2);
    }

    // ─────────────────────────────────────────────────────────────────
    //  UPDATE Set<TValue> — conditional Where
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Update_SetValue_ConditionalWhere_CarrierWithMask()
    {
        var code = GenerateInterceptors(@"
public class Svc
{
    private readonly TestDbContext _db;
    public Svc(TestDbContext db) { _db = db; }
    public string Run(bool restrict)
    {
        var q = _db.Users().Update().Set(u => u.UserName = ""Updated"");
        if (restrict)
            q = q.Where(u => u.IsActive);
        return q.All().ToDiagnostics().Sql;
    }
}
");
        AssertCarrierOptimizedWithMask(code, "UPDATE");
        AssertMaskVariantCount(code, 2);
    }

    // ─────────────────────────────────────────────────────────────────
    //  UPDATE Set<TValue> — conditional additional Set
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Update_ConditionalAdditionalSet_CarrierWithMask()
    {
        var code = GenerateInterceptors(@"
public class Svc
{
    private readonly TestDbContext _db;
    public Svc(TestDbContext db) { _db = db; }
    public string Run(bool clearEmail)
    {
        var q = _db.Users().Update().Set(u => u.UserName = ""Updated"").Where(u => u.UserId == 1);
        if (clearEmail)
            q = q.Set(u => u.IsActive = false);
        return q.ToDiagnostics().Sql;
    }
}
");
        AssertCarrierOptimizedWithMask(code, "UPDATE");
        AssertMaskVariantCount(code, 2);
    }

    // ─────────────────────────────────────────────────────────────────
    //  UPDATE Set<TValue> — conditional Set + conditional Where → 2 bits
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Update_ConditionalSetAndWhere_TwoBits()
    {
        var code = GenerateInterceptors(@"
public class Svc
{
    private readonly TestDbContext _db;
    public Svc(TestDbContext db) { _db = db; }
    public string Run(bool deactivate, bool restrict)
    {
        var q = _db.Users().Update().Set(u => u.UserName = ""x"");
        if (deactivate)
            q = q.Set(u => u.IsActive = false);
        if (restrict)
            q = q.Where(u => u.UserId == 1);
        return q.All().ToDiagnostics().Sql;
    }
}
");
        AssertCarrierOptimizedWithMask(code, "UPDATE");
        // 2 bits → 4 SQL variants
        AssertMaskVariantCount(code, 4);
    }

    // ─────────────────────────────────────────────────────────────────
    //  UPDATE Set(Action<T>) literal — conditional Where
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Update_SetAction_Literal_ConditionalWhere_CarrierWithMask()
    {
        var code = GenerateInterceptors(@"
public class Svc
{
    private readonly TestDbContext _db;
    public Svc(TestDbContext db) { _db = db; }
    public string Run(bool restrict)
    {
        var q = _db.Users().Update().Set(u => u.UserName = ""Patched"");
        if (restrict)
            q = q.Where(u => u.IsActive);
        return q.All().ToDiagnostics().Sql;
    }
}
");
        AssertCarrierOptimizedWithMask(code, "UPDATE");
        AssertMaskVariantCount(code, 2);
    }

    // ─────────────────────────────────────────────────────────────────
    //  UPDATE Set(Action<T>) captured — conditional Where
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Update_SetAction_Captured_ConditionalWhere_CarrierWithMask()
    {
        var code = GenerateInterceptors(@"
public class Svc
{
    private readonly TestDbContext _db;
    public Svc(TestDbContext db) { _db = db; }
    public string Run(string name, bool restrict)
    {
        var q = _db.Users().Update().Set(u => u.UserName = name);
        if (restrict)
            q = q.Where(u => u.IsActive);
        return q.All().ToDiagnostics().Sql;
    }
}
");
        AssertCarrierOptimizedWithMask(code, "UPDATE");
        AssertMaskVariantCount(code, 2);
        // Captured variable should use FieldInfo extraction on carrier
        Assert.That(code, Does.Contain("GetField(\"name\")"),
            "Captured variable should be extracted from delegate.Target");
    }

    // ─────────────────────────────────────────────────────────────────
    //  UPDATE Set(Action<T>) multi-assignment — conditional Where
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Update_SetAction_MultiAssignment_ConditionalWhere_CarrierWithMask()
    {
        var code = GenerateInterceptors(@"
public class Svc
{
    private readonly TestDbContext _db;
    public Svc(TestDbContext db) { _db = db; }
    public string Run(bool restrict)
    {
        var q = _db.Users().Update().Set(u => { u.UserName = ""x""; u.IsActive = false; });
        if (restrict)
            q = q.Where(u => u.UserId == 1);
        return q.All().ToDiagnostics().Sql;
    }
}
");
        AssertCarrierOptimizedWithMask(code, "UPDATE");
        AssertMaskVariantCount(code, 2);
        // Multi-assignment should produce two SET columns
        Assert.That(code, Does.Contain("UserName"));
        Assert.That(code, Does.Contain("IsActive"));
    }

    // ─────────────────────────────────────────────────────────────────
    //  DELETE — conditional Where
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Delete_ConditionalWhere_CarrierWithMask()
    {
        var code = GenerateInterceptors(@"
public class Svc
{
    private readonly TestDbContext _db;
    public Svc(TestDbContext db) { _db = db; }
    public string Run(bool restrict)
    {
        var q = _db.Users().Delete();
        if (restrict)
            q = q.Where(u => u.IsActive == false);
        return q.All().ToDiagnostics().Sql;
    }
}
");
        AssertCarrierOptimizedWithMask(code, "DELETE");
        AssertMaskVariantCount(code, 2);
    }

    // ─────────────────────────────────────────────────────────────────
    //  DELETE — two conditional Wheres → 2 bits
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Delete_TwoConditionalWheres_TwoBits()
    {
        var code = GenerateInterceptors(@"
public class Svc
{
    private readonly TestDbContext _db;
    public Svc(TestDbContext db) { _db = db; }
    public string Run(bool onlyInactive, bool onlyOld)
    {
        var q = _db.Users().Delete();
        if (onlyInactive)
            q = q.Where(u => u.IsActive == false);
        if (onlyOld)
            q = q.Where(u => u.Age > 99);
        return q.All().ToDiagnostics().Sql;
    }
}
");
        AssertCarrierOptimizedWithMask(code, "DELETE");
        // 2 bits → 4 SQL variants
        AssertMaskVariantCount(code, 4);
    }

    // ─────────────────────────────────────────────────────────────────
    //  SELECT — conditional Where with captured parameter
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Select_ConditionalWhere_CapturedParam_CarrierWithMask()
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
}
");
        AssertCarrierOptimizedWithMask(code, "SELECT");
        AssertMaskVariantCount(code, 2);
    }

    // ─────────────────────────────────────────────────────────────────
    //  SELECT — three independent conditionals → 3 bits, 8 masks
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Select_ThreeConditionals_ThreeBits_EightMasks()
    {
        var code = GenerateInterceptors(@"
public class Svc
{
    private readonly TestDbContext _db;
    public Svc(TestDbContext db) { _db = db; }
    public async Task Run(bool filterActive, bool filterAge, bool sort)
    {
        var q = _db.Users().Select(u => u);
        if (filterActive)
            q = q.Where(u => u.IsActive);
        if (filterAge)
            q = q.Where(u => u.Age > 18);
        if (sort)
            q = q.OrderBy(u => u.UserName);
        await q.ExecuteFetchAllAsync();
    }
}
");
        AssertCarrierOptimizedWithMask(code, "SELECT");
        // 3 bits → 8 SQL variants
        AssertMaskVariantCount(code, 8);
    }

    // ─────────────────────────────────────────────────────────────────
    //  SELECT — four conditionals → 4 bits, 16 masks (max for Tier 1)
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Select_FourConditionals_FourBits_SixteenMasks()
    {
        var code = GenerateInterceptors(@"
public class Svc
{
    private readonly TestDbContext _db;
    public Svc(TestDbContext db) { _db = db; }
    public async Task Run(bool c1, bool c2, bool c3, bool c4)
    {
        var q = _db.Users().Select(u => u);
        if (c1)
            q = q.Where(u => u.IsActive);
        if (c2)
            q = q.Where(u => u.Age > 18);
        if (c3)
            q = q.OrderBy(u => u.UserName);
        if (c4)
            q = q.OrderBy(u => u.Age);
        await q.ExecuteFetchAllAsync();
    }
}
");
        AssertCarrierOptimizedWithMask(code, "SELECT");
        // 4 bits → 16 SQL variants
        AssertMaskVariantCount(code, 16);
    }

    // ─────────────────────────────────────────────────────────────────
    //  UPDATE — mixed: conditional Set + mutually exclusive Where
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Update_ConditionalSet_MutuallyExclusiveWhere_CarrierWithMask()
    {
        var code = GenerateInterceptors(@"
public class Svc
{
    private readonly TestDbContext _db;
    public Svc(TestDbContext db) { _db = db; }
    public string Run(bool deactivate, bool targetActive)
    {
        var q = _db.Users().Update().Set(u => u.UserName = ""x"");
        if (deactivate)
            q = q.Set(u => u.IsActive = false);
        if (targetActive)
            q = q.Where(u => u.IsActive);
        else
            q = q.Where(u => u.IsActive == false);
        return q.ToDiagnostics().Sql;
    }
}
");
        AssertCarrierOptimizedWithMask(code, "UPDATE");
        // 1 independent + 1 exclusive → 2 bits, 4 mask variants
        AssertMaskVariantCount(code, 4);
    }

    // ─────────────────────────────────────────────────────────────────
    //  UPDATE — execution via ExecuteNonQueryAsync (not just ToDiagnostics)
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Update_ConditionalWhere_ExecuteNonQuery_CarrierWithMask()
    {
        var code = GenerateInterceptors(@"
public class Svc
{
    private readonly TestDbContext _db;
    public Svc(TestDbContext db) { _db = db; }
    public async Task Run(bool restrict)
    {
        var q = _db.Users().Update().Set(u => u.UserName = ""x"");
        if (restrict)
            q = q.Where(u => u.IsActive);
        await q.All().ExecuteNonQueryAsync();
    }
}
");
        AssertCarrierOptimizedWithMask(code, "UPDATE");
        AssertMaskVariantCount(code, 2);
    }

    // ─────────────────────────────────────────────────────────────────
    //  DELETE — execution via ExecuteNonQueryAsync
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Delete_ConditionalWhere_ExecuteNonQuery_CarrierWithMask()
    {
        var code = GenerateInterceptors(@"
public class Svc
{
    private readonly TestDbContext _db;
    public Svc(TestDbContext db) { _db = db; }
    public async Task Run(bool restrict)
    {
        var q = _db.Users().Delete();
        if (restrict)
            q = q.Where(u => u.IsActive == false);
        await q.All().ExecuteNonQueryAsync();
    }
}
");
        AssertCarrierOptimizedWithMask(code, "DELETE");
        AssertMaskVariantCount(code, 2);
    }

    // ─────────────────────────────────────────────────────────────────
    //  UPDATE Set(Action<T>) — conditional additional Set(Action<T>) → 2 bits
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Update_SetAction_ConditionalAdditionalSetAction_TwoBits()
    {
        var code = GenerateInterceptors(@"
public class Svc
{
    private readonly TestDbContext _db;
    public Svc(TestDbContext db) { _db = db; }
    public string Run(bool deactivate, bool restrict)
    {
        var q = _db.Users().Update().Set(u => u.UserName = ""x"");
        if (deactivate)
            q = q.Set(u => u.IsActive = false);
        if (restrict)
            q = q.Where(u => u.UserId == 1);
        return q.All().ToDiagnostics().Sql;
    }
}
");
        AssertCarrierOptimizedWithMask(code, "UPDATE");
        // 2 bits → 4 SQL variants
        AssertMaskVariantCount(code, 4);
    }
}
